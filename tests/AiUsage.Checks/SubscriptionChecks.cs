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
        var unknown = ClaudeClient.Parse(Json("""{"five_hour":null,"seven_day":null}"""), null, now);
        check(unknown.SessionPercent is null && unknown.Status is not null, "Claude missing quota is not zero");
        var invalid = ClaudeClient.Parse(Json("""{"five_hour":{"utilization":101,"resets_at":"invalid"},"seven_day":null}"""), null, now);
        check(invalid.SessionPercent is null && invalid.SessionReset is null, "Claude invalid fields rejected");
        check(SubscriptionLogin.FromArgument("aiusage:login-claude") == "claude", "Claude protocol URL routes to Claude login");
        check(SubscriptionLogin.FromArgument("aiusage:login-antigravity") == "antigravity" && SubscriptionLogin.FromArgument("aiusage:login-cursor") == "cursor" && SubscriptionLogin.FromArgument("aiusage:login-opencode") == "opencode" && SubscriptionLogin.FromArgument("aiusage:login-commandcode") == "commandcode", "New providers route to browser login actions");
        check(SubscriptionLogin.FromArgument("aiusage:login-gemini") is null, "Removed provider protocol URL is rejected");
        check(SubscriptionLogin.FromArgument("aiusage:login-gemini?command=evil") is null, "Protocol does not accept arbitrary commands");
        var antigravity = AntigravityClient.Parse(Json("""{"buckets":[{"modelId":"gemini-pro","remainingFraction":0.8,"resetTime":"2026-09-13T16:00:00Z"},{"modelId":"claude-sonnet","remainingFraction":0.5,"resetTime":"2026-09-14T12:00:00Z"}]}"""), "Google AI Pro", now);
        check(antigravity.Id == "antigravity" && antigravity.Windows?.Length == 2 && Math.Abs(antigravity.Windows[0].Percent!.Value - 20) < 0.001, "Antigravity model quota maps to named windows");
        var cursor = CursorClient.Parse(Json("""{"membershipType":"pro","billingCycleEnd":"2026-10-01T00:00:00Z","individualUsage":{"plan":{"used":1500,"limit":5000,"totalPercentUsed":30}}}"""), now);
        check(cursor.Id == "cursor" && cursor.SessionPercent == 30 && cursor.SessionReset?.Month == 10, "Cursor usage summary maps included usage and billing reset");
        var opencode = OpenCodeClient.Parse(Json("""{"usage":{"rolling":{"usagePercent":25,"resetInSec":600},"weekly":{"usagePercent":40,"resetInSec":3600}}}"""), now);
        check(opencode.Id == "opencode" && opencode.SessionPercent == 25 && opencode.WeeklyPercent == 40 && opencode.SessionReset == now.AddMinutes(10), "OpenCode usage API maps rolling and weekly windows");
        var commandCode = CommandCodeClient.Parse(Json("""{"credits":{"monthlyCredits":8.7784}}"""), Json("""{"success":true,"data":{"planId":"individual-go","currentPeriodEnd":"2026-10-01T00:00:00Z"}}"""), now);
        check(commandCode.Id == "commandcode" && commandCode.Plan == "individual-go" && commandCode.Windows?.Any(x => x.Label == "Monthly credits") == true, "Command Code billing payload maps plan and credits");
        using var card = JsonDocument.Parse(Card.Render(new() { Settings = true }, new(null, []), now));
        var cardText = card.RootElement.ToString();
        check(cardText.Contains("aiusage:login") && cardText.Contains("aiusage:login-claude") && !cardText.Contains("login-gemini"), "Settings exposes only supported row connections");

        var dir = Path.Combine(Path.GetTempPath(), "SubscriptionChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var path = Path.Combine(dir, "credentials.json");
            var count = 0;
            using var transport = new FakeHandler(async request => {
                count++;
                check(request.RequestUri!.AbsoluteUri == "https://api.anthropic.com/api/oauth/usage", "Claude token sent only to Anthropic usage endpoint");
                check(request.Headers.Authorization?.Parameter == "fake-claude-token" && request.Method == HttpMethod.Get, "Claude uses read-only authenticated GET");
                await Task.CompletedTask;
                return Response("""{"five_hour":{"utilization":25},"seven_day":{"utilization":0}}""");
            });
            using var http = new HttpClient(transport);
            try { await new ClaudeClient(http, path).FetchAsync(); check(false, "Missing credentials must fail"); }
            catch (UsageConnectionException) { check(count == 0, "Missing Claude credentials never trigger network"); }
            var auth = """{"claudeAiOauth":{"accessToken":"fake-claude-token","subscriptionType":"pro","expiresAt":9999999999999}}""";
            await File.WriteAllTextAsync(path, auth);
            var result = await new ClaudeClient(http, path).FetchAsync();
            check(result.SessionPercent == 25 && result.WeeklyPercent == 0 && !JsonSerializer.Serialize(result).Contains("fake-claude-token"), "Display data excludes Claude credentials and preserves real zero");
            check(await File.ReadAllTextAsync(path) == auth, "Claude credential file never modified");
            await File.WriteAllTextAsync(path, """{"claudeAiOauth":{"accessToken":"fake","expiresAt":1}}""");
            try { await new ClaudeClient(http, path).FetchAsync(); check(false, "Expired credential must fail"); }
            catch (UsageConnectionException) { check(count == 1, "Expired Claude credential does not make a request"); }

            using var rateLimited = new HttpClient(new FakeHandler(_ => {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(20));
                return Task.FromResult(response);
            }));
            await File.WriteAllTextAsync(path, auth);
            try { await new ClaudeClient(rateLimited, path).FetchAsync(); check(false, "429 must fail"); }
            catch (UsageConnectionException e) { check(e.RetryAfter == TimeSpan.FromMinutes(20), "Server Retry-After preserved"); }
            using var denied = new HttpClient(new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("secret-server-error") })));
            try { await new ClaudeClient(denied, path).FetchAsync(); check(false, "401 must fail"); }
            catch (UsageConnectionException e) { check(!e.Message.Contains("secret-server-error") && e.Message.Contains("reconnect"), "Auth errors sanitized and actionable"); }

        } finally { foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); }
    }
    static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
