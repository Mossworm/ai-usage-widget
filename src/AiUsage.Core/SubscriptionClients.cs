using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using static AiUsage.CodexClient;

namespace AiUsage;

public sealed class UsageConnectionException(string message, TimeSpan? retryAfter = null) : Exception(message)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

internal static class UsageHttp
{
    // Tokens are sent only to fixed provider endpoints; never follow a redirect with credentials.
    public static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) {
        Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 2_000_000
    };
    public static async Task<JsonElement> SendAsync(HttpClient client, HttpRequestMessage request, string service, CancellationToken token)
    {
        using var response = await client.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMinutes(15);
            throw new UsageConnectionException($"{service} rate limited · retrying later", TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 300, 86400)));
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UsageConnectionException($"Check {service} authentication and account access · reconnect in Settings");
        if (!response.IsSuccessStatusCode) throw new UsageConnectionException($"{service} request failed (HTTP {(int)response.StatusCode})");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new UsageConnectionException($"Check the {service} response format");
        return json.RootElement.Clone();
    }
    public static async Task<JsonElement> CredentialsAsync(string path, string service, CancellationToken token)
    {
        if (!File.Exists(path)) throw new UsageConnectionException($"{service} login required · connect in Settings");
        if (new FileInfo(path).Length > 262144) throw new UsageConnectionException($"Check the {service} credential file format");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
        return json.RootElement.Clone();
    }
    public static DateTimeOffset? Date(JsonElement value, string property) => DateTimeOffset.TryParse(String(value, property), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    public static double? Percentage(JsonElement value, string property) => Get(value, property).TryNumber(out var n) && n >= 0 && n <= 100 ? n : null;
}

public sealed class ClaudeClient(HttpClient? http = null, string? credentialsPath = null)
{
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        var folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        var root = await UsageHttp.CredentialsAsync(credentialsPath ?? Path.Combine(folder, ".credentials.json"), "Claude", cancellation);
        var oauth = Get(root, "claudeAiOauth");
        var access = String(oauth, "accessToken");
        if (string.IsNullOrWhiteSpace(access)) throw new UsageConnectionException("Claude subscription login required · connect in Settings");
        if (Get(oauth, "expiresAt").TryNumber(out var expires) && expires <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new UsageConnectionException("Claude login expired · reconnect in Settings");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd("AiUsageWidget/1.2.0");
        var result = await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "Claude", cancellation);
        return Parse(result, String(oauth, "subscriptionType"), DateTimeOffset.Now);
    }
    public static UsageEntry Parse(JsonElement result, string? plan, DateTimeOffset now)
    {
        if (!result.TryGetProperty("five_hour", out _) && !result.TryGetProperty("seven_day", out _))
            throw new UsageConnectionException("Check the Claude quota response format");
        var session = Get(result, "five_hour"); var weekly = Get(result, "seven_day");
        var first = UsageHttp.Percentage(session, "utilization"); var second = UsageHttp.Percentage(weekly, "utilization");
        return new("claude", plan, first, UsageHttp.Date(session, "resets_at"), second, UsageHttp.Date(weekly, "resets_at"),
            Status: first is null && second is null ? "No Claude quota data" : null, UpdatedAt: now);
    }
}

