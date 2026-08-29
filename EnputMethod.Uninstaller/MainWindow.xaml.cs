using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace EnputMethod.Uninstaller;

public partial class MainWindow : Window
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UninstallInputMethodDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetRegistrationStageDelegate();

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
        WriteUninstallLog(message);
        progress.Report(new UninstallProgressUpdate(succeeded ? 100 : 0, succeeded ? "卸载完成。" : "卸载失败。"));
        MessageBox.Show(message, "Enput Method");
        if (succeeded) Close();
        else UninstallButton.IsEnabled = true;
    }

    private static (bool Succeeded, string Message) UninstallAndRemove(IProgress<UninstallProgressUpdate>? progress)
    {
        string phase = "注销 Windows 输入法服务";
        try
        {
            progress?.Report(new UninstallProgressUpdate(10, "正在注销 Windows 输入法服务..."));
            (int hr, int nativeStage) = InvokeNativeUninstaller();
            if (hr < 0) return (false, $"卸载失败 (0x{hr:X8})，阶段：{NativeStageText(nativeStage)}。\n未删除已安装文件。");

            phase = "停止候选窗口";
            progress?.Report(new UninstallProgressUpdate(45, "正在停止候选窗口..."));
            StopInstalledOverlay();

            phase = "清理 Program Files 中的静态资源";
            progress?.Report(new UninstallProgressUpdate(70, "正在清理 Program Files 中的静态资源..."));
            RemoveInstalledProduct();

            phase = "验证卸载结果";
            progress?.Report(new UninstallProgressUpdate(95, "正在验证卸载结果..."));
            return Directory.Exists(ProductLayout.InstallDirectory)
                ? (false, $"输入法已注销，但 Program Files 中仍有文件被占用。\n请关闭下列进程后重新运行卸载器，或重启 Windows：\n{LoadedProcessSummary()}")
                : (true, "卸载完成。用户配置和学习数据已保留；现在可以删除解压后的发布目录。");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            return (false, $"卸载在“{phase}”阶段失败。\n{ex.Message}\n\n仍加载 Enput 的进程：\n{LoadedProcessSummary()}");
        }
    }

    private static (int HResult, int Stage) InvokeNativeUninstaller()
    {
        string dllPath = Path.Combine(ProductLayout.PayloadDirectory, "EnputMethod.Tsf.dll");
        IntPtr module = NativeLibrary.Load(dllPath);
        IntPtr procedure = NativeLibrary.GetExport(module, "UninstallEnglishInputMethod");
        int result = Marshal.GetDelegateForFunctionPointer<UninstallInputMethodDelegate>(procedure)();
        int stage = 0;
        try
        {
            IntPtr stageProcedure = NativeLibrary.GetExport(module, "GetEnputRegistrationStage");
            stage = Marshal.GetDelegateForFunctionPointer<GetRegistrationStageDelegate>(stageProcedure)();
        }
        catch (EntryPointNotFoundException) { }
        return (result, stage);
    }

    private static string NativeStageText(int stage) => stage switch
    {
        40 => "移除 Enput TSF Profile",
        41 => "移除 Enput TSF 分类",
        42 => "移除 Enput COM 注册",
        _ => "未知原生注销阶段",
    };

    private static string LoadedProcessSummary()
    {
        List<string> holders = [];
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                bool loaded = process.Modules.Cast<ProcessModule>().Any(module => module.FileName.StartsWith(ProductLayout.InstallDirectory, StringComparison.OrdinalIgnoreCase));
                if (loaded) holders.Add($"{process.ProcessName} ({process.Id})");
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        return holders.Count == 0 ? "未检测到；请重启 Windows 后再运行卸载器。" : string.Join("、", holders);
    }

    private static void WriteUninstallLog(string message)
    {
        Directory.CreateDirectory(ProductLayout.UserDataDirectory);
        File.WriteAllText(Path.Combine(ProductLayout.UserDataDirectory, "uninstall-verification.log"), message);
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