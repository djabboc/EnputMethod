using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace EnputMethod.Uninstaller;

public partial class MainWindow : Window
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int UninstallInputMethodDelegate();

    public MainWindow() => InitializeComponent();

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        string message;
        try
        {
            int hr = InvokeNativeUninstaller();
            if (hr >= 0) RemoveOverlay();
            message = hr >= 0 ? "卸载完成。" : $"卸载失败 (0x{hr:X8})。";
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            message = "卸载程序文件不完整、版本不匹配，或 Overlay 文件仍被占用。请关闭 Enput Method 后重试。";
        }

        MessageBox.Show(message, "Enput Method");
        Close();
    }

    private static int InvokeNativeUninstaller()
    {
        string dllPath = Path.Combine(AppContext.BaseDirectory, "EnputMethod.Tsf.dll");
        IntPtr module = NativeLibrary.Load(dllPath);
        IntPtr procedure = NativeLibrary.GetExport(module, "UninstallEnglishInputMethod");
        return Marshal.GetDelegateForFunctionPointer<UninstallInputMethodDelegate>(procedure)();
    }

    private static void RemoveOverlay()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Enput Method", "Overlay");
        string executable = Path.GetFullPath(Path.Combine(directory, "EnputMethod.Overlay.exe"));
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
            catch (InvalidOperationException)
            {
                // It already exited.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The uninstaller will leave files in place if Windows denies process inspection.
            }
            finally
            {
                process.Dispose();
            }
        }
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
