using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace EnputMethod.Installer;

public partial class MainWindow : Window
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int InstallInputMethodDelegate();

    public MainWindow() => InitializeComponent();

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        string message;
        try
        {
            int hr = InvokeNativeInstaller();
            if (hr >= 0)
            {
                EnsureUserConfiguration();
            }
            message = hr >= 0
                ? "安装完成。请切换到其他输入法后再切回 Enput Method。"
                : $"安装失败 (0x{hr:X8})。";
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            message = "安装程序文件不完整或版本不匹配。请将整个安装程序文件夹中的文件放在一起后重试。";
        }

        MessageBox.Show(message, "Enput Method");
        Close();
    }

    private static int InvokeNativeInstaller()
    {
        string dllPath = Path.Combine(AppContext.BaseDirectory, "EnputMethod.Tsf.dll");
        IntPtr module = NativeLibrary.Load(dllPath);
        IntPtr procedure = NativeLibrary.GetExport(module, "InstallEnglishInputMethod");
        return Marshal.GetDelegateForFunctionPointer<InstallInputMethodDelegate>(procedure)();
    }

    private static void EnsureUserConfiguration()
    {
        string destinationDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Enput Method");
        Directory.CreateDirectory(destinationDirectory);
        CopyDefaultFile("conf.json", destinationDirectory);
        CopyDefaultFile("dictionary.txt", destinationDirectory);
    }

    private static void CopyDefaultFile(string fileName, string destinationDirectory)
    {
        string destination = Path.Combine(destinationDirectory, fileName);
        if (!File.Exists(destination))
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, fileName), destination);
        }
    }
}