public sealed class AntigravityClient(HttpClient? http = null, string? credentialsPath = null)
{
    const string Endpoint = "https://cloudcode-pa.googleapis.com/v1internal:";
    const string ClientId = "681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com";
    const string ClientSecret = "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";
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var credentials = await CredentialsAsync(token);
        var access = String(credentials, "access_token");
        if (string.IsNullOrWhiteSpace(access) || !Get(credentials, "expiry_date").TryNumber(out var expiry) || expiry < DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds())
            access = await RefreshAsync(credentials, token);
        var project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT_ID");
        var loaded = await PostAsync("loadCodeAssist", access, new { cloudaicompanionProject = project, metadata = new { ideType = "IDE_UNSPECIFIED", platform = "PLATFORM_UNSPECIFIED", pluginType = "ANTIGRAVITY" } }, token);
        var projectValue = Get(loaded, "cloudaicompanionProject");
        project = projectValue.ValueKind == JsonValueKind.String ? projectValue.GetString() : String(projectValue, "id") ?? project;
        if (string.IsNullOrWhiteSpace(project)) throw new UsageConnectionException("Antigravity account setup required · sign in and try again");
        var tier = Get(loaded, "paidTier");
        if (tier.ValueKind != JsonValueKind.Object) tier = Get(loaded, "currentTier");
        var quota = await PostAsync("retrieveUserQuota", access, new { project }, token);
        return Parse(quota, String(tier, "name") ?? String(tier, "id"), DateTimeOffset.Now);
    }
    async Task<JsonElement> CredentialsAsync(CancellationToken token)
    {
        var inline = Environment.GetEnvironmentVariable("ANTIGRAVITY_OAUTH_CREDENTIALS_JSON");
        if (!string.IsNullOrWhiteSpace(inline)) return JsonDocument.Parse(inline).RootElement.Clone();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = credentialsPath ?? new[] {
            Path.Combine(home, ".codexbar", "antigravity", "oauth_creds.json"),
            Path.Combine(home, ".gemini", "oauth_creds.json")
        }.FirstOrDefault(File.Exists);
        if (path is null) throw new UsageConnectionException("Antigravity login required · set ANTIGRAVITY_OAUTH_CREDENTIALS_JSON or connect the account");
        return await UsageHttp.CredentialsAsync(path, "Antigravity", token);
    }
    async Task<string> RefreshAsync(JsonElement credentials, CancellationToken token)
    {
        var refresh = String(credentials, "refresh_token");
        if (string.IsNullOrWhiteSpace(refresh)) throw new UsageConnectionException("Antigravity login expired · reconnect the account");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token") {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = ClientId, ["client_secret"] = ClientSecret, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token" })
        };
        var result = await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "Antigravity authentication", token);
        return String(result, "access_token") ?? throw new UsageConnectionException("Could not refresh Antigravity login");
    }
    async Task<JsonElement> PostAsync(string method, string access, object payload, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + method) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.UserAgent.ParseAdd("Antigravity");
        return await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "Antigravity", token);
    }
    public static UsageEntry Parse(JsonElement result, string? plan, DateTimeOffset now)
    {
        var buckets = Get(result, "buckets");
        if (buckets.ValueKind != JsonValueKind.Array) throw new UsageConnectionException("Check the Antigravity quota response format");
        var windows = buckets.EnumerateArray().Select(bucket => {
            var label = String(bucket, "modelId") ?? "Model quota";
            var fraction = Get(bucket, "remainingFraction");
            return fraction.TryNumber(out var value) && value is >= 0 and <= 1 ? new UsageWindow(label, (1 - value) * 100, UsageHttp.Date(bucket, "resetTime")) : null;
        }).OfType<UsageWindow>().Take(4).ToArray();
        return new("antigravity", plan, Status: windows.Length == 0 ? "No Antigravity quota data" : null, UpdatedAt: now, Windows: windows);
    }
}

public sealed class CursorClient(HttpClient? http = null)
{
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        var credential = Environment.GetEnvironmentVariable("CURSOR_COOKIE");
        if (string.IsNullOrWhiteSpace(credential)) credential = await ReadAccessTokenAsync(cancellation);
        if (string.IsNullOrWhiteSpace(credential)) throw new UsageConnectionException("Cursor login required · sign in to Cursor or set CURSOR_COOKIE");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://cursor.com/api/usage-summary");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Add("Cookie", credential.Contains('=') ? credential : "WorkosCursorSessionToken=" + credential);
        var result = await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "Cursor", cancellation);
        return Parse(result, DateTimeOffset.Now);
    }
    static async Task<string?> ReadAccessTokenAsync(CancellationToken token)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor", "User", "globalStorage", "state.vscdb");
        if (!File.Exists(path)) return null;
        try {
            await Task.Yield();
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = 'cursorAuth/accessToken' LIMIT 1";
            var value = await command.ExecuteScalarAsync(token);
            return value switch { byte[] bytes => Encoding.UTF8.GetString(bytes).Trim('\0'), _ => value?.ToString() };
        } catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException) { return null; }
    }
    public static UsageEntry Parse(JsonElement result, DateTimeOffset now)
    {
        var plan = Get(Get(result, "individualUsage"), "plan");
        var percent = Get(plan, "totalPercentUsed");
        if (!percent.TryNumber(out var used)) {
            var usedValue = Get(plan, "used"); var limit = Get(plan, "limit");
            used = usedValue.TryNumber(out var u) && limit.TryNumber(out var l) && l > 0 ? u / l * 100 : double.NaN;
        }
        var reset = UsageHttp.Date(result, "billingCycleEnd");
        return new("cursor", String(result, "membershipType") ?? "Cursor", double.IsFinite(used) ? used : null, reset, Status: double.IsFinite(used) ? null : "No Cursor quota data", UpdatedAt: now);
    }
}

