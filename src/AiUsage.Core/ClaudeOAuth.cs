using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static AiUsage.CodexClient;

namespace AiUsage;

public sealed record ClaudeTokens(string AccessToken, string? RefreshToken = null, DateTimeOffset? ExpiresAt = null, string? Plan = null)
{
    // Refresh a minute early so a long request never starts on a token that expires mid-flight.
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } time && time <= now.AddMinutes(1);
}

public sealed class ClaudeAuthorization(string url, string verifier, string state)
{
    public string Url { get; } = url;
    internal string Verifier { get; } = verifier;
    internal string State { get; } = state;
}

// Browser sign-in for the Claude subscription so usage works without the Claude Code CLI installed.
// These are the CLI's own public client and service paths, not a published API, so every endpoint is
// overridable by environment variable and a provider change can be corrected without a rebuild.
public static class ClaudeOAuth
{
    const string Agent = "AiUsageWidget/1.2.0";
    static string Setting(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
    public static string ClientId => Setting("AIUSAGE_CLAUDE_CLIENT_ID", "9d1c250a-e61b-44d9-88ed-5944d1962f5e");
    public static string RedirectUri => Setting("AIUSAGE_CLAUDE_REDIRECT_URI", "https://platform.claude.com/oauth/code/callback");
    static string AuthorizeUrl => Setting("AIUSAGE_CLAUDE_AUTHORIZE_URL", "https://claude.ai/oauth/authorize");
    static string Scope => Setting("AIUSAGE_CLAUDE_SCOPE", "user:inference user:profile");
    static string ProfileUrl => Setting("AIUSAGE_CLAUDE_PROFILE_URL", "https://api.anthropic.com/api/oauth/profile");
    // The token host moved from console.anthropic.com to platform.claude.com; try the current one first.
    static string[] TokenUrls => Setting("AIUSAGE_CLAUDE_TOKEN_URL", "") is { Length: > 0 } custom
        ? [custom]
        : ["https://platform.claude.com/v1/oauth/token", "https://console.anthropic.com/v1/oauth/token"];

    public static ClaudeAuthorization Start()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var url = $"{AuthorizeUrl}?code=true&response_type=code&client_id={Uri.EscapeDataString(ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&scope={Uri.EscapeDataString(Scope)}"
            + $"&code_challenge={challenge}&code_challenge_method=S256&state={state}";
        return new(url, verifier, state);
    }
    public static void Open(ClaudeAuthorization authorization)
    {
        try { Process.Start(new ProcessStartInfo(authorization.Url) { UseShellExecute = true }); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ObjectDisposedException) {
            throw new UsageConnectionException("Could not open the browser · open the Claude login link manually");
        }
    }
    public static async Task<ClaudeTokens> CompleteAsync(ClaudeAuthorization authorization, string pasted, HttpClient? http = null, CancellationToken cancellation = default)
    {
        var (code, state) = ParseResponse(pasted);
        // The authorize page returns the state it was given; a mismatch means this is not our request.
        if (state is not null && !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(authorization.State)))
            throw new UsageConnectionException("The Claude login response does not match this request · connect again");
        var tokens = await ExchangeAsync(new() {
            ["grant_type"] = "authorization_code", ["code"] = code, ["code_verifier"] = authorization.Verifier,
            ["client_id"] = ClientId, ["redirect_uri"] = RedirectUri, ["state"] = authorization.State
        }, http, "Claude login did not complete · copy the code again and retry", cancellation);
        return tokens with { Plan = await PlanAsync(tokens.AccessToken, http, cancellation) };
    }
    public static Task<ClaudeTokens> RefreshAsync(string refreshToken, HttpClient? http = null, CancellationToken cancellation = default)
        => ExchangeAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken, ["client_id"] = ClientId },
            http, "Claude login expired · reconnect in Settings", cancellation);

    // Accepts what the authorize page shows (CODE#STATE), a bare code, or the whole callback link.
    public static (string Code, string? State) ParseResponse(string? pasted)
    {
        var text = (pasted ?? "").Trim();
        if (text.Length == 0) throw new UsageConnectionException("Paste the code shown after the Claude login");
        if (text.Length > 4096) throw new UsageConnectionException("Check the Claude authorization code");
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") {
            string? code = null, state = null;
            foreach (var pair in (uri.Query.TrimStart('?') + "&" + uri.Fragment.TrimStart('#')).Split('&', StringSplitOptions.RemoveEmptyEntries)) {
                var separator = pair.IndexOf('=');
                if (separator <= 0) continue;
                var value = Uri.UnescapeDataString(pair[(separator + 1)..]);
                if (pair[..separator] == "code") code = value; else if (pair[..separator] == "state") state = value;
            }
            if (string.IsNullOrWhiteSpace(code)) throw new UsageConnectionException("That link has no Claude authorization code");
            return (code.Trim(), string.IsNullOrWhiteSpace(state) ? null : state.Trim());
        }
        var parts = text.Split('#', 2);
        var bare = parts[0].Trim();
        if (bare.Length == 0) throw new UsageConnectionException("Paste the code shown after the Claude login");
        return (bare, parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : null);
    }
    static async Task<ClaudeTokens> ExchangeAsync(Dictionary<string, string> body, HttpClient? http, string failureMessage, CancellationToken cancellation)
    {
        var client = http ?? UsageHttp.Client;
        var payload = JsonSerializer.Serialize(body);
        UsageConnectionException? failure = null;
        foreach (var url in TokenUrls) {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
            request.Headers.UserAgent.ParseAdd(Agent);
            request.Headers.Accept.ParseAdd("application/json");
            try {
                var result = await UsageHttp.SendAsync(client, request, "Claude", cancellation);
                var access = String(result, "access_token");
                if (string.IsNullOrWhiteSpace(access)) throw new UsageConnectionException("Check the Claude login response format");
                DateTimeOffset? expires = Get(result, "expires_in").TryNumber(out var seconds) && seconds > 0
                    ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null;
                return new(access, String(result, "refresh_token"), expires);
            } catch (UsageConnectionException e) {
                if (e.RetryAfter is not null) throw;
                failure = e;
            }
        }
        // Server auth wording would be misleading here; report the step the user can act on instead.
        throw new UsageConnectionException(failure is { } last && last.Message.Contains("response format", StringComparison.OrdinalIgnoreCase)
            ? last.Message : failureMessage);
    }
    // Best effort: the plan badge is cosmetic, so a missing or changed profile path must not fail the login.
    static async Task<string?> PlanAsync(string access, HttpClient? http, CancellationToken cancellation)
    {
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get, ProfileUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            request.Headers.UserAgent.ParseAdd(Agent);
            var result = await UsageHttp.SendAsync(http ?? UsageHttp.Client, request, "Claude", cancellation);
            foreach (var parent in new[] { Get(result, "account"), Get(result, "organization"), result })
                foreach (var name in new[] { "subscription_type", "subscriptionType", "billing_type", "organization_type" })
                    if (String(parent, name) is { Length: > 0 } plan) return plan;
        } catch (Exception e) when (e is UsageConnectionException or HttpRequestException or JsonException or IOException or OperationCanceledException) { }
        return null;
    }
    static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

