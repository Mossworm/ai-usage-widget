using System.Diagnostics;
using System.Text.Json;

namespace AiUsage;

public static class SubscriptionLogin
{
    public static string Name(string id) => Catalog.Services.FirstOrDefault(s => s.Id == id && !s.IsApi)?.Name
        ?? throw new ArgumentException("Unknown subscription");
    public static string? FromArgument(string argument) => argument.ToLowerInvariant() switch {
        "--login" or "aiusage:login" => "chatgpt",
        "--login-claude" or "aiusage:login-claude" => "claude",
        "--login-gemini" or "aiusage:login-gemini" => "gemini", _ => null
    };
    public static async Task ConnectAsync(string id, CancellationToken cancellation = default)
    {
        if (id == "chatgpt") { await CodexClient.LoginAsync(cancellation); return; }
        if (id is not ("claude" or "gemini")) throw new ArgumentException("Unknown subscription");
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
            } else {
                await RequestAsync(process, 1, "initialize", new { protocolVersion = 1, clientCapabilities = new { }, clientInfo = new { name = "ai-usage-widget", version = "1.2.0" } }, timeout.Token);
                await RequestAsync(process, 2, "authenticate", new { methodId = "oauth-personal" }, timeout.Token);
                // No session/new or session/prompt: authentication only, no model calls.
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
        } else {
            info.FileName = FindFile(folders.Select(f => Path.Combine(f, "node.exe")), "Node.js");
            var entry = FindFile(folders.Select(f => Path.Combine(f, "node_modules", "@google", "gemini-cli", "bundle", "gemini.js")), "Gemini");
            info.ArgumentList.Add(entry); info.ArgumentList.Add("--acp");
        }
        return info;
    }
    static async Task RequestAsync(Process process, int id, string method, object parameters, CancellationToken token)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }).AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
        while (true) {
            var line = await process.StandardOutput.ReadLineAsync(token);
            if (line is null) throw new UsageConnectionException("The Gemini login connection closed");
            using var doc = JsonDocument.Parse(line);
            var responseId = CodexClient.Get(doc.RootElement, "id");
            if (responseId.ValueKind != JsonValueKind.Number || !responseId.TryGetInt32(out var number) || number != id) continue;
            if (doc.RootElement.TryGetProperty("error", out _)) throw new UsageConnectionException("Gemini login did not complete · try connecting again");
            return;
        }
    }
    static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[2048];
        try { while (await reader.ReadAsync(buffer.AsMemory(), token) > 0) { }
        } catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException) { }
    }
}
