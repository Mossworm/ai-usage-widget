using AiUsage;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

if (args.Contains("--live")) {
    try {
        var usage = await new CodexClient().FetchAsync();
        // Deliberately emit only display fields: no account response, identity or credentials.
        Console.WriteLine(JsonSerializer.Serialize(usage, LocalStore.Json));
        return;
    } catch (Exception e) {
        Console.Error.WriteLine(e is CodexException ? e.Message : "Live check failed: " + e.GetType().Name);
        Environment.ExitCode = 1; return;
    }
}
if (args.Contains("--live-claude") || args.Contains("--live-gemini")) {
    try {
        var usage = args.Contains("--live-claude") ? await new ClaudeClient().FetchAsync() : await new GeminiClient().FetchAsync();
        Console.WriteLine(JsonSerializer.Serialize(usage, LocalStore.Json)); return;
    } catch (Exception e) {
        Console.Error.WriteLine(e is UsageConnectionException ? e.Message : "Live check failed: " + e.GetType().Name);
        Environment.ExitCode = 1; return;
    }
}

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
var manifest = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppxManifest.xml"));
XNamespace packageNs = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
var definition = manifest.Descendants(packageNs + "Definition").Single(x => (string?)x.Attribute("Id") == "AiUsage");
Check((string?)definition.Attribute("IsCustomizable") == "true", "Widget menu enables customization");
XNamespace comNs = "http://schemas.microsoft.com/appx/manifest/com/windows10";
var customizationInterface = manifest.Descendants(comNs + "Interface")
    .SingleOrDefault(x => string.Equals((string?)x.Attribute("Id"), "38C3A963-DD93-479D-9276-04BF84EE1816", StringComparison.OrdinalIgnoreCase));
var customizationProxy = customizationInterface?.Parent?.Elements(comNs + "ProxyStub")
    .SingleOrDefault(x => (string?)x.Attribute("Id") == (string?)customizationInterface.Attribute("ProxyStubClsid"));
Check(string.Equals((string?)customizationProxy?.Attribute("Id"), "93022121-B6C9-4A22-90DB-763E24FD99E1", StringComparison.OrdinalIgnoreCase)
    && (string?)customizationProxy?.Attribute("Path") == @"Provider\Microsoft.Windows.Widgets.dll", "Customization callback has the packaged Widgets SDK proxy");
