using System.Text.Json;

namespace AiUsage;

// Semantic Adaptive Card colors inherit the Widgets Board's light/dark/high-contrast theme.
public static class Card
{
    static object Text(string text, bool subtle = false) => new { type = "TextBlock", text, wrap = true, size = "Small", spacing = "None", isSubtle = subtle };
    static object Action(string title, string verb) => new { type = "Action.Execute", title, verb, associatedInputs = "none" };
    static object Column(object[] items, object? width = null) => new { type = "Column", width = width ?? "stretch", items, spacing = "Small" };
    static object Bar(UsageWindow window, bool secondary) => new { type = "Image", url = CardImages.Progress(window.Percent, secondary), size = "Stretch", height = "3px", spacing = "None", altText = $"{window.Label} 남은 비율 {Labels.Percent(window.Percent)}" };
    public static string Render(Preferences prefs, UsageSnapshot data, DateTimeOffset now)
    {
        var body = new List<object> {
            new { type = "ActionSet", actions = new[] { Action(prefs.Settings ? "Status" : "● Status", "status"), Action(prefs.Settings ? "● Setting" : "Setting", "settings") }, spacing = "None" }
        };
        if (prefs.Settings)
        {
            body.Add(new { type = "TextBlock", text = "SERVICES", weight = "Bolder", size = "Small", spacing = "Medium" });
            foreach (var service in Catalog.Services)
            {
                var enabled = prefs.Enabled.Contains(service.Id);
                body.Add(new {
                    type = "ColumnSet", spacing = "Medium",
                    selectAction = Action($"{service.Name} {(enabled ? "끄기" : "켜기")}", "toggle:" + service.Id),
                    columns = new[] { Column([Text(service.Name)]), Column([new { type = "Image", url = ToggleImage(enabled), width = "34px", height = "20px", altText = enabled ? "켜짐" : "꺼짐" }], "auto") }
                });
            }
            if (!data.IsSample) body.Add(new { type = "ActionSet", actions = new[] {
                new { type = "Action.OpenUrl", title = "Codex 연결", url = "aiusage:login" },
                new { type = "Action.OpenUrl", title = "Claude 연결", url = "aiusage:login-claude" }
            }, spacing = "Medium" });
            if (!data.IsSample) body.Add(new { type = "ActionSet", actions = new[] {
                new { type = "Action.OpenUrl", title = "Gemini 연결", url = "aiusage:login-gemini" }
            }, spacing = "Small" });
        }
        else
        {
            foreach (var service in Catalog.Services.Where(s => prefs.Enabled.Contains(s.Id)))
            {
                var usage = data.Services.FirstOrDefault(x => x.Id == service.Id);
                var items = new List<object> {
                    new { type = "TextBlock", text = $"{service.Name}  ·  {(service.IsApi ? "API" : usage?.Plan ?? "플랜 미연결")}", weight = "Bolder", size = "Small", spacing = "None", wrap = true }
                };
                if (service.IsApi) items.Add(Text($"이번 달 {Labels.Money(usage?.MonthCost)} · 오늘 {Labels.Money(usage?.DayCost)}", true));
                else
                {
                    if (usage?.Status is not null) items.Add(Text(usage.Status, true));
                    else {
                        var windows = Labels.Windows(service, usage);
                        foreach (var window in windows) items.Add(Text($"{window.Label} {Labels.Percent(window.Percent)} · {Labels.Reset(window.Reset, now)}", true));
                        for (var i = 0; i < windows.Length; i++) items.Add(Bar(windows[i], i != 0));
                    }
                }
                body.Add(new { type = "ColumnSet", separator = true, spacing = "Small", columns = new[] {
                    Column([new { type = "Image", url = CardImages.Dot(service.Color), width = "24px", height = "24px", altText = service.Name }], "auto"),
                    Column(items.ToArray())
                } });
            }
            if (prefs.Enabled.Count == 0) body.Add(Text("표시할 AI가 없습니다. Setting에서 서비스를 켜주세요.", true));
        }
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["type"] = "AdaptiveCard", ["version"] = "1.5", ["body"] = body });
    }
    // PNG data URIs are supplied by the packaged app; no external requests for toggle assets.
    public static Func<bool, string> ToggleImage { get; set; } = enabled => enabled ? "" : "";
}
