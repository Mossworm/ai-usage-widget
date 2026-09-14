using System.Diagnostics;

namespace AiUsage;

public static class SubscriptionLogin
{
    public static string Name(string id) => Catalog.Services.FirstOrDefault(s => s.Id == id && !s.IsApi)?.Name
        ?? throw new ArgumentException("Unknown subscription");
    public static string? FromArgument(string argument) => argument.ToLowerInvariant() switch {
        "--login" or "aiusage:login" => "chatgpt",
        "--login-claude" or "aiusage:login-claude" => "claude",
        "aiusage:login-opencode" => "opencode",
        "aiusage:login-commandcode" => "commandcode", _ => null
    };
    // Claude signs in through the browser and finishes with a code the user pastes back,
    // so it cannot complete in one call the way the other subscriptions do.
    public static bool RequiresCode(string id) => id == "claude";
    public static async Task ConnectAsync(string id, CancellationToken cancellation = default)
    {
        if (id == "chatgpt") { await CodexClient.LoginAsync(cancellation); return; }
        // Claude cannot finish here: the browser hands the code back to the user, not to the app.
        if (RequiresCode(id)) throw new UsageConnectionException($"Start {Name(id)} login with BeginClaude and finish it with the pasted code");
        var url = id switch {
            "opencode" => "https://opencode.ai/auth",
            "commandcode" => "https://commandcode.ai/",
            _ => throw new ArgumentException("Unknown subscription")
        };
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    public static ClaudeAuthorization BeginClaude()
    {
        var authorization = ClaudeOAuth.Start();
        ClaudeOAuth.Open(authorization);
        return authorization;
    }
    public static async Task CompleteClaudeAsync(ClaudeAuthorization authorization, string pasted, CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var tokens = await ClaudeOAuth.CompleteAsync(authorization, pasted, null, timeout.Token);
        try { new ClaudeTokenStore().Save(tokens); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException) {
            throw new UsageConnectionException("Could not save the Claude login · check disk access and try again");
        }
    }
    public static void DisconnectClaude() => new ClaudeTokenStore().Clear();
}
