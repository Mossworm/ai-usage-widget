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
        check(SubscriptionLogin.FromArgument("aiusage:login-claude") == "claude", "Claude protocol URL routes to Claude login");
        check(SubscriptionLogin.RequiresCode("claude") && !SubscriptionLogin.RequiresCode("chatgpt"), "Only Claude finishes login with a pasted code");
        check(SubscriptionLogin.FromArgument("aiusage:login-gemini") is null, "Removed provider protocol URL is rejected");
        check(SubscriptionLogin.FromArgument("aiusage:login-gemini?command=evil") is null, "Protocol does not accept arbitrary commands");
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

            await ClaudeOAuthChecksAsync(check, dir, now);

        } finally { foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); }
    }
    static async Task ClaudeOAuthChecksAsync(Action<bool, string> check, string dir, DateTimeOffset now)
    {
        var start = ClaudeOAuth.Start();
        var url = new Uri(start.Url);
        var query = Query(url);
        check(url.GetLeftPart(UriPartial.Path) == "https://claude.ai/oauth/authorize" && query["response_type"] == "code", "Claude login opens the Anthropic authorize page");
        check(query["code_challenge_method"] == "S256" && query["code_challenge"].Length > 0 && query["state"].Length > 0, "Claude login uses PKCE S256 with a state value");
        check(!start.Url.Contains("client_secret") && query["code_challenge"] != query["state"] && query["code_challenge"] != start.Url, "Authorize request carries no secret and a challenge distinct from state");
        check(query["redirect_uri"] == ClaudeOAuth.RedirectUri && query["client_id"] == ClaudeOAuth.ClientId, "Authorize request declares the redirect and client it will exchange with");
        check(Query(new Uri(ClaudeOAuth.Start().Url))["code_challenge"] != query["code_challenge"], "Each Claude login generates fresh PKCE material");

        check(ClaudeOAuth.ParseResponse("abc#xyz") == ("abc", "xyz"), "Pasted CODE#STATE splits into code and state");
        check(ClaudeOAuth.ParseResponse("  abc  ") == ("abc", null), "A bare pasted code is accepted");
        check(ClaudeOAuth.ParseResponse("https://platform.claude.com/oauth/code/callback?code=a%2Bb&state=s") == ("a+b", "s"), "A pasted callback link is decoded");
        foreach (var bad in new[] { "", "   ", "#state" })
            try { ClaudeOAuth.ParseResponse(bad); check(false, "Empty pasted code must fail"); } catch (UsageConnectionException) { }
        try { await ClaudeOAuth.CompleteAsync(start, "code#wrong-state"); check(false, "State mismatch must fail"); }
        catch (UsageConnectionException e) { check(e.Message.Contains("does not match"), "A mismatched login response is rejected before any network call"); }

        var storePath = Path.Combine(dir, "claude-auth.dat");
        var store = new ClaudeTokenStore(storePath);
        check(store.Read() is null, "A missing Claude token store reads as not connected");
        // Refresh decisions run off the real clock, so a token meant to be live needs a real expiry.
        var live = DateTimeOffset.UtcNow.AddHours(1);
        store.Save(new("stored-access", "stored-refresh", live, "max"));
        check(!File.ReadAllText(storePath).Contains("stored-access"), "Stored Claude tokens are encrypted at rest");
        var read = store.Read();
        check(read?.AccessToken == "stored-access" && read.RefreshToken == "stored-refresh" && read.Plan == "max", "Stored Claude tokens round-trip");
        check(read is not null && !read.IsExpired(live.AddHours(-1)) && read.IsExpired(live.AddHours(1)), "Token expiry is evaluated against the stored time");

        var requests = new List<string>();
        using var storeHttp = new HttpClient(new FakeHandler(async request => {
            requests.Add(request.RequestUri!.AbsoluteUri);
            await Task.CompletedTask;
            return Response("""{"five_hour":{"utilization":12},"seven_day":{"utilization":30}}""");
        }));
        var usage = await new ClaudeClient(storeHttp, null, store).FetchAsync();
        check(usage.SessionPercent == 12 && usage.Plan == "max", "The app's own Claude login serves usage without the CLI");
        check(requests is ["https://api.anthropic.com/api/oauth/usage"], "A valid stored token goes straight to the usage endpoint");
        check(ClaudeClient.DefaultCredentialsPath().EndsWith(Path.Combine(".claude", ".credentials.json")), "The CLI credential file stays the documented fallback source");

        store.Save(new("old-access", null, DateTimeOffset.UtcNow.AddYears(-1), "max"));
        try { await new ClaudeClient(storeHttp, null, store).FetchAsync(); check(false, "Expired token without refresh must fail"); }
        catch (UsageConnectionException e) { check(e.Message.Contains("reconnect") && requests.Count == 1, "An expired token with no refresh token asks for reconnect without a usage call"); }

        var refreshed = new List<string>();
        using var refreshHttp = new HttpClient(new FakeHandler(async request => {
            var target = request.RequestUri!.AbsoluteUri;
            refreshed.Add(target);
            await Task.CompletedTask;
            if (target.Contains("/oauth/token")) {
                check(request.Method == HttpMethod.Post && (await request.Content!.ReadAsStringAsync()).Contains("\"grant_type\":\"refresh_token\""), "Refresh uses a POST with the refresh grant");
                return Response("""{"access_token":"new-access","refresh_token":"new-refresh","expires_in":3600}""");
            }
            check(request.Headers.Authorization?.Parameter == "new-access", "The refreshed token is used for the usage call");
            return Response("""{"five_hour":{"utilization":5},"seven_day":{"utilization":6}}""");
        }));
        store.Save(new("stale-access", "stale-refresh", DateTimeOffset.UtcNow.AddYears(-1), "pro"));
        var afterRefresh = await new ClaudeClient(refreshHttp, null, store).FetchAsync();
        check(afterRefresh.SessionPercent == 5 && afterRefresh.Plan == "pro", "An expired login refreshes itself and keeps serving usage");
        var rotated = store.Read();
        check(rotated?.AccessToken == "new-access" && rotated.RefreshToken == "new-refresh" && rotated.Plan == "pro", "Rotated tokens are saved and the plan is preserved");
        check(refreshed[0].EndsWith("/v1/oauth/token"), "Refresh hits the token endpoint before the usage endpoint");
        check(!System.Text.Json.JsonSerializer.Serialize(afterRefresh).Contains("new-access"), "Display data excludes the refreshed token");
        store.Clear();
        check(store.Read() is null && !File.Exists(storePath), "Disconnecting removes the stored Claude login");
    }
    static Dictionary<string, string> Query(Uri url)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) {
            var separator = pair.IndexOf('=');
            if (separator > 0) result[pair[..separator]] = Uri.UnescapeDataString(pair[(separator + 1)..]);
        }
        return result;
    }
    static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
