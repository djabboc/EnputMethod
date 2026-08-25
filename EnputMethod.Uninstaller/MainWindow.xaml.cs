using System.Runtime.InteropServices;
using System.Windows;

namespace EnputMethod.Uninstaller;

public partial class MainWindow : Window
{
    [DllImport("EnputMethod.Tsf.dll", ExactSpelling = true)]
    private static extern int UninstallEnglishInputMethod();

    public MainWindow() => InitializeComponent();

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        string message;
        try
        {
            int hr = UninstallEnglishInputMethod();
            message = hr >= 0 ? "卸载完成。" : $"卸载失败 (0x{hr:X8})。";
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            message = "卸载程序文件不完整或版本不匹配。请将整个卸载程序文件夹中的文件放在一起后重试。";
        }

        MessageBox.Show(message, "Enput Method");
        Close();
    }
}
