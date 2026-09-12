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
        var gemini = GeminiClient.Parse(Json("""{"buckets":[{"modelId":"gemini-2.5-pro","remainingFraction":0.7,"resetTime":"2026-09-13T01:00:00Z"},{"modelId":"gemini-3-pro","remainingFraction":0.2,"resetTime":"2026-09-13T02:00:00Z"},{"modelId":"gemini-3-flash","remainingFraction":0.9,"resetTime":"2026-09-13T03:00:00Z"}]}"""), "Google AI Pro", now);
        check(gemini.Windows?.Length == 2 && gemini.Windows[0].Label == "gemini-3-pro" && gemini.Windows[0].Percent == 80, "Gemini selects most depleted Pro model without summing quotas");
        check(Math.Abs(gemini.Windows![1].Percent!.Value - 10) < 0.001, "Gemini remaining fraction converted to used percent");
        check(gemini.SessionPercent is null && gemini.WeeklyPercent is null, "Gemini model quota never masquerades as five-hour or weekly");
        check(gemini.Windows[0].Reset?.Hour == 2 && gemini.Windows[1].Reset?.Hour == 3, "Gemini model reset times remain independent");
        var empty = GeminiClient.Parse(Json("""{"buckets":[{"modelId":"x","remainingFraction":2},{"modelId":"y","remainingAmount":"55"}]}"""), null, now);
        check(empty.Windows!.Length == 0 && empty.Status is not null, "Gemini invalid fractions or unknown total are not invented");
        check(Labels.Windows(Catalog.Services.First(x => x.Id == "gemini"), null)[0].Label == "모델 한도", "Disconnected Gemini has honest model label");
        check(SubscriptionLogin.FromArgument("aiusage:login-claude") == "claude" && SubscriptionLogin.FromArgument("aiusage:login-gemini") == "gemini", "New protocol URLs route to correct login");
        check(SubscriptionLogin.FromArgument("aiusage:login-gemini?command=evil") is null, "Protocol does not accept arbitrary commands");
        using var card = JsonDocument.Parse(Card.Render(new() { Settings = true }, new(null, []), now));
        var body = card.RootElement.GetProperty("body");
        var loginActions = body.EnumerateArray().Where(x => x.GetProperty("type").GetString() == "ActionSet").Skip(1)
            .SelectMany(x => x.GetProperty("actions").EnumerateArray()).ToArray();
        check(loginActions.Length == 3, "Settings exposes all three login actions");

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
            catch (UsageConnectionException e) { check(!e.Message.Contains("secret-server-error") && e.Message.Contains("다시 연결"), "Auth errors sanitized and actionable"); }

            var geminiAuth = """{"access_token":"expired-token","refresh_token":"fake-google-refresh","expiry_date":1}""";
            await File.WriteAllTextAsync(path, geminiAuth);
            var methods = new List<string>();
            using var google = new HttpClient(new FakeHandler(async request => {
                methods.Add(request.RequestUri!.AbsoluteUri);
                if (methods.Count == 1) {
                    check(request.RequestUri.Host == "oauth2.googleapis.com" && request.Headers.Authorization is null, "Google refresh goes only to OAuth token endpoint");
                    check((await request.Content!.ReadAsStringAsync()).Contains("grant_type=refresh_token"), "Google refresh uses OAuth grant");
                    return Response("""{"access_token":"new-google-token","expires_in":3600}""");
                }
                check(request.RequestUri.Host == "cloudcode-pa.googleapis.com" && request.Headers.Authorization?.Parameter == "new-google-token", "Google quota requests use refreshed token at Google host");
                if (methods.Count == 2) return Response("""{"cloudaicompanionProject":{"id":"test-project"},"paidTier":{"name":"Google AI Pro"}}""");
                check((await request.Content!.ReadAsStringAsync()).Contains("test-project"), "Project discovered from Code Assist response");
                return Response("""{"buckets":[{"modelId":"gemini-pro","remainingFraction":0.5}]}""");
            }));
            var googleUsage = await new GeminiClient(google, path).FetchAsync();
            check(methods.Count == 3 && googleUsage.Plan == "Google AI Pro" && googleUsage.Windows![0].Percent == 50, "Gemini refresh, account discovery, quota pipeline succeeds");
            check(await File.ReadAllTextAsync(path) == geminiAuth && !JsonSerializer.Serialize(googleUsage).Contains("token"), "Gemini credentials never overwritten or included in display data");
        } finally { foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); }
    }
    static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
