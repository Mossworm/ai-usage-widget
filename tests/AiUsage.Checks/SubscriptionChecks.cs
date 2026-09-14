using AiUsage;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

static class SubscriptionChecks
{
    static JsonElement Json(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
    public static async Task RunAsync(Action<bool, string> check, DateTimeOffset now)
    {
        var claude = ClaudeClient.Parse(Json("""{"five_hour":{"utilization":22.5,"resets_at":"2026-09-12T12:00:00Z"},"seven_day":{"utilization":73,"resets_at":"2026-09-18T12:00:00Z"},"seven_day_sonnet":{"utilization":99}}"""), "max", now);
        check(claude.SessionPercent == 22.5 && claude.WeeklyPercent == 73, "Claude global limits are distinct from model limits");
        check(claude.Plan == "max" && claude.SessionReset?.Offset == TimeSpan.Zero, "Claude plan and ISO reset parsed");
        check(claude.Windows is null && Labels.Windows(Catalog.Services[1], claude).Length == 2, "Without a tracked model quota Claude keeps the two global rows");
        var fable = ClaudeClient.Parse(Json("""{"five_hour":{"utilization":22.5},"seven_day":{"utilization":73},"seven_day_sonnet":{"utilization":99},"seven_day_fable":{"utilization":41,"resets_at":"2026-09-18T12:00:00Z"}}"""), "max", now);
        var fableWindows = Labels.Windows(Catalog.Services[1], fable);
        check(fableWindows.Length == 3 && fableWindows[0].Percent == 22.5 && fableWindows[1].Percent == 73, "The Fable weekly row is added after the global 5-hour and weekly rows");
        check(fableWindows[2] is { Label: "Weekly (Fable)", Percent: 41 } && fableWindows[2].Reset?.Offset == TimeSpan.Zero, "The Fable weekly quota keeps its own percentage and reset time");
        check(fable.WeeklyPercent == 73 && fableWindows.All(w => w.Percent != 99), "The Fable row never takes the Sonnet quota or replaces the global weekly one");
        // The shape a live Max account returns: seven_day_<model> keys are null and the real
        // per-model quota is a weekly_scoped row in `limits`, named by display_name.
        var live = Json("""{"five_hour":{"utilization":8},"seven_day":{"utilization":1},"seven_day_opus":null,"seven_day_sonnet":null,"limits":[{"kind":"session","group":"session","percent":8,"resets_at":"2026-09-14T16:00:00Z"},{"kind":"weekly_all","group":"weekly","percent":1,"resets_at":"2026-09-19T00:00:00Z"},{"kind":"weekly_scoped","group":"weekly","percent":37,"resets_at":"2026-09-19T00:00:00Z","scope":{"model":{"id":null,"display_name":"Fable"},"surface":null}}]}""");
        var scoped = Labels.Windows(Catalog.Services[1], ClaudeClient.Parse(live, "max", now));
        check(scoped.Length == 3 && scoped[0].Percent == 8 && scoped[1].Percent == 1, "A live-shaped response keeps the global 5-hour and weekly rows");
        check(scoped[2] is { Label: "Weekly (Fable)", Percent: 37 } && scoped[2].Reset?.Offset == TimeSpan.Zero, "The scoped weekly quota in limits becomes the Fable row");
        check(ClaudeClient.ScopedModelNames(live) is ["Fable"], "Per-model quota names are reported for configuration");
        var unscoped = ClaudeClient.Parse(Json("""{"five_hour":{"utilization":8},"seven_day":{"utilization":1},"limits":[{"kind":"weekly_scoped","percent":50,"scope":{"model":{"display_name":"Sonnet"}}}]}"""), "max", now);
        check(unscoped.Windows is null, "A weekly_scoped row for another model adds no Fable row");
        var spelled = ClaudeClient.Parse(Json("""{"five_hour":{"utilization":1},"seven_day":{"utilization":2},"seven-day-Fable-5":{"utilization":3}}"""), null, now);
        check(Labels.Windows(Catalog.Services[1], spelled) is [_, _, { Percent: 3 }], "A differently spelled Fable window key still matches");
        var emptyFable = ClaudeClient.Parse(Json("""{"five_hour":{"utilization":1},"seven_day":{"utilization":2},"seven_day_fable":{"utilization":null}}"""), null, now);
        check(emptyFable.Windows is null, "A Fable window with no usable number adds no empty row");
        var unknown = ClaudeClient.Parse(Json("""{"five_hour":null,"seven_day":null}"""), null, now);
        check(unknown.SessionPercent is null && unknown.Status is not null, "Claude missing quota is not zero");
        var invalid = ClaudeClient.Parse(Json("""{"five_hour":{"utilization":101,"resets_at":"invalid"},"seven_day":null}"""), null, now);
        check(invalid.SessionPercent is null && invalid.SessionReset is null, "Claude invalid fields rejected");
        using var card = JsonDocument.Parse(Card.Render(new() { Settings = true }, new(null, []), now));
        var cardText = card.RootElement.ToString();
        check(!cardText.Contains("aiusage:") && !cardText.Contains("Connect"), "Settings carries no login entry point of its own");

        var dir = Path.Combine(Path.GetTempPath(), "SubscriptionChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "credentials.json");
            var count = 0;
            using var transport = new FakeHandler(async request => {
                count++;
                check(request.RequestUri!.AbsoluteUri == "https://api.anthropic.com/api/oauth/usage", "Claude token sent only to Anthropic usage endpoint");
                check(request.Headers.Authorization?.Parameter == "fake-claude-token" && request.Method == HttpMethod.Get, "Claude uses read-only authenticated GET");
                check(request.Headers.UserAgent.ToString() == "claude-code/0.2.29", "The usage call identifies itself as the Claude Code CLI");
                await Task.CompletedTask;
                return Response("""{"five_hour":{"utilization":25},"seven_day":{"utilization":0}}""");
            });
            using var http = new HttpClient(transport);
            try { await new ClaudeClient(http, path).FetchAsync(); check(false, "Missing credentials must fail"); }
            catch (UsageConnectionException e) { check(count == 0 && e.Message.Contains("sign in with Claude Code"), "A missing credential file asks for a Claude Code sign-in without a request"); }
            var auth = """{"claudeAiOauth":{"accessToken":"fake-claude-token","subscriptionType":"pro","expiresAt":9999999999999}}""";
            await File.WriteAllTextAsync(path, auth);
            var result = await new ClaudeClient(http, path).FetchAsync();
            check(result.SessionPercent == 25 && result.WeeklyPercent == 0 && !JsonSerializer.Serialize(result).Contains("fake-claude-token"), "Display data excludes Claude credentials and preserves real zero");
            check(await File.ReadAllTextAsync(path) == auth, "Claude credential file never modified");
            await File.WriteAllTextAsync(path, """{"claudeAiOauth":{"accessToken":"fake","expiresAt":1}}""");
            try { await new ClaudeClient(http, path).FetchAsync(); check(false, "Expired credential must fail"); }
            catch (UsageConnectionException e) { check(count == 1 && e.Message.Contains("open Claude Code"), "An expired credential points at Claude Code instead of a Connect button"); }

            using var rateLimited = new HttpClient(new FakeHandler(_ => {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(20));
                return Task.FromResult(response);
            }));
            await File.WriteAllTextAsync(path, auth);
            try { await new ClaudeClient(rateLimited, path).FetchAsync(); check(false, "429 must fail"); }
            catch (UsageConnectionException e) { check(e.RetryAfter == TimeSpan.FromMinutes(20), "Server Retry-After preserved"); }
            using var denied = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("secret-server-error") })));
            try { await new ClaudeClient(denied, path).FetchAsync(); check(false, "401 must fail"); }
            catch (UsageConnectionException e) { check(!e.Message.Contains("secret-server-error") && e.Message.Contains("sign in again"), "Auth errors sanitized and actionable"); }

            check(ClaudeClient.DefaultCredentialsPath().EndsWith(Path.Combine(".claude", ".credentials.json")), "The CLI credential file is the single Claude source");

        } finally { foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); }
    }
    static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
