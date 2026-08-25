using System.Linq;
using System.Runtime.InteropServices;

namespace EnputMethod.Setup;
public partial class App : System.Windows.Application
{
    [DllImport("EnputMethod.Tsf.v2.dll", ExactSpelling = true)]
    private static extern int InstallEnglishInputMethod();

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (e.Args.Contains("--install", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = InstallEnglishInputMethod();
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }
}
