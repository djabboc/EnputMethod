using System.IO;

namespace EnputMethod.Uninstaller;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--uninstall-and-verify", StringComparer.OrdinalIgnoreCase))
        {
            (bool succeeded, string message) = global::EnputMethod.Uninstaller.MainWindow.UnregisterInputMethodOnStaWorker().GetAwaiter().GetResult();
            Directory.CreateDirectory(ProductLayout.UserDataDirectory);
            File.WriteAllText(Path.Combine(ProductLayout.UserDataDirectory, "uninstall-verification.log"), message);
            Shutdown(succeeded ? 0 : 1);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}