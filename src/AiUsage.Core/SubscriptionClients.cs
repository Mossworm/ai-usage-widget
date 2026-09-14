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
            throw new UsageConnectionException($"{service} rejected this sign-in · sign in again in the CLI");
        if (!response.IsSuccessStatusCode) throw new UsageConnectionException($"{service} request failed (HTTP {(int)response.StatusCode})");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (json.RootElement.ValueKind != JsonValueKind.Object) throw new UsageConnectionException($"Check the {service} response format");
        return json.RootElement.Clone();
    }
    public static async Task<JsonElement> CredentialsAsync(string path, string service, string missing, CancellationToken token)
    {
        if (!File.Exists(path)) throw new UsageConnectionException(missing);
        if (new FileInfo(path).Length > 262144) throw new UsageConnectionException($"Check the {service} credential file format");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, token));
        return json.RootElement.Clone();
    }
    public static DateTimeOffset? Date(JsonElement value, string property) => DateTimeOffset.TryParse(String(value, property), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    public static double? Percentage(JsonElement value, string property) => Get(value, property).TryNumber(out var n) && n >= 0 && n <= 100 ? n : null;
}

public sealed class ClaudeClient(HttpClient? http = null, string? credentialsPath = null)
{
    public static string DefaultCredentialsPath()
    {
        var folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(folder, ".credentials.json");
    }
    // The Claude Code CLI owns the browser sign-in, the credential file and the token refresh.
    // This widget only ever reads that file; it never writes it and stores no token of its own.
    const string SignInRequired = "Claude Code sign-in required · sign in with Claude Code";
    const string SignInExpired = "Claude Code sign-in expired · open Claude Code to refresh it";
    // The usage endpoint is the CLI's own, so it is addressed with the CLI's user agent.
    static string Agent => Environment.GetEnvironmentVariable("AIUSAGE_CLAUDE_USER_AGENT") is { Length: > 0 } custom ? custom.Trim() : "claude-code/0.2.29";
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        var (result, plan) = await FetchResponseAsync(cancellation);
        return Parse(result, plan, DateTimeOffset.Now);
    }
    // Exposed so a live check can report which quota windows the account actually returns.
    public async Task<(JsonElement Result, string? Plan)> FetchResponseAsync(CancellationToken cancellation = default)
    {
        var (access, plan) = await ResolveAsync(cancellation);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.UserAgent.ParseAdd(Agent);
        return (await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "Claude", cancellation), plan);
    }
    async Task<(string Access, string? Plan)> ResolveAsync(CancellationToken cancellation)
    {
        var root = await UsageHttp.CredentialsAsync(credentialsPath ?? DefaultCredentialsPath(), "Claude", SignInRequired, cancellation);
        var oauth = Get(root, "claudeAiOauth");
        var access = String(oauth, "accessToken");
        if (string.IsNullOrWhiteSpace(access)) throw new UsageConnectionException(SignInRequired);
        // The refresh token beside it is deliberately left alone: refreshing would rotate the value
        // the running CLI still holds and sign the user out of Claude Code.
        if (Get(oauth, "expiresAt").TryNumber(out var expires) && expires <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new UsageConnectionException(SignInExpired);
        return (access, String(oauth, "subscriptionType"));
    }
    public static UsageEntry Parse(JsonElement result, string? plan, DateTimeOffset now)
    {
        if (!result.TryGetProperty("five_hour", out _) && !result.TryGetProperty("seven_day", out _))
            throw new UsageConnectionException("Check the Claude quota response format");
        var session = Get(result, "five_hour"); var weekly = Get(result, "seven_day");
        var first = UsageHttp.Percentage(session, "utilization"); var second = UsageHttp.Percentage(weekly, "utilization");
        var firstReset = UsageHttp.Date(session, "resets_at"); var secondReset = UsageHttp.Date(weekly, "resets_at");
        var models = ModelWindows(result);
        return new("claude", plan, first, firstReset, second, secondReset,
            Status: first is null && second is null ? "No Claude quota data" : null, UpdatedAt: now,
            Windows: models.Length == 0 ? null : [new("5-hour", first, firstReset), new("Weekly", second, secondReset), .. models]);
    }
    // Anthropic reports a per-model weekly quota beside the global ones as seven_day_<model>.
    // Which ones to surface is configurable because the set of models changes over time.
    public static string[] TrackedModels => (Environment.GetEnvironmentVariable("AIUSAGE_CLAUDE_MODEL_WINDOWS") ?? "fable")
        .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    static UsageWindow[] ModelWindows(JsonElement result)
    {
        var windows = new List<UsageWindow>();
        foreach (var model in TrackedModels) {
            if (ScopedWeekly(result, model) is { } scoped) { windows.Add(scoped); continue; }
            var window = FindModelWindow(result, model);
            // A model with no quota reported on this plan gets no row rather than an empty one.
            if (UsageHttp.Percentage(window, "utilization") is not { } percent) continue;
            windows.Add(new($"Weekly ({Title(model)})", percent, UsageHttp.Date(window, "resets_at")));
        }
        return [.. windows];
    }
    // Which models this account actually reports a weekly quota for, for configuring the rows.
    public static string[] ScopedModelNames(JsonElement result)
    {
        var limits = Get(result, "limits");
        if (limits.ValueKind != JsonValueKind.Array) return [];
        return [.. limits.EnumerateArray()
            .Where(x => String(x, "kind") == "weekly_scoped")
            .Select(x => String(Get(Get(x, "scope"), "model"), "display_name"))
            .OfType<string>().Distinct()];
    }
    // Current responses carry the per-model weekly quota as a weekly_scoped row inside `limits`,
    // named by the model's display name. The seven_day_<model> keys are left null there.
    static UsageWindow? ScopedWeekly(JsonElement result, string model)
    {
        var limits = Get(result, "limits");
        var wanted = Simplify(model);
        if (limits.ValueKind != JsonValueKind.Array || wanted.Length == 0) return null;
        foreach (var limit in limits.EnumerateArray()) {
            if (String(limit, "kind") != "weekly_scoped") continue;
            var name = String(Get(Get(limit, "scope"), "model"), "display_name");
            if (name is null || !Simplify(name).StartsWith(wanted, StringComparison.Ordinal)) continue;
            if (UsageHttp.Percentage(limit, "percent") is not { } percent) continue;
            // Label with the provider's own display name rather than the configured spelling.
            return new($"Weekly ({name})", percent, UsageHttp.Date(limit, "resets_at"));
        }
        return null;
    }
    // Matched on the suffix alone so a seven_day_fable_5 or seven-day-fable spelling still lands.
    static JsonElement FindModelWindow(JsonElement result, string model)
    {
        if (result.ValueKind != JsonValueKind.Object) return default;
        var wanted = Simplify(model);
        if (wanted.Length == 0) return default;
        foreach (var property in result.EnumerateObject()) {
            var name = Simplify(property.Name);
            if (name.StartsWith("sevenday", StringComparison.Ordinal) && name["sevenday".Length..].StartsWith(wanted, StringComparison.Ordinal))
                return property.Value;
        }
        return default;
    }
    static string Simplify(string value) => new([.. value.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
    static string Title(string model) => model.Length == 0 ? model : char.ToUpperInvariant(model[0]) + model[1..];
}
