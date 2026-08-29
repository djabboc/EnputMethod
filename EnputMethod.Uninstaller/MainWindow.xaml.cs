using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace EnputMethod.Uninstaller;

public partial class MainWindow : Window
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UninstallInputMethodDelegate();

    private sealed record UninstallProgressUpdate(int Percentage, string Stage);

    public MainWindow() => InitializeComponent();

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        UninstallButton.IsEnabled = false;
        IProgress<UninstallProgressUpdate> progress = new Progress<UninstallProgressUpdate>(value =>
        {
            StatusText.Text = value.Stage;
            UninstallProgress.Value = value.Percentage;
        });
        (bool succeeded, string message) = await Task.Run(() => UninstallAndRemove(progress));
        progress.Report(new UninstallProgressUpdate(succeeded ? 100 : 0, succeeded ? "卸载完成。" : "卸载失败。"));
        MessageBox.Show(message, "Enput Method");
        if (succeeded) Close();
        else UninstallButton.IsEnabled = true;
    }

    private static (bool Succeeded, string Message) UninstallAndRemove(IProgress<UninstallProgressUpdate>? progress)
    {
        try
        {
            progress?.Report(new UninstallProgressUpdate(10, "正在注销 Windows 输入法服务..."));
            int hr = InvokeNativeUninstaller();
            if (hr < 0) return (false, $"卸载失败 (0x{hr:X8})。\n未删除已安装文件。");
            progress?.Report(new UninstallProgressUpdate(45, "正在停止候选窗口..."));
            StopInstalledOverlay();
            progress?.Report(new UninstallProgressUpdate(70, "正在清理 Program Files 中的静态资源..."));
            RemoveInstalledProduct();
            progress?.Report(new UninstallProgressUpdate(95, "正在验证卸载结果..."));
            return Directory.Exists(ProductLayout.InstallDirectory)
                ? (false, "输入法已注销，但部分已安装文件仍被占用。请关闭所有使用 Enput 的应用后重试。")
                : (true, "卸载完成。用户配置和学习数据已保留；现在可以删除解压后的发布目录。");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            return (false, $"卸载程序文件不完整、版本不匹配，或已安装文件仍被占用。\n{ex.Message}");
        }
    }

    private static int InvokeNativeUninstaller()
    {
        string dllPath = Path.Combine(ProductLayout.PayloadDirectory, "EnputMethod.Tsf.dll");
        IntPtr module = NativeLibrary.Load(dllPath);
        IntPtr procedure = NativeLibrary.GetExport(module, "UninstallEnglishInputMethod");
        return Marshal.GetDelegateForFunctionPointer<UninstallInputMethodDelegate>(procedure)();
    }

    private static void StopInstalledOverlay()
    {
        string executable = Path.GetFullPath(Path.Combine(ProductLayout.InstallDirectory, "Overlay", "EnputMethod.Overlay.exe"));
        foreach (Process process in Process.GetProcessesByName("EnputMethod.Overlay"))
        {
            try
            {
                if (!string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), executable, StringComparison.OrdinalIgnoreCase)) continue;
                _ = process.CloseMainWindow();
                if (!process.WaitForExit(2000))
                {
                    process.Kill(true);
                    _ = process.WaitForExit(2000);
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
    }

    private static void RemoveInstalledProduct()
    {
        if (Directory.Exists(ProductLayout.InstallDirectory)) Directory.Delete(ProductLayout.InstallDirectory, true);
    }
}