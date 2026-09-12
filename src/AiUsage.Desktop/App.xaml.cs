using System.Globalization;
using System.IO;
using System.Windows;

namespace AiUsage.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var sample = e.Args.Contains("--sample");
        // Packaged preview images are the language-neutral English fallback.
        // The live app and widget still use the user's Windows UI culture.
        if (e.Args.Contains("--render")) CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var window = new MainWindow(sample);
        if (e.Args.Contains("--render")) {
            var index = Array.IndexOf(e.Args, "--render");
            var directory = Path.GetFullPath(e.Args[index + 1]);
            Directory.CreateDirectory(directory);
            window.RenderPreviews(directory);
            Shutdown();
        } else {
            var login = e.Args.Select(SubscriptionLogin.FromArgument).FirstOrDefault(id => id is not null);
            if (login is not null) window.Loaded += async (_, _) => await window.ConnectSubscriptionAsync(login);
            window.Show();
        }
    }
}
