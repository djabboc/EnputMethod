namespace EnputMethod.Installer;

public partial class App : System.Windows.Application
{
    private const string InstallVerificationLogFileName = "install-verification.log";

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--install-and-verify", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                InstallerVerification result = global::EnputMethod.Installer.MainWindow.InstallAndVerify();
                WriteInstallVerificationLog(result.Message);
                Environment.ExitCode = result.Succeeded ? 0 : 1;
            }
            catch (Exception ex)
            {
                WriteInstallVerificationLog(ex.ToString());
                Environment.ExitCode = 1;
            }
            finally
            {
                Shutdown();
            }
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void WriteInstallVerificationLog(string message)
    {
        string directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Enput Method");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, InstallVerificationLogFileName), message);
    }
}