var now = new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.FromHours(9));
var data = Catalog.Sample(now);
var prefs = new Preferences();
Card.ToggleImage = enabled => "data:image/png;base64," + (enabled ? "ON" : "OFF");
Check(prefs.Enabled.Count == 6, "Six services enabled initially");
prefs.Toggle("claude");
Check(!prefs.Enabled.Contains("claude"), "Toggle disables service");
prefs.Toggle("invalid-id");
Check(prefs.Enabled.Count == 5, "Unknown service is ignored");
using (var doc = JsonDocument.Parse(Card.Render(prefs, data, now))) {
    var text = doc.RootElement.ToString();
    Check(!text.Contains("Claude  ·"), "Disabled subscription hidden");
    Check(text.Contains("Claude API"), "API visibility independent of subscription");
    var body = doc.RootElement.GetProperty("body");
    Check(body[body.GetArrayLength() - 1].GetProperty("type").GetString() == "ColumnSet", "Status has no footer");
    Check(!text.Contains("\"verb\":\"status\"") && !text.Contains("\"verb\":\"settings\""), "Widget has no Status or Settings navigation buttons");
}
prefs.Settings = true;
using (var doc = JsonDocument.Parse(Card.Render(prefs, data, now))) {
    var body = doc.RootElement.GetProperty("body");
    Check(body.GetArrayLength() == 7, "Sample settings contains header and six toggles");
    Check(body[3].GetProperty("selectAction").GetProperty("verb").GetString() == "toggle:claude", "Setting row targets correct service");
}
prefs.Settings = false; prefs.Enabled.Clear();
using (var doc = JsonDocument.Parse(Card.Render(prefs, data, now))) Check(doc.RootElement.GetProperty("body")[0].GetProperty("text").GetString()!.Contains("No AI services to display"), "All-disabled empty state");
Check(Labels.Percent(null) == "—", "Unknown usage is not shown as zero");
Check(Labels.Percent(100) == "0%" && Labels.Percent(0) == "100%", "Exhausted quota is zero remaining");
Check(Labels.Percent(130) == "0%" && Labels.Percent(-1) == "100%", "Remaining progress values bounded");
Check(Labels.Percent(double.NaN) == "—", "Nonfinite usage rejected");
Check(Labels.Money(null) == "—" && Labels.Money(-1) == "—", "Unknown or invalid cost not shown as zero");
Check(Labels.Money(0) == "$0.00", "Known zero cost distinct from unknown");
Check(Labels.Reset(now.AddMinutes(-1), now) == "Waiting for reset", "Expired reset never becomes negative countdown");
Check(Labels.Reset(now.AddMinutes(162), now) == "Resets in 2h 42m", "Countdown computed from timestamp");
Check(Labels.Reset(now.AddDays(7), now).Contains("/"), "Long reset date uses slash separator");
Check(Labels.Footer(new(null, []), now).Contains("Waiting for connection"), "Disconnected footer");
Check(Labels.Footer(new(now.AddHours(-1), []), now).Contains("stale data"), "Stale data is indicated");
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ko-KR");
Check(Labels.Reset(now.AddMinutes(162), now) == "2시간 42분 후 리셋", "ko-KR selects Korean translation");
using (var doc = JsonDocument.Parse(Card.Render(prefs, data, now))) {
    var body = doc.RootElement.GetProperty("body");
    Check(body[0].GetProperty("text").GetString()!.Contains("표시할 AI가 없습니다"), "ko-KR localizes widget content");
}
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
Check(Labels.Reset(now.AddMinutes(162), now) == "Resets in 2h 42m", "Non-ko-KR locale falls back to English");
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
var dir = Path.Combine(Path.GetTempPath(), "AiUsageChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try {
    var path = Path.Combine(dir, "settings.json");
    LocalStore.SavePreferences(prefs, path);
    Check(LocalStore.ReadPreferences(path).Enabled.Count == 0, "All-off configuration survives reload");
    File.WriteAllText(path, "broken"); Check(LocalStore.ReadPreferences(path).Enabled.Count == 6, "Malformed settings recover");
    File.WriteAllText(path, "{\"Enabled\":null}"); Check(LocalStore.ReadPreferences(path).Enabled.Count == 6, "Null settings recover");
    Check(LocalStore.ReadUsage(Path.Combine(dir, "missing.json")).Services.Length == 0, "Missing usage file is disconnected");
    File.WriteAllText(path, "{\"Services\":null}"); Check(LocalStore.ReadUsage(path).Services.Length == 0, "Invalid snapshot recovers");
    File.WriteAllText(path, JsonSerializer.Serialize(data, LocalStore.Json));
    Check(LocalStore.ReadUsage(path).Services[1].MonthCost == 12.48m, "Usage snapshot preserves monetary precision");
} finally { foreach (var file in Directory.GetFiles(dir)) File.Delete(file); Directory.Delete(dir); }
UsageEntry Parse(string json) { using var doc = JsonDocument.Parse(json); return CodexClient.Parse(doc.RootElement, "plus", now); }
var actual = Parse("""{"rateLimits":{"limitId":"codex","primary":{"usedPercent":23,"windowDurationMins":300,"resetsAt":1789185600},"secondary":{"usedPercent":41,"windowDurationMins":10080,"resetsAt":1789790400},"planType":"pro"}}""");
Check(actual.SessionPercent == 23 && actual.WeeklyPercent == 41, "RPC usedPercent is used, not remaining percentage");
Check(actual.Plan == "pro" && actual.UpdatedAt == now, "Plan and actual fetch timestamp mapped");
Check(actual.SessionReset == DateTimeOffset.FromUnixTimeSeconds(1789185600), "Unix seconds converted correctly");
var reversed = Parse("""{"rateLimits":{"primary":{"usedPercent":72,"windowDurationMins":10080},"secondary":{"usedPercent":12,"windowDurationMins":300}}}""");
Check(reversed.SessionPercent == 12 && reversed.WeeklyPercent == 72, "Windows mapped by duration even if reversed");
var weeklyOnly = Parse("""{"rateLimits":{"primary":{"usedPercent":36,"windowDurationMins":10080},"secondary":null}}""");
Check(weeklyOnly.SessionPercent is null && weeklyOnly.WeeklyPercent == 36, "Weekly-only account does not invent five-hour quota");
var multiple = Parse("""{"rateLimits":{"limitId":"codex_other","primary":{"usedPercent":99,"windowDurationMins":300}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":7,"windowDurationMins":300}}}}""");
Check(multiple.SessionPercent == 7, "Codex bucket preferred over other model bucket");
var otherWindow = Parse("""{"rateLimits":{"primary":{"usedPercent":45,"windowDurationMins":15}}}""");
Check(otherWindow.SessionPercent is null && otherWindow.WeeklyPercent is null && otherWindow.Status is not null, "Unsupported duration is explicit, not mislabeled");
var invalid = Parse("""{"rateLimits":{"primary":{"usedPercent":500,"windowDurationMins":300,"resetsAt":9223372036854775807}}}""");
Check(invalid.SessionPercent is null && invalid.SessionReset is null, "Invalid upstream percentage and timestamp rejected");
var nulls = Parse("""{"rateLimits":{"primary":null,"secondary":null}}""");
Check(nulls.SessionPercent is null && nulls.Plan == "plus", "Null windows and account plan fallback");
try { Parse("""{"rateLimits":{"limitId":"code_review"}}"""); Check(false, "Wrong bucket must fail"); }
catch (CodexException) { Check(true, "Unrelated quota is not displayed as Codex"); }
using (var doc = JsonDocument.Parse(Card.Render(new() { Settings = true }, new(null, []), now))) {
    var body = doc.RootElement.GetProperty("body");
    var firstRow = body[body.GetArrayLength() - 2].GetProperty("actions");
    var secondRow = body[body.GetArrayLength() - 1].GetProperty("actions");
    Check(firstRow.GetArrayLength() == 2 && secondRow.GetArrayLength() == 1, "Connection actions are limited to two per row");
    Check(firstRow[0].GetProperty("url").GetString() == "aiusage:login", "Native widget connects through registered login protocol");
}
await SubscriptionChecks.RunAsync(Check, now);
Console.WriteLine($"{passed} checks passed.");
