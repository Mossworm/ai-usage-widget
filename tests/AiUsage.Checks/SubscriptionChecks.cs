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
        check(SubscriptionLogin.FromArgument("aiusage:login-opencode") == "opencode" && SubscriptionLogin.FromArgument("aiusage:login-commandcode") == "commandcode", "New providers route to browser login actions");
        check(SubscriptionLogin.FromArgument("aiusage:login-gemini") is null, "Removed provider protocol URL is rejected");
        check(SubscriptionLogin.FromArgument("aiusage:login-gemini?command=evil") is null, "Protocol does not accept arbitrary commands");
        var opencode = OpenCodeClient.Parse(Json("""{"usage":{"rolling":{"usagePercent":25,"resetInSec":600},"weekly":{"usagePercent":40,"resetInSec":3600}}}"""), now);
        check(opencode.Id == "opencode" && opencode.Plan == "Go" && opencode.SessionPercent == 25 && opencode.WeeklyPercent == 40 && opencode.SessionReset == now.AddMinutes(10), "OpenCode usage API maps rolling and weekly windows");
        var opencodeNew = OpenCodeClient.Parse(Json("""{"usage":{"rolling":{"percent":10,"resetsAt":"2026-09-12T14:00:00Z"},"monthly":{"percent":27,"resetsAt":"2026-09-18T12:00:00Z"}}}"""), now);
        check(opencodeNew.Plan == "Go" && opencodeNew.SessionPercent == 10 && opencodeNew.WeeklyPercent == 27 && opencodeNew.SessionReset?.Offset == TimeSpan.Zero, "OpenCode percent/resetsAt shape falls back to monthly for weekly");
        try { OpenCodeClient.Parse(Json("""{"usage":{"rolling":null,"weekly":null}}"""), now); check(false, "Empty OpenCode usage must fail"); }
        catch (UsageConnectionException e) { check(e.Message.Contains("response format"), "Empty OpenCode usage is explicit, not zero"); }
        var commandCode = CommandCodeClient.Parse(
            Json("""{"org":{"id":"org_123"}}"""),
            Json("""{"credits":{"monthlyCredits":8.0,"purchasedCredits":2.0,"freeCredits":0},"windowLimits":{"fiveHour":{"used":4.0,"cap":16.0,"resetAt":"2026-09-12T16:00:00Z"},"weekly":{"used":14.0,"cap":40.0,"resetAt":"2026-09-18T12:00:00Z"}}}"""),
            Json("""{"totalCost":2.0}"""),
            Json("""{"success":true,"data":{"planId":"individual-go","currentPeriodEnd":"2026-10-01T00:00:00Z"}}"""), now);
        check(commandCode.Id == "commandcode" && commandCode.Plan == "Go" && commandCode.SessionPercent == 25 && commandCode.WeeklyPercent == 35, "Command Code billing payload maps plan and 5-hour/weekly windows");
        check(CommandCodeClient.NormalizePlan("goat-monthly") == "Goat" && CommandCodeClient.NormalizePlan("pro-monthly") == "Pro" && CommandCodeClient.NormalizePlan("individual-go") == "Go", "Command Code plans normalized to Go, Goat, Pro");
        var commandMonthly = CommandCodeClient.Parse(
            Json("""{"org":{"id":"org_123"}}"""),
            Json("""{"credits":{"monthlyCredits":8.0,"purchasedCredits":2.0,"freeCredits":0},"windowLimits":{"fiveHour":{"used":1.6,"cap":16.0,"resetAt":"2026-09-12T16:00:00Z"}}}"""),
            Json("""{"totalCost":2.0}"""),
            Json("""{"success":true,"data":{"planId":"pro"}}"""), now);
        check(commandMonthly.SessionPercent == 10 && commandMonthly.WeeklyPercent is not null && Math.Abs(commandMonthly.WeeklyPercent.Value - 100.0 * 2 / 12) < 0.001 && commandMonthly.Plan == "Pro", "Command Code weekly falls back to monthly billing period");
        try {
            CommandCodeClient.Parse(Json("""{"org":{"id":"org_123"}}"""), Json("""{"credits":{}}"""), Json("""{}"""), Json("""{}"""), now);
            check(false, "Empty Command Code usage must fail");
        } catch (UsageConnectionException e) { check(e.Message.Contains("response format"), "Empty Command Code usage is explicit, not zero"); }
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

            var openCodeAuth = Path.Combine(dir, "opencode-auth.json");
            try { await new OpenCodeClient(http, Path.Combine(dir, "missing-opencode.json")).FetchAsync(); check(false, "Missing OpenCode credentials must fail"); }
            catch (UsageConnectionException e) { check(e.Message == "OpenCode login required · connect in Settings", "Missing OpenCode credentials ask for login without network"); }
            await File.WriteAllTextAsync(openCodeAuth, """{"opencode-go":{"type":"api","key":"fake-opencode-key"}}""");
            using var openCodeTransport = new FakeHandler(async request => {
                check(request.RequestUri!.AbsoluteUri == "https://opencode.ai/zen/go/v1/usage", "OpenCode token sent only to Go usage endpoint");
                check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization?.Parameter == "fake-opencode-key", "OpenCode uses read-only Bearer GET");
                await Task.CompletedTask;
                return Response("""{"usage":{"rolling":{"percent":10,"resetsAt":"2026-09-12T14:00:00Z"},"weekly":{"percent":27,"resetsAt":"2026-09-18T12:00:00Z"}}}""");
            });
            using var openCodeHttp = new HttpClient(openCodeTransport);
            var openCodeLive = await new OpenCodeClient(openCodeHttp, openCodeAuth).FetchAsync();
            check(openCodeLive.Plan == "Go" && openCodeLive.SessionPercent == 10 && openCodeLive.WeeklyPercent == 27 && !JsonSerializer.Serialize(openCodeLive).Contains("fake-opencode-key"), "OpenCode auth.json key used and excluded from display");
            check((await File.ReadAllTextAsync(openCodeAuth)).Contains("fake-opencode-key"), "OpenCode credential file never modified");

            var commandAuth = Path.Combine(dir, "commandcode-auth.json");
            try { await new CommandCodeClient(http, Path.Combine(dir, "missing-commandcode.json")).FetchAsync(); check(false, "Missing Command Code credentials must fail"); }
            catch (UsageConnectionException e) { check(e.Message == "Command Code login required · connect in Settings", "Missing Command Code credentials ask for login without network"); }
            await File.WriteAllTextAsync(commandAuth, """{"apiKey":"fake-cc-key"}""");
            using var commandTransport = new FakeHandler(async request => {
                check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization?.Parameter == "fake-cc-key", "Command Code uses read-only Bearer GET");
                await Task.CompletedTask;
                var url = request.RequestUri!.AbsoluteUri;
                if (url.Contains("/alpha/whoami")) return Response("""{"org":{"id":"org_123"}}""");
                if (url.Contains("/alpha/billing/credits")) return Response("""{"credits":{"monthlyCredits":8.0,"purchasedCredits":2.0},"windowLimits":{"fiveHour":{"used":4.0,"cap":16.0,"resetAt":"2026-09-12T16:00:00Z"},"weekly":{"used":14.0,"cap":40.0,"resetAt":"2026-09-18T12:00:00Z"}}}""");
                if (url.Contains("/alpha/usage/summary")) return Response("""{"totalCost":2.0}""");
                return Response("""{"success":true,"data":{"planId":"goat-monthly"}}""");
            });
            using var commandHttp = new HttpClient(commandTransport);
            var commandLive = await new CommandCodeClient(commandHttp, commandAuth).FetchAsync();
            check(commandLive.Plan == "Goat" && commandLive.SessionPercent == 25 && commandLive.WeeklyPercent == 35 && !JsonSerializer.Serialize(commandLive).Contains("fake-cc-key"), "Command Code auth.json key used and excluded from display");
            check((await File.ReadAllTextAsync(commandAuth)).Contains("fake-cc-key"), "Command Code credential file never modified");

        } finally { foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); }
    }
    static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
