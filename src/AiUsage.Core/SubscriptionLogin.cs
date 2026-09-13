using System.Diagnostics;

namespace AiUsage;

public static class SubscriptionLogin
{
    public static string Name(string id) => Catalog.Services.FirstOrDefault(s => s.Id == id && !s.IsApi)?.Name
        ?? throw new ArgumentException("Unknown subscription");
    public static string? FromArgument(string argument) => argument.ToLowerInvariant() switch {
        "--login" or "aiusage:login" => "chatgpt",
        "--login-claude" or "aiusage:login-claude" => "claude", _ => null
    };
    public static async Task ConnectAsync(string id, CancellationToken cancellation = default)
    {
        if (id == "chatgpt") { await CodexClient.LoginAsync(cancellation); return; }
        if (id != "claude") throw new ArgumentException("Unknown subscription");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var process = Process.Start(StartInfo(id)) ?? throw new UsageConnectionException($"Could not start {Name(id)} login");
        var errorDrain = DrainAsync(process.StandardError, timeout.Token);
        Task? outputDrain = null;
        try {
            if (id == "claude") {
                outputDrain = DrainAsync(process.StandardOutput, timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode != 0) throw new UsageConnectionException("Claude login did not complete · try connecting again");
            }
        } finally {
            timeout.Cancel(); CodexExecutable.Stop(process);
            await errorDrain; if (outputDrain is not null) await outputDrain;
        }
    }
    static IEnumerable<string> Folders() => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Concat([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin")])
        .Where(Path.IsPathFullyQualified).Distinct(StringComparer.OrdinalIgnoreCase);
    static string FindFile(IEnumerable<string> paths, string name) => paths.FirstOrDefault(File.Exists) ?? throw new UsageConnectionException($"{name} CLI must be installed · run the install command in README");
    static ProcessStartInfo StartInfo(string id)
    {
        var folders = Folders().ToArray();
        var info = new ProcessStartInfo {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (id == "claude") {
            info.FileName = FindFile(folders.SelectMany(f => new[] { Path.Combine(f, "claude.exe"), Path.Combine(f, "node_modules", "@anthropic-ai", "claude-code", "bin", "claude.exe") }), "Claude");
            info.ArgumentList.Add("auth"); info.ArgumentList.Add("login"); info.ArgumentList.Add("--claudeai");
        }
        return info;
    }
    static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[2048];
        try { while (await reader.ReadAsync(buffer.AsMemory(), token) > 0) { }
        } catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
    }
}
