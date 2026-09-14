using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

public sealed class ClaudeClient(HttpClient? http = null, string? credentialsPath = null, ClaudeTokenStore? store = null)
{
    public static string DefaultCredentialsPath()
    {
        var folder = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return Path.Combine(folder, ".credentials.json");
    }
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
        request.Headers.UserAgent.ParseAdd("AiUsageWidget/1.2.0");
        return (await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "Claude", cancellation), plan);
    }
    // An explicit credential path pins the source. Otherwise the widget's own browser login wins,
    // and an existing Claude Code CLI credential file stays usable as the fallback.
    async Task<(string Access, string? Plan)> ResolveAsync(CancellationToken cancellation)
    {
        if (credentialsPath is null && (store ?? new ClaudeTokenStore()).Read() is { } saved)
            return (await FreshAsync(saved, cancellation), saved.Plan);
        var root = await UsageHttp.CredentialsAsync(credentialsPath ?? DefaultCredentialsPath(), "Claude", cancellation);
        var oauth = Get(root, "claudeAiOauth");
        var access = String(oauth, "accessToken");
        if (string.IsNullOrWhiteSpace(access)) throw new UsageConnectionException("Claude subscription login required · connect in Settings");
        if (Get(oauth, "expiresAt").TryNumber(out var expires) && expires <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new UsageConnectionException("Claude login expired · reconnect in Settings");
        return (access, String(oauth, "subscriptionType"));
    }
    async Task<string> FreshAsync(ClaudeTokens saved, CancellationToken cancellation)
    {
        if (!saved.IsExpired(DateTimeOffset.UtcNow)) return saved.AccessToken;
        if (string.IsNullOrWhiteSpace(saved.RefreshToken)) throw new UsageConnectionException("Claude login expired · reconnect in Settings");
        var refreshed = await ClaudeOAuth.RefreshAsync(saved.RefreshToken, http, cancellation);
        var value = refreshed with { RefreshToken = refreshed.RefreshToken ?? saved.RefreshToken, Plan = refreshed.Plan ?? saved.Plan };
        // A rotated token that cannot be stored still works for this fetch; the next one signs in again.
        try { (store ?? new ClaudeTokenStore()).Save(value); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException) { }
        return value.AccessToken;
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

public sealed class OpenCodeClient(HttpClient? http = null, string? credentialsPath = null)
{
    const string UsageUrl = "https://opencode.ai/zen/go/v1/usage";
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        var key = await ReadApiKeyAsync(credentialsPath, cancellation);
        if (string.IsNullOrWhiteSpace(key)) throw new UsageConnectionException("OpenCode login required · connect in Settings");
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.ParseAdd("application/json");
        JsonElement result;
        try {
            result = await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "OpenCode", cancellation);
        } catch (UsageConnectionException e) when (e.RetryAfter is null && IsAuthFailure(e)) {
            throw new UsageConnectionException("OpenCode login required · connect in Settings");
        }
        try {
            return Parse(result, DateTimeOffset.Now);
        } catch (UsageConnectionException e) when (e.Message.Contains("response format", StringComparison.OrdinalIgnoreCase)) {
            throw new UsageConnectionException("OpenCode login required · connect in Settings");
        }
    }
    static bool IsAuthFailure(UsageConnectionException e) =>
        e.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("account access", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("(HTTP 401)", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("(HTTP 402)", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("(HTTP 403)", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("(HTTP 404)", StringComparison.OrdinalIgnoreCase);
    internal static async Task<string?> ReadApiKeyAsync(string? overridePath, CancellationToken cancellation)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome)) dataHome = Path.Combine(home, ".local", "share");
        var path = overridePath ?? Path.Combine(dataHome, "opencode", "auth.json");
        if (File.Exists(path)) {
            JsonElement root;
            try {
                if (new FileInfo(path).Length > 262144) return null;
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellation));
                root = json.RootElement.Clone();
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { root = default; }
            // OpenCode stores credentials as { "<provider>": { "type": "api", "key": "..." } }.
            // The Go subscription shares the OPENCODE_API_KEY credential with provider id "opencode-go".
            foreach (var id in new[] { "opencode-go", "opencode", "opencode-zen", "zen" }) {
                var entry = Get(root, id);
                var key = ExtractKey(entry);
                if (!string.IsNullOrWhiteSpace(key)) return key;
            }
            // pi-quota-monitoring fallbacks: flat string entry, flat apiKey field, nested access/key objects.
            foreach (var id in new[] { "opencode-go", "opencode" }) {
                var entry = Get(root, id);
                if (entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString())) return entry.GetString();
            }
            foreach (var name in new[] { "apiKey", "api_key" }) {
                var value = Get(root, name);
                if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString();
            }
        }
        var env = Environment.GetEnvironmentVariable("OPENCODE_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }
    internal static string? ExtractKey(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in new[] { "key", "access", "token", "apiKey", "api_key" }) {
            var value = Get(entry, name);
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString();
        }
        return null;
    }
    public static UsageEntry Parse(JsonElement result, DateTimeOffset now)
    {
        var usage = Get(result, "usage");
        if (usage.ValueKind != JsonValueKind.Object) usage = result;
        var rolling = FirstObject(usage, "rolling", "rollingUsage");
        // Weekly is the second window shown next to the rolling 5-hour window.
        // Fall back to monthly when the API only reports rolling + monthly.
        var weekly = FirstObject(usage, "weekly", "weeklyUsage");
        var monthly = FirstObject(usage, "monthly", "monthlyUsage");
        var session = Percent(rolling);
        var sessionReset = Reset(rolling, now);
        var weeklyPercent = Percent(weekly) ?? Percent(monthly);
        var weeklyReset = Reset(weekly, now) ?? Reset(monthly, now);
        if (session is null && weeklyPercent is null) throw new UsageConnectionException("Check the OpenCode usage response format");
        return new("opencode", "Go", session, sessionReset, weeklyPercent, weeklyReset, Status: null, UpdatedAt: now);
    }
    static JsonElement FirstObject(JsonElement parent, string first, string second)
    {
        var value = Get(parent, first);
        if (value.ValueKind == JsonValueKind.Object) return value;
        value = Get(parent, second);
        return value.ValueKind == JsonValueKind.Object ? value : default;
    }
    static double? Percent(JsonElement window) => Number(window, "percent") ?? Number(window, "usagePercent");
    static double? Number(JsonElement value, string property) => Get(value, property).TryNumber(out var number) && number is >= 0 and <= 100 ? number : null;
    static DateTimeOffset? Reset(JsonElement window, DateTimeOffset now)
    {
        if (TryDate(Get(window, "resetsAt"), out var first)) return first;
        if (TryDate(Get(window, "resetAt"), out var second)) return second;
        if (Get(window, "resetInSec").TryNumber(out var seconds) && seconds >= 0) return now.AddSeconds(seconds);
        if (Get(window, "resetInMs").TryNumber(out var ms) && ms >= 0) return now.AddMilliseconds(ms);
        return null;
    }
    internal static bool TryDate(JsonElement value, out DateTimeOffset time)
    {
        time = default;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var epoch)) {
            try { time = epoch >= 1_000_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(epoch) : DateTimeOffset.FromUnixTimeSeconds(epoch); return true; }
            catch (ArgumentOutOfRangeException) { return false; }
        }
        if (value.ValueKind == JsonValueKind.String) {
            var text = value.GetString();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) return true;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
                try { time = parsed >= 1_000_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(parsed) : DateTimeOffset.FromUnixTimeSeconds(parsed); return true; }
                catch (ArgumentOutOfRangeException) { return false; }
            }
        }
        return false;
    }
}

