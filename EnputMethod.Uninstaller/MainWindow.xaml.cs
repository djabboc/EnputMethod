using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace EnputMethod.Uninstaller;

public partial class MainWindow : Window
{
    private const string TextServiceClsid = "{9C8945D5-01DF-48F4-A8DB-57E8B6A1EB10}";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UninstallInputMethodDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetRegistrationStageDelegate();

    internal sealed record UninstallProgressUpdate(int Percentage, string Stage);

    public MainWindow() => InitializeComponent();

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        UninstallButton.IsEnabled = false;
        IProgress<UninstallProgressUpdate> progress = new Progress<UninstallProgressUpdate>(value =>
        {
            StatusText.Text = value.Stage;
            UninstallProgress.Value = value.Percentage;
        });
        (bool succeeded, string message) = await UnregisterInputMethodOnStaWorker(progress);
        WriteUninstallLog(message);
        progress.Report(new UninstallProgressUpdate(succeeded ? 100 : 0, succeeded ? "卸载完成。" : "卸载失败。"));
        MessageBox.Show(message, "Enput Method");
        if (succeeded) Close();
        else UninstallButton.IsEnabled = true;
    }

    internal static Task<(bool Succeeded, string Message)> UnregisterInputMethodOnStaWorker(IProgress<UninstallProgressUpdate>? progress = null)
        => RunOnStaAsync(() => UnregisterInputMethod(progress));
    private static Task<T> RunOnStaAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    internal static (bool Succeeded, string Message) UnregisterInputMethod(IProgress<UninstallProgressUpdate>? progress = null)
    {
        const string phase = "注销 Windows 输入法服务";
        try
        {
            progress?.Report(new UninstallProgressUpdate(20, "正在从 Windows 输入法列表中移除 Enput Method..."));
            (int hr, int nativeStage) = InvokeNativeUninstaller();
            if (hr < 0) return (false, $"卸载失败 (0x{hr:X8})，阶段：{NativeStageText(nativeStage)}。\n没有修改已安装的 DLL 或资源文件。");

            string? remainingRegistration = FindRemainingRegistration();
            if (remainingRegistration is not null)
            {
                return (false, $"卸载未完成：Enput 的注册仍存在于 {remainingRegistration}。\n请重试卸载；已保留 Program Files 中的 DLL、Overlay、静态资源和用户数据。");
            }

            progress?.Report(new UninstallProgressUpdate(80, "正在保留运行文件以兼容已打开的应用..."));
            return (true, "Enput Method 已从 Windows 输入法列表中移除。\n已保留 Program Files 中的 DLL、Overlay 和静态资源，以保证已打开应用不会缺少依赖；重新安装可立即复用这些文件。\n用户配置和学习数据也已保留。现在可以删除解压后的发布目录。");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or UnauthorizedAccessException)
        {
            return (false, $"卸载在“{phase}”阶段失败。\n{ex.Message}\n\n没有修改已安装的 DLL 或资源文件。");
        }
    }

    private static string? FindRemainingRegistration()
    {
        string[] paths =
        [
            $@"SOFTWARE\Classes\CLSID\{TextServiceClsid}",
            $@"SOFTWARE\Microsoft\CTF\TIP\{TextServiceClsid}",
        ];
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            foreach (string path in paths)
            {
                using RegistryKey? key = root.OpenSubKey(path);
                if (key is not null) return $"{hive}\\{path}";
            }
        }
        return null;
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
        30 => "初始化 TSF 注销所需的 STA COM",
        40 => "移除 Enput TSF Profile",
        41 => "移除 Enput TSF 分类",
        42 => "移除 Enput COM 注册",
        _ => "未知原生注销阶段",
    };

    private static void WriteUninstallLog(string message)
    {
        Directory.CreateDirectory(ProductLayout.UserDataDirectory);
        File.WriteAllText(Path.Combine(ProductLayout.UserDataDirectory, "uninstall-verification.log"), message);
    }
}
