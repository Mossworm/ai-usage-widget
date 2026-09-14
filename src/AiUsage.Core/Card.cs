using System.Text.Json;

namespace AiUsage;

// Semantic Adaptive Card colors inherit the Widgets Board's light/dark/high-contrast theme.
public static class Card
{
    static object Text(string text, bool subtle = false) => new { type = "TextBlock", text, wrap = true, size = "Small", spacing = "None", isSubtle = subtle };
    static object Action(string title, string verb) => new { type = "Action.Execute", title, verb, associatedInputs = "none" };
    static object Column(object[] items, object? width = null) => new { type = "Column", width = width ?? "stretch", items, spacing = "Small" };
    static object Bar(UsageWindow window, int index) => new { type = "Image", url = CardImages.Progress(window.Percent, index), size = "Stretch", height = "3px", spacing = "None", altText = $"{window.Label} remaining {Labels.Percent(window.Percent)}" };
    public static string Render(Preferences prefs, UsageSnapshot data, DateTimeOffset now)
    {
        var body = new List<object>();
        if (prefs.Settings)
        {
            body.Add(new { type = "TextBlock", text = "SERVICES", weight = "Bolder", size = "Small", spacing = "Medium" });
            foreach (var service in Catalog.Services)
            {
                var enabled = prefs.Enabled.Contains(service.Id);
                body.Add(new {
                    type = "ColumnSet", spacing = "Medium",
                    selectAction = Action($"{service.Name} {(enabled ? "turn off" : "turn on")}", "toggle:" + service.Id),
                    columns = new[] {
                        Column([Text(service.Name)]),
                        !data.IsSample ? Column([new { type = "ActionSet", actions = new[] {
                            new { type = "Action.OpenUrl", title = "Connect", url = service.Id switch {
                                "chatgpt" => "aiusage:login",
                                "claude" => "aiusage:login-claude",
                                _ => "https://example.invalid"
                            } }
                        } }], "auto") : Column([]),
                        Column([new { type = "Image", url = ToggleImage(enabled), width = "34px", height = "20px", altText = enabled ? "On" : "Off" }], "auto")
                    }
                });
            }
            body.Add(new {
                type = "Container", height = "stretch", verticalContentAlignment = "bottom", spacing = "Medium",
                items = new[] { new {
                    type = "ColumnSet", columns = new[] {
                        Column([new { type = "ActionSet", actions = new[] { Action("Back", "status") } }], "auto"),
                        Column([])
                    }
                } }
            });
        }
        else
        {
            foreach (var service in Catalog.Services.Where(s => prefs.Enabled.Contains(s.Id)))
            {
                var usage = data.Services.FirstOrDefault(x => x.Id == service.Id);
                var items = new List<object> {
                    new { type = "TextBlock", text = $"{service.Name}  ·  {Labels.Plan(service, usage)}", weight = "Bolder", size = "Small", spacing = "None", wrap = true }
                };
                if (service.IsApi) items.Add(Text($"This month {Labels.Money(usage?.MonthCost)} · Today {Labels.Money(usage?.DayCost)}", true));
                else
                {
                    if (usage?.Status is not null) items.Add(Text(usage.Status, true));
                    else {
                        var windows = Labels.Windows(service, usage);
                        foreach (var window in windows) items.Add(Text($"{window.Label} {Labels.Percent(window.Percent)} · {Labels.Reset(window.Reset, now)}", true));
                        for (var i = 0; i < windows.Length; i++) items.Add(Bar(windows[i], i));
                    }
                }
                if (body.Count > 0) body.Add(new {
                    type = "Image", url = CardImages.Separator, size = "Stretch", height = "1px", spacing = "Small", altText = ""
                });
                body.Add(new { type = "ColumnSet", spacing = "Small", columns = new[] {
                    Column([new { type = "Image", url = CardImages.Dot(service.Color), width = "24px", height = "24px", altText = service.Name }], "auto"),
                    Column(items.ToArray())
                } });
            }
            if (prefs.Enabled.Count == 0) body.Add(Text("No AI services to display. Turn on services in Settings.", true));
        }
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["type"] = "AdaptiveCard", ["version"] = "1.5", ["body"] = body });
    }
    // PNG data URIs are supplied by the packaged app; no external requests for toggle assets.
    public static Func<bool, string> ToggleImage { get; set; } = enabled => enabled ? "" : "";
}