public sealed class OpenCodeClient(HttpClient? http = null)
{
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        var key = Environment.GetEnvironmentVariable("OPENCODE_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new UsageConnectionException("OpenCode login required · set OPENCODE_API_KEY");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://opencode.ai/zen/go/v1/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.ParseAdd("application/json");
        var result = await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "OpenCode", cancellation);
        return Parse(result, DateTimeOffset.Now);
    }
    public static UsageEntry Parse(JsonElement result, DateTimeOffset now)
    {
        var usage = Get(result, "usage");
        var rolling = Get(usage, "rolling");
        if (rolling.ValueKind != JsonValueKind.Object) rolling = Get(usage, "rollingUsage");
        var weekly = Get(usage, "weekly");
        if (weekly.ValueKind != JsonValueKind.Object) weekly = Get(usage, "weeklyUsage");
        var session = Number(rolling, "usagePercent");
        var weeklyPercent = Number(weekly, "usagePercent");
        var sessionReset = Reset(rolling, now); var weeklyReset = Reset(weekly, now);
        if (session is null && weeklyPercent is null) throw new UsageConnectionException("Check the OpenCode usage response format");
        return new("opencode", "Go", session, sessionReset, weeklyPercent, weeklyReset, Status: null, UpdatedAt: now);
    }
    static double? Number(JsonElement value, string property) => Get(value, property).TryNumber(out var number) && number is >= 0 and <= 100 ? number : null;
    static DateTimeOffset? Reset(JsonElement value, DateTimeOffset now) => Get(value, "resetInSec").TryNumber(out var seconds) && seconds >= 0 ? now.AddSeconds(seconds) : UsageHttp.Date(value, "resetAt");
}

public sealed class CommandCodeClient(HttpClient? http = null)
{
    const string BaseUrl = "https://api.commandcode.ai";
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        var cookie = Environment.GetEnvironmentVariable("COMMANDCODE_COOKIE");
        if (string.IsNullOrWhiteSpace(cookie)) throw new UsageConnectionException("Command Code login required · sign in at commandcode.ai or set COMMANDCODE_COOKIE");
        var client = http ?? UsageHttp.Client;
        var credits = await GetAsync(client, "/internal/billing/credits", cookie, cancellation);
        JsonElement subscription;
        try { subscription = await GetAsync(client, "/internal/billing/subscriptions", cookie, cancellation); }
        catch (UsageConnectionException) { subscription = default; }
        return Parse(credits, subscription, DateTimeOffset.Now);
    }
    static async Task<JsonElement> GetAsync(HttpClient client, string path, string cookie, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Referrer = new Uri("https://commandcode.ai/");
        request.Headers.Add("Origin", "https://commandcode.ai");
        return await UsageHttp.SendAsync(client, request, "Command Code", token);
    }
    public static UsageEntry Parse(JsonElement credits, JsonElement subscription, DateTimeOffset now)
    {
        var root = Get(credits, "credits");
        var monthly = Number(root, "monthlyCredits") ?? Number(root, "remainingCredits");
        var plan = String(Get(subscription, "data"), "planId") ?? "Command Code";
        var windows = new List<UsageWindow>();
        CollectWindows(credits, windows, now);
        CollectWindows(subscription, windows, now);
        if (monthly is not null) windows.Add(new("Monthly credits", null, DateTimeOffset.TryParse(String(Get(subscription, "data"), "currentPeriodEnd"), out var end) ? end : null));
        return new("commandcode", plan, Status: windows.Count == 0 ? "No Command Code quota data" : null, UpdatedAt: now, Windows: windows.Take(4).ToArray());
    }
    static void CollectWindows(JsonElement value, List<UsageWindow> windows, DateTimeOffset now)
    {
        if (value.ValueKind == JsonValueKind.Object) {
            var used = Number(value, "usedPercent") ?? Number(value, "usagePercent");
            if (used is null && Number(value, "used") is double consumed && Number(value, "limit") is double limit && limit > 0) used = consumed / limit * 100;
            if (used is not null) {
                var label = value.TryGetProperty("window", out var window) && window.ValueKind == JsonValueKind.String ? window.GetString()! : "Usage";
                var reset = Reset(value, now); if (!windows.Any(x => x.Label == label)) windows.Add(new(label, used, reset));
            }
            foreach (var property in value.EnumerateObject()) CollectWindows(property.Value, windows, now);
        } else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CollectWindows(item, windows, now);
    }
    static double? Number(JsonElement value, string property) => Get(value, property).TryNumber(out var number) && double.IsFinite(number) ? number : null;
    static DateTimeOffset? Reset(JsonElement value, DateTimeOffset now) => Get(value, "resetInSec").TryNumber(out var seconds) && seconds >= 0 ? now.AddSeconds(seconds) : UsageHttp.Date(value, "resetAt");
}