// Subscription tokens live in this app's own folder, encrypted for the current Windows user.
// The Claude Code CLI credential file is only ever read, never written.
public sealed class ClaudeTokenStore(string? path = null)
{
    static readonly JsonSerializerOptions Format = new() { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string Path => path ?? System.IO.Path.Combine(LocalStore.Folder, "claude-auth.dat");
    public ClaudeTokens? Read()
    {
        try {
            var file = new FileInfo(Path);
            if (!file.Exists || file.Length is 0 or > 262144) return null;
            var value = JsonSerializer.Deserialize<ClaudeTokens>(Encoding.UTF8.GetString(Dpapi.Unprotect(File.ReadAllBytes(Path))), Format);
            return string.IsNullOrWhiteSpace(value?.AccessToken) ? null : value;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or CryptographicException or UsageConnectionException) { return null; }
    }
    public void Save(ClaudeTokens tokens)
    {
        var bytes = Dpapi.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokens, Format)));
        var full = System.IO.Path.GetFullPath(Path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, full, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Clear()
    {
        try { if (File.Exists(Path)) File.Delete(Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

// DPAPI through crypt32 directly: the app is Windows-only and this keeps AiUsage.Core dependency-free.
static class Dpapi
{
    const int UiForbidden = 0x1;
    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr handle);
    public static byte[] Protect(byte[] value) => Run(value, true);
    public static byte[] Unprotect(byte[] value) => Run(value, false);
    static byte[] Run(byte[] value, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new UsageConnectionException("Claude login storage requires Windows");
        if (value.Length == 0) throw new CryptographicException("Empty DPAPI payload");
        var pin = GCHandle.Alloc(value, GCHandleType.Pinned);
        var output = default(Blob);
        try {
            var input = new Blob { Size = value.Length, Data = pin.AddrOfPinnedObject() };
            var ok = protect
                ? CryptProtectData(ref input, "AI Usage Widget · Claude", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!ok || output.Data == IntPtr.Zero) throw new CryptographicException(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        } finally {
            pin.Free();
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}
