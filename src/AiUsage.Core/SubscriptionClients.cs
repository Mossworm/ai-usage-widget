using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
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

