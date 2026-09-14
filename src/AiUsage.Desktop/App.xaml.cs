using System.IO;
using System.Windows;

namespace AiUsage.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var sample = e.Args.Contains("--sample");
        var window = new MainWindow(sample);
        if (e.Args.Contains("--render")) {
            var index = Array.IndexOf(e.Args, "--render");
            var directory = Path.GetFullPath(e.Args[index + 1]);
            Directory.CreateDirectory(directory);
            window.RenderPreviews(directory);
            Shutdown();
        } else window.Show();
    }
}
