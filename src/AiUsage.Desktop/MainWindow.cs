using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AiUsage.Desktop;
public sealed class MainWindow : Window
{
    readonly bool sample;
    readonly SubscriptionFeed feed = new();
    readonly CancellationTokenSource lifetime = new();
    bool loginPending;
    string? loginMessage;
    readonly Preferences prefs;
    readonly UsageSnapshot demo = Catalog.Sample(DateTimeOffset.Now);
    readonly StackPanel content = new();
    readonly Button status = new() { Content = "Status" };
    readonly Button settings = new() { Content = "Setting" };
    readonly TextBlock footer = new() { FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new(12, 10, 12, 12), TextWrapping = TextWrapping.Wrap };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(15) };
    public MainWindow(bool sample)
    {
        this.sample = sample;
        prefs = sample ? new() : LocalStore.ReadPreferences();
        prefs.Settings = false;
        Title = sample ? "Agent Usage · 샘플 미리보기" : "Agent Usage";
        Width = 382; Height = 626; MinWidth = 330; MinHeight = 350;
        FontFamily = new("Segoe UI"); FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Background"); SetResourceReference(ForegroundProperty, "Text");
        var root = new DockPanel();
        var tabs = new Grid { Margin = new(2) };
        tabs.ColumnDefinitions.Add(new()); tabs.ColumnDefinitions.Add(new());
        status.Margin = new(5); settings.Margin = new(5); Grid.SetColumn(settings, 1);
        tabs.Children.Add(status); tabs.Children.Add(settings);
        var nav = new Border { Child = tabs, CornerRadius = new(12), Margin = new(16, 14, 16, 10) };
        nav.SetResourceReference(Border.BackgroundProperty, "Nav");
        DockPanel.SetDock(nav, Dock.Top); root.Children.Add(nav);
        DockPanel.SetDock(footer, Dock.Bottom); footer.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = root;
        status.Click += (_, _) => { prefs.Settings = false; Refresh(); };
        settings.Click += (_, _) => { prefs.Settings = true; Refresh(); };
        SystemEvents.UserPreferenceChanged += ThemeChanged;
        Closed += (_, _) => { lifetime.Cancel(); timer.Stop(); SystemEvents.UserPreferenceChanged -= ThemeChanged; };
        Loaded += async (_, _) => await FetchUsageAsync();
        timer.Tick += async (_, _) => { if (!prefs.Settings) Refresh(); await FetchUsageAsync(); };
        timer.Start(); ApplyTheme(); Refresh();
    }
    async Task FetchUsageAsync(bool force = false)
    {
        if (sample || loginPending) return;
        try { await feed.RefreshAsync(prefs.Enabled.ToHashSet(), force, lifetime.Token); if (!lifetime.IsCancellationRequested) Refresh(); }
        catch (OperationCanceledException) { }
    }
    public async Task ConnectSubscriptionAsync(string id)
    {
        if (sample || loginPending) return;
        loginPending = true; loginMessage = $"열린 브라우저에서 {SubscriptionLogin.Name(id)} 로그인을 완료해주세요."; Refresh();
        try { await SubscriptionLogin.ConnectAsync(id, lifetime.Token); loginMessage = null; }
        catch (OperationCanceledException) { loginMessage = "로그인이 취소되었거나 시간이 초과되었습니다."; }
        catch (Exception e) when (e is UsageConnectionException or CodexException or IOException or JsonException or System.ComponentModel.Win32Exception) { loginMessage = e is UsageConnectionException or CodexException ? e.Message : "로그인을 실행하지 못했습니다."; }
        finally { loginPending = false; }
        if (!lifetime.IsCancellationRequested) { await FetchUsageAsync(true); Refresh(); }
    }
    void ThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => { ApplyTheme(); Refresh(); });
    void ApplyTheme(bool? forceDark = null)
    {
        bool dark = forceDark ?? (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0);
        foreach (var (key, light, night) in new[] { ("Background", "#FFFFFF", "#303030"), ("Text", "#20252B", "#F4F4F4"), ("Muted", "#707985", "#B3B6BD"), ("Track", "#EEEEEF", "#454545"), ("Selected", "#FFFFFF", "#4A4A4A"), ("Nav", "#F0F0F1", "#383838"), ("Line", "#F0F0F0", "#404040") })
            Resources[key] = Brush(dark ? night : light);
        if (SystemParameters.HighContrast && forceDark is null) {
            Resources["Background"] = SystemColors.WindowBrush; Resources["Text"] = SystemColors.WindowTextBrush;
            Resources["Muted"] = SystemColors.WindowTextBrush; Resources["Selected"] = SystemColors.HighlightBrush;
        }
    }
    static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    TextBlock Text(string value, bool muted = false, double size = 12)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new(0, 1, 0, 1) };
        text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Text"); return text;
    }
    public void Refresh()
    {
        status.SetResourceReference(Button.BackgroundProperty, prefs.Settings ? "Nav" : "Selected");
        settings.SetResourceReference(Button.BackgroundProperty, prefs.Settings ? "Selected" : "Nav");
        content.Children.Clear();
        var data = sample ? demo : feed.Read(prefs.Enabled);
        footer.Text = prefs.Settings ? loginMessage ?? "" : "";
        footer.Visibility = prefs.Settings && loginMessage is not null ? Visibility.Visible : Visibility.Collapsed;
        if (prefs.Settings) {
            content.Children.Add(new TextBlock { Text = "SERVICES", FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new(20, 12, 20, 12) });
            foreach (var service in Catalog.Services) {
                var row = new DockPanel { Margin = new(23, 6, 25, 6) };
                var toggle = new ToggleButton { IsChecked = prefs.Enabled.Contains(service.Id) };
                AutomationProperties.SetName(toggle, service.Name + " 표시");
                toggle.Click += (_, _) => {
                    prefs.Toggle(service.Id);
                    if (!sample) try { LocalStore.SavePreferences(prefs); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
                        prefs.Toggle(service.Id); toggle.IsChecked = prefs.Enabled.Contains(service.Id); footer.Text = "설정을 저장하지 못했습니다. 다시 시도해주세요.";
                    }
                };
                DockPanel.SetDock(toggle, Dock.Right); row.Children.Add(toggle); row.Children.Add(Text(service.Name, size: 13)); content.Children.Add(row);
            }
            if (!sample) {
                var connections = new UniformGrid { Columns = 2, Margin = new(14, 8, 14, 0) };
                foreach (var id in new[] { "chatgpt", "claude", "gemini" }) {
                    var connect = new Button { Content = $"{SubscriptionLogin.Name(id)} 연결", Margin = new(6, 4, 6, 4), IsEnabled = !loginPending };
                    connect.SetResourceReference(Button.BackgroundProperty, "Nav");
                    connect.Click += async (_, _) => await ConnectSubscriptionAsync(id);
                    connections.Children.Add(connect);
                }
                content.Children.Add(connections);
            }
            return;
        }
        foreach (var service in Catalog.Services.Where(s => prefs.Enabled.Contains(s.Id))) {
            var usage = data.Services.FirstOrDefault(x => x.Id == service.Id);
            var grid = new Grid { Margin = new(18, 10, 20, 10) };
            grid.ColumnDefinitions.Add(new() { Width = new(43) }); grid.ColumnDefinitions.Add(new());
            var iconColor = (Color)ColorConverter.ConvertFromString(service.Color);
            var icon = new Border { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(9), Background = new SolidColorBrush(Color.FromArgb(30, iconColor.R, iconColor.G, iconColor.B)), Child = new Ellipse { Width = 9, Height = 9, Fill = Brush(service.Color) } };
            grid.Children.Add(icon);
            var stack = new StackPanel(); Grid.SetColumn(stack, 1); grid.Children.Add(stack);
            var title = new WrapPanel();
            var name = Text(service.Name, size: 14); name.FontWeight = FontWeights.SemiBold; name.Margin = new(0, 0, 7, 4); title.Children.Add(name);
            var badge = new Border { Background = new SolidColorBrush(Color.FromArgb(28, iconColor.R, iconColor.G, iconColor.B)), CornerRadius = new(4), Padding = new(6, 2, 6, 2), Margin = new(0, 0, 0, 4) };
            var label = Text(service.IsApi ? "API" : usage?.Plan ?? "미연결", size: 10); label.FontWeight = FontWeights.SemiBold; badge.Child = label; title.Children.Add(badge); stack.Children.Add(title);
            if (service.IsApi) stack.Children.Add(Text($"이번 달 {Labels.Money(usage?.MonthCost)} · 오늘 {Labels.Money(usage?.DayCost)}", true));
            else {
                if (usage?.Status is not null) stack.Children.Add(Text(usage.Status, true));
                else {
                    var windows = Labels.Windows(service, usage);
                    foreach (var window in windows) stack.Children.Add(Text($"{window.Label} {Labels.Percent(window.Percent)} · {Labels.Reset(window.Reset, DateTimeOffset.Now)}", true));
                    for (var i = 0; i < windows.Length; i++) stack.Children.Add(Progress(windows[i].Percent, i == 0 ? "#4BA3EF" : "#70BB7B"));
                }
            }
            var border = new Border { Child = grid, BorderThickness = new(0, 0, 0, 1) }; border.SetResourceReference(Border.BorderBrushProperty, "Line"); content.Children.Add(border);
        }
        if (prefs.Enabled.Count == 0) { var empty = Text("표시할 AI가 없습니다.\nSetting에서 서비스를 켜주세요.", true); empty.Margin = new(24, 40, 24, 40); content.Children.Add(empty); }
    }
    FrameworkElement Progress(double? value, string color)
    {
        var fraction = Labels.RemainingPercent(value) ?? 0;
        var grid = new Grid { Height = 4 };
        grid.ColumnDefinitions.Add(new() { Width = new(fraction, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new(100 - fraction, GridUnitType.Star) });
        grid.Children.Add(new Border { Background = Brush(color), CornerRadius = new(2) });
        var track = new Border { CornerRadius = new(2), Child = grid, Margin = new(0, 4, 0, 0) }; track.SetResourceReference(Border.BackgroundProperty, "Track"); return track;
    }
    public void RenderPreviews(string directory)
    {
        timer.Stop();
        foreach (var dark in new[] { false, true }) foreach (var setting in new[] { false, true }) {
            prefs.Settings = setting; ApplyTheme(dark); Refresh();
            var element = (FrameworkElement)Content;
            element.Width = 366; element.Height = setting ? (sample ? 350 : 520) : 626;
            var surface = new Border { Background = (Brush)Resources["Background"] };
            Content = null; surface.Child = element; Content = surface;
            surface.Measure(new(366, element.Height)); surface.Arrange(new Rect(0, 0, 366, element.Height)); surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(366, (int)element.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(System.IO.Path.Combine(directory, $"{(setting ? "setting" : "status")}-{(dark ? "dark" : "light")}.png"))) encoder.Save(stream);
            Content = null; surface.Child = null; Content = element;
        }
        SystemEvents.UserPreferenceChanged -= ThemeChanged;
    }
}
