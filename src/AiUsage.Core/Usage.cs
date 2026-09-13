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
        new("chatgpt", "Codex", false, "#10A586"),
        new("claude", "Claude Code", false, "#D87959"),
        new("opencode", "OpenCode", false, "#737983"),
        new("commandcode", "Command Code", false, "#8B5CF6")
    ];
    public static UsageSnapshot Sample(DateTimeOffset now) => new(now, [
        new("chatgpt", "Plus", 23, now.AddHours(3).AddMinutes(12), 38, now.AddDays(4)),
        new("claude", "Max", 7, now.AddHours(2).AddMinutes(42), 14, now.AddDays(3)),
        new("opencode", "Go", 11, now.AddHours(5), 24, now.AddDays(6)),
        new("commandcode", "Go", 12, now.AddHours(4), 31, now.AddDays(3), Windows: [new("Monthly credits", 18, now.AddDays(3))])
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
    public static UsageWindow[] Windows(Service service, UsageEntry? usage) => usage?.Windows
        ?? [new("5-hour", usage?.SessionPercent, usage?.SessionReset), new("Weekly", usage?.WeeklyPercent, usage?.WeeklyReset)];
    public static double? RemainingPercent(double? used) => used is null || !double.IsFinite(used.Value)
        ? null
        : 100 - Math.Clamp(used.Value, 0, 100);
    public static string Percent(double? used) => RemainingPercent(used) is double remaining ? $"{remaining:0}%" : "—";
    public static string Money(decimal? n) => n is null || n < 0 ? "—" : n.Value.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
    public static string Reset(DateTimeOffset? time, DateTimeOffset now)
    {
        if (time is null) return "Reset time unavailable";
        var left = time.Value - now;
        if (left <= TimeSpan.Zero) return "Waiting for reset";
        return left.TotalDays >= 1
            ? $"Resets {time.Value.ToLocalTime():M'/'d HH:mm}"
            : $"Resets in {(int)left.TotalHours}h {left.Minutes}m";
    }
    public static string Footer(UsageSnapshot data, DateTimeOffset now)
    {
        if (data.IsSample) return "Sample data · not actual usage";
        if (data.Notice is not null) return data.Notice;
        if (data.UpdatedAt is null) return "Waiting for connection · no usage data";
        if (data.UpdatedAt > now.AddMinutes(1)) return "Check update time";
        var age = now - data.UpdatedAt.Value;
        return age.TotalMinutes >= 5
            ? $"Last updated {data.UpdatedAt.Value.ToLocalTime():M'/'d HH:mm} · stale data"
            : $"Last updated {Math.Max(0, (int)age.TotalSeconds)}s ago";
    }
}
