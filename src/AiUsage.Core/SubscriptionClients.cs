using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class GeminiClient(HttpClient? http = null, string? credentialsPath = null)
{
    // Public installed-app OAuth client metadata published by google-gemini/gemini-cli.
    // These identify the CLI; they are not a user's secret or an API key.
    const string ClientId = "681255809395-oo8ft2oprdrnp9e3aqf6av3hmdib135j.apps.googleusercontent.com";
    const string ClientSecret = "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";
    const string Endpoint = "https://cloudcode-pa.googleapis.com/v1internal:";
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        var folder = Path.Combine(Environment.GetEnvironmentVariable("GEMINI_CLI_HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini");
        var credentials = await UsageHttp.CredentialsAsync(credentialsPath ?? Path.Combine(folder, "oauth_creds.json"), "Gemini", token);
        var access = String(credentials, "access_token");
        if (string.IsNullOrWhiteSpace(access) || !Get(credentials, "expiry_date").TryNumber(out var expiry) || expiry < DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds())
            access = await RefreshAccessAsync(credentials, token);
        var project = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT_ID");
        var loaded = await PostAsync("loadCodeAssist", access, new { cloudaicompanionProject = project, metadata = new { ideType = "IDE_UNSPECIFIED", platform = "PLATFORM_UNSPECIFIED", pluginType = "GEMINI" } }, token);
        var projectValue = Get(loaded, "cloudaicompanionProject");
        project = projectValue.ValueKind == JsonValueKind.String ? projectValue.GetString() : String(projectValue, "id") ?? project;
        if (string.IsNullOrWhiteSpace(project)) throw new UsageConnectionException("Gemini CLI account setup required · sign in and try again");
        var tier = Get(loaded, "paidTier");
        if (tier.ValueKind != JsonValueKind.Object) tier = Get(loaded, "currentTier");
        var quota = await PostAsync("retrieveUserQuota", access, new { project }, token);
        return Parse(quota, String(tier, "name") ?? String(tier, "id"), DateTimeOffset.Now);
    }
    async Task<string> RefreshAccessAsync(JsonElement credentials, CancellationToken token)
    {
        var refresh = String(credentials, "refresh_token");
        if (string.IsNullOrWhiteSpace(refresh)) throw new UsageConnectionException("Gemini login expired · reconnect in Settings");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token") {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = ClientId, ["client_secret"] = ClientSecret, ["refresh_token"] = refresh, ["grant_type"] = "refresh_token" })
        };
        var result = await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "Gemini authentication", token);
        return String(result, "access_token") ?? throw new UsageConnectionException("Could not refresh Gemini login · reconnect in Settings");
        // Never overwrite Gemini CLI credentials. Refreshed access tokens live only in memory.
    }
    async Task<JsonElement> PostAsync(string method, string access, object payload, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint + method) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.UserAgent.ParseAdd("AiUsageWidget/1.2.0");
        return await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "Gemini", token);
    }
    public static UsageEntry Parse(JsonElement result, string? plan, DateTimeOffset now)
    {
        var buckets = Get(result, "buckets");
        if (buckets.ValueKind != JsonValueKind.Array) throw new UsageConnectionException("Check the Gemini model quota response format");
        var all = new List<UsageWindow>();
        foreach (var bucket in buckets.EnumerateArray()) {
            var model = String(bucket, "modelId");
            if (string.IsNullOrWhiteSpace(model) || !Get(bucket, "remainingFraction").TryNumber(out var fraction) || fraction < 0 || fraction > 1) continue;
            all.Add(new(model, (1 - fraction) * 100, UsageHttp.Date(bucket, "resetTime")));
        }
        // Fit the two-line widget: show the most depleted Pro and Flash model, then fill
        // missing families with remaining models. Never add independent quota percentages.
        var sorted = all.OrderByDescending(x => x.Percent).ThenBy(x => x.Label, StringComparer.Ordinal).ToArray();
        var selected = new List<UsageWindow>();
        foreach (var family in new[] { "pro", "flash" }) {
            var match = sorted.FirstOrDefault(x => x.Label.Contains(family, StringComparison.OrdinalIgnoreCase));
            if (match is not null && !selected.Any(x => x.Label == match.Label)) selected.Add(match);
        }
        foreach (var value in sorted) if (selected.Count < 2 && !selected.Any(x => x.Label == value.Label)) selected.Add(value);
        return new("gemini", plan, Status: selected.Count == 0 ? "No Gemini model quota data" : null, UpdatedAt: now, Windows: selected.ToArray());
    }
}
