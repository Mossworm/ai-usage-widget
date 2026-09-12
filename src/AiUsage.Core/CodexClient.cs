using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace AiUsage;

public sealed class CodexException(string message) : Exception(message);

public static class CodexExecutable
{
    public static string Find()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_BIN");
        if (!string.IsNullOrWhiteSpace(configured)) {
            if (Path.IsPathFullyQualified(configured) && configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(configured)) return configured;
            throw new CodexException("CODEX_BIN에 codex.exe의 전체 경로를 지정해주세요.");
        }
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Concat([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs")]);
        foreach (var folder in folders.Where(Path.IsPathFullyQualified)) {
            var direct = Path.Combine(folder, "codex.exe");
            if (File.Exists(direct)) return direct;
            // Resolve the native npm binary instead of passing a shell command through cmd.exe.
            var package = Path.Combine(folder, "node_modules", "@openai", "codex");
            foreach (var path in new[] {
                Path.Combine(package, "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"),
                Path.Combine(package, "vendor", "x86_64-pc-windows-msvc", "codex", "codex.exe")
            }) if (File.Exists(path)) return path;
        }
        throw new CodexException("Codex CLI가 없습니다. 설치 후 다시 실행해주세요.");
    }
    internal static Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo(Find()) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new CodexException("Codex를 실행하지 못했습니다.");
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
                throw new CodexException("Codex 로그인이 필요합니다. Setting에서 연결해주세요.");
            if (type == "apiKey") throw new CodexException("API 키 로그인입니다. ChatGPT 계정으로 Codex에 로그인해주세요.");
            var limits = await RpcAsync(process, 3, "account/rateLimits/read", new { }, timeout.Token);
            return Parse(limits, String(identity, "planType"), DateTimeOffset.Now);
        }
        finally { timeout.Cancel(); CodexExecutable.Stop(process); await drain; }
    }
    // Codex CLI owns browser OAuth, secure credential storage and token refresh.
    // The widget never reads auth.json or receives password/access/refresh tokens.
    public static async Task LoginAsync(CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var process = CodexExecutable.Start("login");
        var stdout = DrainAsync(process.StandardOutput, timeout.Token);
        var stderr = DrainAsync(process.StandardError, timeout.Token);
        try {
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) throw new CodexException("로그인을 완료하지 못했습니다. 다시 연결해주세요.");
        } finally { timeout.Cancel(); CodexExecutable.Stop(process); await Task.WhenAll(stdout, stderr); }
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
            if (line is null) throw new CodexException("Codex 연결이 종료되었습니다. 다시 시도해주세요.");
            if (line.Length > 2_000_000) throw new CodexException("Codex 응답 크기가 예상 범위를 초과했습니다.");
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var responseId = Get(root, "id");
            if (responseId.ValueKind != JsonValueKind.Number || !responseId.TryGetInt32(out var resultId) || resultId != id) continue;
            if (root.TryGetProperty("error", out var error)) {
                var message = String(error, "message") ?? "";
                if (new[] { "auth", "login", "401", "unauthorized" }.Any(s => message.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    throw new CodexException("Codex 인증이 만료되었습니다. Setting에서 다시 연결해주세요.");
                throw new CodexException("Codex 사용량 조회에 실패했습니다. 잠시 후 다시 시도합니다.");
            }
            var result = Get(root, "result");
            if (result.ValueKind != JsonValueKind.Object) throw new CodexException("Codex 응답 형식이 올바르지 않습니다.");
            return result.Clone();
        }
    }
    public static UsageEntry Parse(JsonElement result, string? plan, DateTimeOffset now)
    {
        var limits = Get(Get(result, "rateLimitsByLimitId"), "codex");
        if (limits.ValueKind != JsonValueKind.Object) limits = Get(result, "rateLimits");
        var limitId = String(limits, "limitId");
        if (limits.ValueKind != JsonValueKind.Object || (limitId is not null && limitId != "codex"))
            throw new CodexException("Codex 기본 한도 데이터가 없습니다.");
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
            Status: session is null && weekly is null ? "5시간·주간 한도가 제공되지 않는 계정입니다." : null, UpdatedAt: now);
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
    UsageEntry current = new("chatgpt", Status: "Codex 사용량 연결 중…");
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
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { Volatile.Write(ref current, new("chatgpt", Status: "Codex 조회 시간이 초과되었습니다. 잠시 후 재시도합니다.")); }
            catch (Exception e) when (e is CodexException or IOException or Win32Exception or JsonException or UnauthorizedAccessException) {
                Volatile.Write(ref current, new("chatgpt", Status: e is CodexException ? e.Message : "Codex에 연결할 수 없습니다. 설치·네트워크를 확인해주세요."));
            }
        } finally { gate.Release(); }
    }
}
