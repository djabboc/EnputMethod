using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace EnputMethod.Setup;
public partial class MainWindow : Window
{
    [DllImport("EnputMethod.Tsf.v2.dll", ExactSpelling = true)] private static extern int InstallEnglishInputMethod();
    [DllImport("EnputMethod.Tsf.v2.dll", ExactSpelling = true)] private static extern int UninstallEnglishInputMethod();
    public MainWindow() { InitializeComponent(); StatusText.Text = "此工具会以管理员权限注册 Enput Method - English。它是透明英文输入法，按键将直接输入到当前应用。"; }
    private void Install_Click(object sender, RoutedEventArgs e) => RunAction(InstallEnglishInputMethod, "已安装。现在可以按 Win + Space，在输入法列表中选择 Enput Method - English。", "安装失败");
    private void Uninstall_Click(object sender, RoutedEventArgs e) => RunAction(UninstallEnglishInputMethod, "已卸载 Enput Method 输入法配置。", "卸载失败");
    private void Settings_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("ms-settings:typing") { UseShellExecute = true });
    private void RunAction(Func<int> action, string success, string failure)
    {
        try { int hr = action(); StatusText.Text = hr >= 0 ? success : $"{failure} (0x{hr:X8})。请确认 DLL 已与此程序放在同一文件夹。"; }
        catch (DllNotFoundException) { StatusText.Text = "未找到 EnputMethod.Tsf.dll。请先生成整个解决方案，再从 bin 文件夹启动本程序。"; }
        catch (Win32Exception ex) { StatusText.Text = $"{failure}: {ex.Message}"; }
    }
}
