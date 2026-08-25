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
            message = hr >= 0 ? "卸载完成。" : $"卸载失败 (0x{hr:X8})。";
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            message = "卸载程序文件不完整或版本不匹配。请将整个卸载程序文件夹中的文件放在一起后重试。";
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
}
