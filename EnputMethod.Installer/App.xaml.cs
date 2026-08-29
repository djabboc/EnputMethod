namespace EnputMethod.Installer;

public partial class App : System.Windows.Application
{
    private const string InstallVerificationLogFileName = "install-verification.log";

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        int seedArgument = Array.FindIndex(e.Args, argument => string.Equals(argument, "--build-lexicon-seed", StringComparison.OrdinalIgnoreCase));
        if (seedArgument >= 0 && seedArgument + 1 < e.Args.Length)
        {
            try
            {
                string output = System.IO.Path.GetFullPath(e.Args[seedArgument + 1]);
                string staging = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output)!, ".enput-seed-build");
                System.IO.Directory.CreateDirectory(staging);
                LexiconDatabaseBuilder.CreateOrMigrate(staging, AppContext.BaseDirectory);
                System.IO.File.Copy(System.IO.Path.Combine(staging, "enput.db"), output, true);
                Environment.ExitCode = 0;
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
        if (e.Args.Contains("--migrate-lexicon", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string directory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Enput Method");
                LexiconDatabaseBuilder.CreateOrMigrate(directory, AppContext.BaseDirectory);
                WriteInstallVerificationLog("SQLite lexicon migration completed.");
                Environment.ExitCode = 0;
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
        if (e.Args.Contains("--verify-lexicon", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string directory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Enput Method");
                LexiconDatabaseBuilder.VerifyInstalledDatabase(directory);
                WriteInstallVerificationLog("SQLite lexicon verification passed.");
                Environment.ExitCode = 0;
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
