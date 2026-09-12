using System.Globalization;
using System.Text.Json;

namespace AiUsage;

public record Service(string Id, string Name, bool IsApi, string Color);
public record UsageWindow(string Label, double? Percent, DateTimeOffset? Reset);
public record UsageEntry(string Id, string? Plan = null, double? SessionPercent = null,
    DateTimeOffset? SessionReset = null, double? WeeklyPercent = null,
    DateTimeOffset? WeeklyReset = null, decimal? MonthCost = null, decimal? DayCost = null,
    string? Status = null, DateTimeOffset? UpdatedAt = null, UsageWindow[]? Windows = null);
public record UsageSnapshot(DateTimeOffset? UpdatedAt, UsageEntry[] Services, bool IsSample = false, string? Notice = null);

public static class Catalog
{
    public static readonly Service[] Services = [
        new("chatgpt", "Codex (ChatGPT)", false, "#10A586"),
        new("openai-api", "ChatGPT API", true, "#10A586"),
        new("claude", "Claude", false, "#D87959"),
        new("anthropic-api", "Claude API", true, "#D87959"),
        new("gemini", "Gemini CLI", false, "#4385EF"),
        new("gemini-api", "Gemini API", true, "#4385EF")
    ];
    public static UsageSnapshot Sample(DateTimeOffset now) => new(now, [
        new("chatgpt", "Plus", 23, now.AddHours(3).AddMinutes(12), 38, now.AddDays(4)),
        new("openai-api", MonthCost: 12.48m, DayCost: 0.82m),
        new("claude", "Max", 7, now.AddHours(2).AddMinutes(42), 14, now.AddDays(3)),
        new("anthropic-api", MonthCost: 8.64m, DayCost: 0.36m),
        new("gemini", "Pro", Windows: [new("gemini-pro", 51, now.AddHours(10)), new("gemini-flash", 16, now.AddHours(10))]),
        new("gemini-api")
    ], true);
}

public sealed class Preferences
{
    public HashSet<string> Enabled { get; set; } = Catalog.Services.Select(s => s.Id).ToHashSet();
    public bool Settings { get; set; }
    public void Toggle(string id)
    {
        if (!Catalog.Services.Any(s => s.Id == id)) return;
        if (!Enabled.Remove(id)) Enabled.Add(id);
    }
}

public static class LocalStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageWidget");
    public static Preferences ReadPreferences(string? path = null)
    {
        try {
            var value = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path ?? Path.Combine(Folder, "settings.json")), Json);
            if (value?.Enabled is not null) return value;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    public static void SavePreferences(Preferences value, string? path = null)
    {
        path ??= Path.Combine(Folder, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Json)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static UsageSnapshot ReadUsage(string? path = null)
    {
        try {
            var result = JsonSerializer.Deserialize<UsageSnapshot>(File.ReadAllText(path ?? Path.Combine(Folder, "usage.json")), Json);
            if (result?.Services is not null && result.Services.All(x => x is not null)) return result;
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return new(null, []);
    }
}

public static class Labels
{
    public static UsageWindow[] Windows(Service service, UsageEntry? usage) => usage?.Windows ?? (service.Id == "gemini"
        ? [new(UiText.Choose("Model quota", "모델 한도"), null, null)]
        : [new(UiText.Choose("5-hour", "5시간"), usage?.SessionPercent, usage?.SessionReset), new(UiText.Choose("Weekly", "주간"), usage?.WeeklyPercent, usage?.WeeklyReset)]);
    public static double? RemainingPercent(double? used) => used is null || !double.IsFinite(used.Value)
        ? null
        : 100 - Math.Clamp(used.Value, 0, 100);
    public static string Percent(double? used) => RemainingPercent(used) is double remaining ? $"{remaining:0}%" : "—";
    public static string Money(decimal? n) => n is null || n < 0 ? "—" : n.Value.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    public static string Reset(DateTimeOffset? time, DateTimeOffset now)
    {
        if (time is null) return UiText.Choose("Reset time unavailable", "리셋 시간 없음");
        var left = time.Value - now;
        if (left <= TimeSpan.Zero) return UiText.Choose("Waiting for reset", "리셋 확인 대기");
        return left.TotalDays >= 1
            ? UiText.Choose($"Resets {time.Value.ToLocalTime():M'/'d HH:mm}", $"{time.Value.ToLocalTime():M'/'d HH:mm} 리셋")
            : UiText.Choose($"Resets in {(int)left.TotalHours}h {left.Minutes}m", $"{(int)left.TotalHours}시간 {left.Minutes}분 후 리셋");
    }
    public static string Footer(UsageSnapshot data, DateTimeOffset now)
    {
        if (data.IsSample) return UiText.Choose("Sample data · not actual usage", "샘플 데이터 · 실제 사용량이 아닙니다");
        if (data.Notice is not null) return data.Notice;
        if (data.UpdatedAt is null) return UiText.Choose("Waiting for connection · no usage data", "연결 대기 · 사용량 데이터 없음");
        if (data.UpdatedAt > now.AddMinutes(1)) return UiText.Choose("Check update time", "업데이트 시간 확인 필요");
        var age = now - data.UpdatedAt.Value;
        return age.TotalMinutes >= 5
            ? UiText.Choose($"Last updated {data.UpdatedAt.Value.ToLocalTime():M'/'d HH:mm} · stale data", $"마지막 업데이트 {data.UpdatedAt.Value.ToLocalTime():M'/'d HH:mm} · 오래된 데이터")
            : UiText.Choose($"Last updated {Math.Max(0, (int)age.TotalSeconds)}s ago", $"마지막 업데이트 {Math.Max(0, (int)age.TotalSeconds)}초 전");
    }
}
