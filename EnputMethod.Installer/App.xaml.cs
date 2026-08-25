using System.Runtime.InteropServices;
namespace EnputMethod.Installer;
public partial class App : System.Windows.Application { [DllImport("EnputMethod.Tsf.dll", ExactSpelling = true)] private static extern int InstallEnglishInputMethod(); protected override void OnStartup(System.Windows.StartupEventArgs e) { Environment.ExitCode = InstallEnglishInputMethod(); Shutdown(); } }
