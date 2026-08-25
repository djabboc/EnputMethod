using System.Runtime.InteropServices;
namespace EnputMethod.Uninstaller;
public partial class App : System.Windows.Application { [DllImport("EnputMethod.Tsf.dll", ExactSpelling = true)] private static extern int UninstallEnglishInputMethod(); protected override void OnStartup(System.Windows.StartupEventArgs e) { Environment.ExitCode = UninstallEnglishInputMethod(); Shutdown(); } }