public sealed class CommandCodeClient(HttpClient? http = null, string? credentialsPath = null)
{
    const string BaseUrl = "https://api.commandcode.ai";
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        var key = await ReadApiKeyAsync(credentialsPath, cancellation);
        if (string.IsNullOrWhiteSpace(key)) throw new UsageConnectionException("Command Code login required · connect in Settings");
        var client = http ?? UsageHttp.Client;
        var whoami = await GetAsync(client, "/alpha/whoami", key, cancellation);
        var orgId = OrgId(whoami);
        var query = orgId is null ? "" : "?orgId=" + Uri.EscapeDataString(orgId);
        var credits = await GetAsync(client, "/alpha/billing/credits" + query, key, cancellation);
        JsonElement summary = default;
        try { summary = await GetAsync(client, "/alpha/usage/summary" + query, key, cancellation); }
        catch (UsageConnectionException e) when (e.RetryAfter is null) { summary = default; }
        JsonElement subscription = default;
        foreach (var path in new[] { "/alpha/billing/subscriptions", "/alpha/billing/subscription" }) {
            try { subscription = await GetAsync(client, path + query, key, cancellation); break; }
            catch (UsageConnectionException e) when (e.RetryAfter is null) { }
        }
        try {
            return Parse(whoami, credits, summary, subscription, DateTimeOffset.Now);
        } catch (UsageConnectionException e) when (e.Message.Contains("response format", StringComparison.OrdinalIgnoreCase)) {
            throw new UsageConnectionException("Command Code login required · connect in Settings");
        }
    }
    internal static async Task<string?> ReadApiKeyAsync(string? overridePath, CancellationToken cancellation)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = overridePath ?? Path.Combine(home, ".commandcode", "auth.json");
        if (File.Exists(path)) {
            try {
                if (new FileInfo(path).Length > 262144) return null;
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellation));
                var root = json.RootElement.Clone();
                // The Command Code CLI stores { "apiKey": "..." } in ~/.commandcode/auth.json.
                foreach (var name in new[] { "apiKey", "api_key", "key", "token", "accessToken", "access" }) {
                    var value = Get(root, name);
                    if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString();
                }
                var nested = Get(root, "commandcode");
                if (nested.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(nested.GetString())) return nested.GetString();
                if (nested.ValueKind == JsonValueKind.Object) {
                    var key = OpenCodeClient.ExtractKey(nested);
                    if (!string.IsNullOrWhiteSpace(key)) return key;
                }
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        }
        foreach (var name in new[] { "COMMAND_CODE_API_KEY", "COMMANDCODE_API_KEY", "CMD_API_KEY" }) {
            var env = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(env)) return env;
        }
        return null;
    }
    static string? OrgId(JsonElement whoami) =>
        String(Get(whoami, "org"), "id")
        ?? String(Get(whoami, "organization"), "id")
        ?? String(whoami, "orgId")
        ?? String(whoami, "org_id");
    static async Task<JsonElement> GetAsync(HttpClient client, string path, string key, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("AiUsageWidget/1.2.0");
        try {
            return await UsageHttp.SendAsync(client, request, "Command Code", token);
        } catch (UsageConnectionException e) when (e.RetryAfter is null && IsAuthFailure(e)) {
            throw new UsageConnectionException("Command Code login required · connect in Settings");
        }
    }
    static bool IsAuthFailure(UsageConnectionException e) =>
        e.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("account access", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("(HTTP 401)", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("(HTTP 402)", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("(HTTP 403)", StringComparison.OrdinalIgnoreCase)
        || e.Message.Contains("(HTTP 404)", StringComparison.OrdinalIgnoreCase);
    public static UsageEntry Parse(JsonElement whoami, JsonElement credits, JsonElement summary, JsonElement subscription, DateTimeOffset now)
    {
        // pi-quota-monitoring shape: credits.windowLimits.fiveHour/weekly { used, cap, resetAt },
        // credits.credits { monthlyCredits, purchasedCredits, freeCredits, monthlyResetAt },
        // usage summary { totalCost } for the monthly billing period.
        var limits = Get(credits, "windowLimits");
        if (limits.ValueKind != JsonValueKind.Object) limits = Get(credits, "window_limits");
        var fiveHour = FirstObject(limits, "fiveHour", "five_hour");
        var weeklyWindow = FirstObject(limits, "weekly", "seven_day");
        var session = WindowPercent(fiveHour);
        var sessionReset = WindowReset(fiveHour, now);
        var weeklyPercent = WindowPercent(weeklyWindow);
        var weeklyReset = WindowReset(weeklyWindow, now);
        if (weeklyPercent is null) {
            var monthly = MonthlyPercent(credits, summary);
            if (monthly is not null) {
                weeklyPercent = monthly.Value.Percent;
                weeklyReset ??= monthly.Value.Reset;
            }
        }
        if (session is null && weeklyPercent is null) throw new UsageConnectionException("Check the Command Code usage response format");
        var plan = NormalizePlan(PlanCandidate(whoami, credits, subscription)) ?? "Command Code";
        return new("commandcode", plan, session, sessionReset, weeklyPercent, weeklyReset, Status: null, UpdatedAt: now);
    }
    static JsonElement FirstObject(JsonElement parent, string first, string second)
    {
        var value = Get(parent, first);
        if (value.ValueKind == JsonValueKind.Object) return value;
        value = Get(parent, second);
        return value.ValueKind == JsonValueKind.Object ? value : default;
    }
    static double? WindowPercent(JsonElement window)
    {
        if (window.ValueKind != JsonValueKind.Object) return null;
        if (Get(window, "used").TryNumber(out var used) && used >= 0) {
            foreach (var capName in new[] { "cap", "limit", "capAmount", "total" }) {
                if (Get(window, capName).TryNumber(out var cap) && cap > 0)
                    return Math.Clamp(used / cap * 100, 0, 100);
            }
        }
        foreach (var name in new[] { "percent", "usagePercent", "usedPercent", "utilization" }) {
            if (Get(window, name).TryNumber(out var direct) && direct is >= 0 and <= 100) return direct;
        }
        return null;
    }
    static DateTimeOffset? WindowReset(JsonElement window, DateTimeOffset now)
    {
        if (window.ValueKind != JsonValueKind.Object) return null;
        if (OpenCodeClient.TryDate(Get(window, "resetAt"), out var first)) return first;
        if (OpenCodeClient.TryDate(Get(window, "resetsAt"), out var second)) return second;
        if (OpenCodeClient.TryDate(Get(window, "reset"), out var third)) return third;
        if (Get(window, "resetInSec").TryNumber(out var seconds) && seconds >= 0) return now.AddSeconds(seconds);
        if (Get(window, "resetInMs").TryNumber(out var ms) && ms >= 0) return now.AddMilliseconds(ms);
        return UsageHttp.Date(window, "resetAt");
    }
    static (double Percent, DateTimeOffset? Reset)? MonthlyPercent(JsonElement credits, JsonElement summary)
    {
        var wallet = Get(credits, "credits");
        if (wallet.ValueKind != JsonValueKind.Object) wallet = credits;
        var monthly = Amount(wallet, "monthlyCredits") ?? Amount(wallet, "monthly");
        var purchased = Amount(wallet, "purchasedCredits") ?? 0;
        var free = Amount(wallet, "freeCredits") ?? Amount(wallet, "free") ?? 0;
        var spent = Amount(summary, "totalCost") ?? Amount(summary, "total") ?? Amount(Get(summary, "usage"), "totalCost");
        if (monthly is null && purchased == 0 && free == 0) return null;
        var remaining = (monthly ?? 0) + purchased + free;
        var total = remaining + (spent ?? 0);
        if (total <= 0 || spent is null || spent < 0) return null;
        DateTimeOffset? reset = null;
        if (!OpenCodeClient.TryDate(Get(wallet, "monthlyResetAt"), out var monthlyReset)) monthlyReset = default;
        else reset = monthlyReset;
        reset ??= NextMonthStart(DateTimeOffset.Now);
        return (Math.Clamp(spent.Value / total * 100, 0, 100), reset);
    }
    static double? Amount(JsonElement value, string property) => Get(value, property).TryNumber(out var number) && double.IsFinite(number) ? number : null;
    static DateTimeOffset NextMonthStart(DateTimeOffset now) => new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(1);
    static string? PlanCandidate(JsonElement whoami, JsonElement credits, JsonElement subscription)
    {
        var data = Get(subscription, "data");
        if (data.ValueKind != JsonValueKind.Object) data = subscription;
        foreach (var node in new[] { data, Get(data, "subscription"), Get(data, "plan"), whoami, Get(whoami, "org"), Get(whoami, "organization"), Get(whoami, "user"), Get(whoami, "subscription"), credits, Get(credits, "subscription") }) {
            foreach (var name in new[] { "planId", "plan_id", "plan", "planName", "plan_name", "tier", "name", "slug" }) {
                var value = String(node, name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return null;
    }
    public static string? NormalizePlan(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var lower = raw.ToLowerInvariant();
        if (lower.Contains("goat")) return "Goat";
        if (lower.Contains("max")) return "Max";
        if (lower.Contains("team")) return "Team";
        if (lower.Contains("provider")) return "Provider";
        if (lower.Contains("ultra")) return "Ultra";
        if (lower.Contains("pro")) return "Pro";
        if (lower.Contains("go")) return "Go";
        return raw.Trim().Length is > 0 and <= 32 ? raw.Trim() : null;
    }
}

