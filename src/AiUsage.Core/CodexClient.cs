using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace AiUsage;

public sealed class CodexException(string message) : Exception(message);

public static class CodexExecutable
{
    const string Exe = "codex.exe";
    // An explicit pin wins outright. Otherwise every known install location contributes a candidate
    // and the most recently written binary is chosen, so a freshly updated IDE extension beats a
    // stale global install instead of losing to whichever folder happened to be searched first.
    public static string Find()
    {
        foreach (var name in new[] { "CODEX_EXECUTABLE", "CODEX_BIN" }) {
            var configured = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(configured)) continue;
            if (Path.IsPathFullyQualified(configured) && configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(configured)) return configured;
            throw new CodexException($"Set {name} to the full path of {Exe}.");
        }
        var found = Candidates().Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(Written).FirstOrDefault();
        return found ?? throw new CodexException("Codex CLI was not found. Install it and try again.");
    }
    static DateTime Written(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }
    static IEnumerable<string> Candidates()
    {
        // On PATH, or beside a global npm install.
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Concat([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs")]);
        foreach (var folder in folders.Where(Path.IsPathFullyQualified)) {
            yield return Path.Combine(folder, Exe);
            // Resolve the native npm binary instead of passing a shell command through cmd.exe.
            var package = Path.Combine(folder, "node_modules", "@openai", "codex");
            yield return Path.Combine(package, "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", Exe);
            yield return Path.Combine(package, "vendor", "x86_64-pc-windows-msvc", "codex", Exe);
        }
        // The standalone installer keeps one folder per version.
        foreach (var version in Children(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin")))
            yield return Path.Combine(version, Exe);
        // The binary shipped inside the ChatGPT extension for VS Code and the editors forked from it.
        foreach (var root in ExtensionRoots())
            foreach (var extension in Children(root).Where(x => Path.GetFileName(x).StartsWith("openai.chatgpt-", StringComparison.OrdinalIgnoreCase)))
                foreach (var relative in new[] { Path.Combine("bin", "windows-x86_64", Exe), Path.Combine("binaries", "windows-x86_64", Exe), Path.Combine("bin", Exe) })
                    yield return Path.Combine(extension, relative);
    }
    static IEnumerable<string> ExtensionRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [.. new[] { ".vscode", ".vscode-insiders", ".windsurf", ".cursor" }.Select(ide => Path.Combine(home, ide, "extensions"))];
    }
    static IEnumerable<string> Children(string folder)
    {
        try { return Directory.EnumerateDirectories(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return []; }
    }
    internal static Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo(Find()) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new CodexException("Could not start Codex.");
    }
    internal static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}

public sealed class CodexClient
{
    public async Task<UsageEntry> FetchAsync(CancellationToken cancellation = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var process = CodexExecutable.Start("app-server", "--listen", "stdio://");
        // Drain stderr without displaying or retaining potentially sensitive CLI diagnostics.
        var drain = DrainAsync(process.StandardError, timeout.Token);
        try {
            await RpcAsync(process, 1, "initialize", new { clientInfo = new { name = "ai_usage_widget", title = "AI Usage Widget", version = "1.1.0" } }, timeout.Token);
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}".AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var account = await RpcAsync(process, 2, "account/read", new { refreshToken = false }, timeout.Token);
            var identity = Get(account, "account");
            var type = String(identity, "type");
            if (identity.ValueKind == JsonValueKind.Null || identity.ValueKind == JsonValueKind.Undefined)
                throw new CodexException("Codex sign-in required. Run `codex login` once.");
            if (type == "apiKey") throw new CodexException("You are signed in with an API key. Sign in to Codex with your ChatGPT account.");
            var limits = await RpcAsync(process, 3, "account/rateLimits/read", new { }, timeout.Token);
            return Parse(limits, String(identity, "planType"), DateTimeOffset.Now);
        }
        finally { timeout.Cancel(); CodexExecutable.Stop(process); await drain; }
    }
    static async Task DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[2048];
        try { while (await reader.ReadAsync(buffer.AsMemory(), token) > 0) { } }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    static async Task<JsonElement> RpcAsync(Process process, int id, string method, object parameters, CancellationToken token)
    {
        var request = JsonSerializer.Serialize(new { id, method, @params = parameters });
        await process.StandardInput.WriteLineAsync(request.AsMemory(), token);
        await process.StandardInput.FlushAsync(token);
        while (true) {
            var line = await process.StandardOutput.ReadLineAsync(token);
            if (line is null) throw new CodexException("The Codex connection closed. Try again.");
            if (line.Length > 2_000_000) throw new CodexException("The Codex response exceeded the expected size.");
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var responseId = Get(root, "id");
            if (responseId.ValueKind != JsonValueKind.Number || !responseId.TryGetInt32(out var resultId) || resultId != id) continue;
            if (root.TryGetProperty("error", out var error)) {
                var message = String(error, "message") ?? "";
                if (new[] { "auth", "login", "401", "unauthorized" }.Any(s => message.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    throw new CodexException("Codex sign-in expired. Run `codex login` again.");
                throw new CodexException("Could not retrieve Codex usage. It will retry shortly.");
            }
            var result = Get(root, "result");
            if (result.ValueKind != JsonValueKind.Object) throw new CodexException("The Codex response format is invalid.");
            return result.Clone();
        }
    }
    public static UsageEntry Parse(JsonElement result, string? plan, DateTimeOffset now)
    {
        var limits = Get(Get(result, "rateLimitsByLimitId"), "codex");
        if (limits.ValueKind != JsonValueKind.Object) limits = Get(result, "rateLimits");
        var limitId = String(limits, "limitId");
        if (limits.ValueKind != JsonValueKind.Object || (limitId is not null && limitId != "codex"))
            throw new CodexException("No default Codex quota data is available.");
        double? session = null, weekly = null;
        DateTimeOffset? sessionReset = null, weeklyReset = null;
        foreach (var key in new[] { "primary", "secondary" }) {
            var window = Get(limits, key);
            var duration = Get(window, "windowDurationMins");
            // Never call an arbitrary primary/model/review window the five-hour quota.
            if (!duration.TryNumber(out var minutes) || (minutes != 300 && minutes != 10080)) continue;
            var percent = Get(window, "usedPercent");
            double? used = percent.TryNumber(out var n) && n >= 0 && n <= 100 ? n : null;
            DateTimeOffset? reset = null;
            var timestamp = Get(window, "resetsAt");
            if (timestamp.ValueKind == JsonValueKind.Number && timestamp.TryGetInt64(out var seconds)) {
                try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { }
            }
            if (minutes == 300) { session = used; sessionReset = reset; }
            else { weekly = used; weeklyReset = reset; }
        }
        return new("chatgpt", String(limits, "planType") ?? plan, session, sessionReset, weekly, weeklyReset,
            Status: session is null && weekly is null ? "This account does not provide 5-hour or weekly quotas." : null, UpdatedAt: now);
    }
    internal static JsonElement Get(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    internal static string? String(JsonElement element, string name) { var value = Get(element, name); return value.ValueKind == JsonValueKind.String ? value.GetString() : null; }
}
static class JsonNumber
{
    public static bool TryNumber(this JsonElement value, out double number) { number = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number); }
}

public sealed class CodexUsageFeed
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly CodexClient client = new();
    UsageEntry current = new("chatgpt", Status: "Connecting to Codex usage…");
    DateTimeOffset nextFetch;
    public UsageSnapshot Read(bool includeCodex = true)
    {
        var stored = LocalStore.ReadUsage();
        if (stored.IsSample || !includeCodex) return stored;
        var value = Volatile.Read(ref current);
        return stored with {
            Services = stored.Services.Where(x => x.Id != "chatgpt").Append(value).ToArray(),
            Notice = value.Status ?? ("Codex · " + Labels.Footer(new(value.UpdatedAt, []), DateTimeOffset.Now))
        };
    }
    public async Task RefreshAsync(bool force = false, CancellationToken cancellation = default)
    {
        if (!await gate.WaitAsync(0, cancellation)) return;
        try {
            if (LocalStore.ReadUsage().IsSample) return;
            if (!force && DateTimeOffset.UtcNow < nextFetch) return;
            nextFetch = DateTimeOffset.UtcNow.AddMinutes(2);
            try { Volatile.Write(ref current, await client.FetchAsync(cancellation)); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { Volatile.Write(ref current, new("chatgpt", Status: "The Codex request timed out. It will retry shortly.")); }
            catch (Exception e) when (e is CodexException or IOException or Win32Exception or JsonException or UnauthorizedAccessException) {
                Volatile.Write(ref current, new("chatgpt", Status: e is CodexException ? e.Message : "Could not connect to Codex. Check the installation and network."));
            }
        } finally { gate.Release(); }
    }
}
