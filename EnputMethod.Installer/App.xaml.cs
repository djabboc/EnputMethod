namespace EnputMethod.Installer;

public partial class App : System.Windows.Application
{
    private const string InstallVerificationLogFileName = "install-verification.log";
    private const string LexiconVerificationLogFileName = "lexicon-verification.log";
    private const string PackageVerificationLogFileName = "package-verification.log";

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
                LexiconDatabaseBuilder.CreateOrMigrate(staging, ProductLayout.PackageResourceDirectory);
                System.IO.File.Copy(System.IO.Path.Combine(staging, "enput.db"), output, true);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                WriteVerificationLog(InstallVerificationLogFileName, ex.ToString());
                Environment.ExitCode = 1;
            }
            finally { Current.Shutdown(Environment.ExitCode); }
            return;
        }

        if (e.Args.Contains("--migrate-lexicon", StringComparer.OrdinalIgnoreCase))
        {
            RunHeadless(InstallVerificationLogFileName, () =>
            {
                LexiconDatabaseBuilder.CreateOrMigrate(ProductLayout.StaticResourceDirectory, ProductLayout.PackageResourceDirectory);
                return "SQLite lexicon migration completed.";
            });
            return;
        }

        if (e.Args.Contains("--verify-lexicon", StringComparer.OrdinalIgnoreCase))
        {
            RunHeadless(LexiconVerificationLogFileName, () =>
            {
                LexiconDatabaseBuilder.VerifyInstalledDatabase(ProductLayout.StaticResourceDirectory);
                return "SQLite lexicon verification passed.";
            });
            return;
        }

        if (e.Args.Contains("--verify-package", StringComparer.OrdinalIgnoreCase))
        {
            InstallerVerification result = InstallerVerifier.VerifyPackage(ProductLayout.PayloadDirectory);
            WriteVerificationLog(PackageVerificationLogFileName, result.Message);
            Environment.ExitCode = result.Succeeded ? 0 : 1;
            Shutdown(Environment.ExitCode);
            return;
        }

        if (e.Args.Contains("--install-and-verify", StringComparer.OrdinalIgnoreCase))
        {
            InstallerVerification result = global::EnputMethod.Installer.MainWindow.InstallAndVerify();
            WriteVerificationLog(InstallVerificationLogFileName, result.Message);
            Environment.ExitCode = result.Succeeded ? 0 : 1;
            Shutdown(Environment.ExitCode);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void RunHeadless(string logFileName, Func<string> action)
    {
        try
        {
            WriteVerificationLog(logFileName, action());
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            WriteVerificationLog(logFileName, ex.ToString());
            Environment.ExitCode = 1;
        }
        finally { Current.Shutdown(Environment.ExitCode); }
    }

    private static void WriteVerificationLog(string fileName, string message)
    {
        string directory = ProductLayout.UserDataDirectory;
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, fileName), message);
    }
}