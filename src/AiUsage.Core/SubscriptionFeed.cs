using System.ComponentModel;
using System.Text.Json;

namespace AiUsage;

public sealed class SubscriptionFeed
{
    sealed class Slot(string id, Func<CancellationToken, Task<UsageEntry>> fetch, TimeSpan interval)
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public UsageEntry Current = new(id, Status: UiText.Choose("Checking connection…", "연결 확인 중…"));
        public DateTimeOffset Next;
        public DateTimeOffset BlockedUntil;
        public readonly Func<CancellationToken, Task<UsageEntry>> Fetch = fetch;
        public readonly TimeSpan Interval = interval;
    }
    readonly Dictionary<string, Slot> slots = new() {
        ["chatgpt"] = new("chatgpt", new CodexClient().FetchAsync, TimeSpan.FromMinutes(2)),
        ["claude"] = new("claude", new ClaudeClient().FetchAsync, TimeSpan.FromMinutes(5)),
        ["gemini"] = new("gemini", new GeminiClient().FetchAsync, TimeSpan.FromMinutes(5))
    };
    public UsageSnapshot Read(IReadOnlySet<string> enabled)
    {
        var stored = LocalStore.ReadUsage();
        if (stored.IsSample) return stored;
        var live = slots.Where(x => enabled.Contains(x.Key)).Select(x => Volatile.Read(ref x.Value.Current)).ToArray();
        var ready = live.Count(x => x.UpdatedAt is not null && x.Status is null);
        return stored with {
            Services = stored.Services.Where(x => !slots.ContainsKey(x.Id)).Concat(live).ToArray(),
            Notice = live.Length == 0 ? null : UiText.Choose($"Connected {ready}/{live.Length} · auto refresh", $"연결 {ready}/{live.Length} · 자동 업데이트")
        };
    }
    public Task RefreshAsync(IReadOnlySet<string> enabled, bool force = false, CancellationToken cancellation = default)
    {
        if (LocalStore.ReadUsage().IsSample) return Task.CompletedTask;
        return Task.WhenAll(slots.Where(x => enabled.Contains(x.Key)).Select(x => RefreshOneAsync(x.Key, x.Value, force, cancellation)));
    }
    async Task RefreshOneAsync(string id, Slot slot, bool force, CancellationToken cancellation)
    {
        if (!await slot.Gate.WaitAsync(0, cancellation)) return;
        try {
            if (DateTimeOffset.UtcNow < slot.BlockedUntil || (!force && DateTimeOffset.UtcNow < slot.Next)) return;
            slot.Next = DateTimeOffset.UtcNow.Add(slot.Interval);
            try { Volatile.Write(ref slot.Current, await slot.Fetch(cancellation)); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { Volatile.Write(ref slot.Current, new(id, Status: UiText.Choose("Request timed out · waiting to retry", "조회 시간 초과 · 재시도 대기"))); }
            catch (Exception e) when (e is UsageConnectionException or CodexException or IOException or Win32Exception or JsonException or UnauthorizedAccessException or HttpRequestException) {
                if (e is UsageConnectionException { RetryAfter: { } delay }) slot.BlockedUntil = slot.Next = DateTimeOffset.UtcNow.Add(delay);
                Volatile.Write(ref slot.Current, new(id, Status: e is UsageConnectionException or CodexException ? e.Message : UiText.Choose("Connection failed · check installation, login, and network", "연결 실패 · 설치·로그인·네트워크 확인")));
            }
        } finally { slot.Gate.Release(); }
    }
}
