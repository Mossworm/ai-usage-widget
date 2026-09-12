using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.Widgets.Providers;

namespace AiUsage.Provider;

[ComVisible(true), ComDefaultInterface(typeof(IWidgetProvider)), Guid(Program.ClassId)]
public sealed class WidgetProvider : IWidgetProvider, IWidgetProvider2
{
    public static readonly ManualResetEvent Exit = new(false);
    readonly object gate = new();
    readonly Dictionary<string, Preferences> widgets = new();
    readonly HashSet<string> active = new();
    readonly Timer timer;
    readonly SubscriptionFeed feed = new();
    readonly CancellationTokenSource lifetime = new();
    public WidgetProvider()
    {
        var on = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "toggle-on.png")));
        var off = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "toggle-off.png")));
        Card.ToggleImage = enabled => enabled ? on : off;
        foreach (var info in WidgetManager.GetDefault().GetWidgetInfos() ?? []) {
            Preferences prefs;
            try { prefs = JsonSerializer.Deserialize<Preferences>(info.CustomState, LocalStore.Json) ?? new(); }
            catch (JsonException) { prefs = new(); }
            if (prefs.Enabled is null) prefs = new();
            widgets[info.WidgetContext.Id] = prefs;
        }
        timer = new(_ => Tick(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }
    void Tick()
    {
        lock (gate) foreach (var id in active.ToArray()) SafeUpdate(id);
        _ = FetchUsageAsync();
    }
    async Task FetchUsageAsync()
    {
        HashSet<string> enabled;
        lock (gate) enabled = active.Where(widgets.ContainsKey).SelectMany(id => widgets[id].Enabled).ToHashSet();
        if (enabled.Count == 0) return;
        try {
            await feed.RefreshAsync(enabled, cancellation: lifetime.Token);
            lock (gate) foreach (var id in active.ToArray()) SafeUpdate(id);
        } catch (OperationCanceledException) { }
    }
    void SafeUpdate(string id)
    {
        try { Update(id); }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(e); }
    }
    void Update(string id)
    {
        if (!widgets.TryGetValue(id, out var prefs)) return;
        WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(id) {
            Template = Card.Render(prefs, feed.Read(prefs.Enabled), DateTimeOffset.Now),
            Data = "{}", CustomState = JsonSerializer.Serialize(prefs, LocalStore.Json)
        });
    }
    public void CreateWidget(WidgetContext context)
    {
        lock (gate) { widgets[context.Id] = new(); active.Add(context.Id); Update(context.Id); }
        _ = FetchUsageAsync();
    }
    public void DeleteWidget(string widgetId, string customState)
    {
        lock (gate) {
            widgets.Remove(widgetId); active.Remove(widgetId);
            if (widgets.Count == 0) { lifetime.Cancel(); timer.Dispose(); Exit.Set(); }
        }
    }
    public void Activate(WidgetContext context)
    {
        lock (gate) { widgets.TryAdd(context.Id, new()); active.Add(context.Id); Update(context.Id); }
        _ = FetchUsageAsync();
    }
    public void Deactivate(string widgetId) { lock (gate) active.Remove(widgetId); }
    public void OnWidgetContextChanged(WidgetContextChangedArgs args) { lock (gate) Update(args.WidgetContext.Id); }
    public void OnCustomizationRequested(WidgetCustomizationRequestedArgs args)
    {
        lock (gate) {
            var id = args.WidgetContext.Id;
            if (!widgets.TryGetValue(id, out var prefs)) {
                // Customization may be the first callback after provider activation.
                try { prefs = JsonSerializer.Deserialize<Preferences>(args.CustomState, LocalStore.Json) ?? new(); }
                catch (JsonException) { prefs = new(); }
                if (prefs.Enabled is null) prefs = new();
                widgets[id] = prefs;
            }
            prefs.Settings = true;
            Update(id);
        }
    }
    public void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        lock (gate) {
            var id = args.WidgetContext.Id;
            if (!widgets.TryGetValue(id, out var prefs)) return;
            if (args.Verb.StartsWith("toggle:", StringComparison.Ordinal)) prefs.Toggle(args.Verb[7..]);
            Update(id);
        }
    }
}
