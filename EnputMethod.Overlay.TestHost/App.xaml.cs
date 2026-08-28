using System.Diagnostics;
using System.IO;
using System.Windows;

namespace EnputMethod.Overlay.TestHost;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string executable = Path.Combine(AppContext.BaseDirectory, "Overlay", "EnputMethod.Overlay.exe");
        if (!File.Exists(executable)) return;
        Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable),
        });
    }
}
