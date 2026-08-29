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
                LexiconDatabaseBuilder.CreateOrMigrate(staging, ProductLayout.PackageResourceDirectory);
                System.IO.File.Copy(System.IO.Path.Combine(staging, "enput.db"), output, true);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                WriteInstallVerificationLog(ex.ToString());
                Environment.ExitCode = 1;
            }
            finally { Current.Shutdown(); }
            return;
        }

        if (e.Args.Contains("--migrate-lexicon", StringComparer.OrdinalIgnoreCase))
        {
            RunHeadless(() =>
            {
                LexiconDatabaseBuilder.CreateOrMigrate(ProductLayout.StaticResourceDirectory, ProductLayout.PackageResourceDirectory);
                return "SQLite lexicon migration completed.";
            });
            return;
        }

        if (e.Args.Contains("--verify-lexicon", StringComparer.OrdinalIgnoreCase))
        {
            RunHeadless(() =>
            {
                LexiconDatabaseBuilder.VerifyInstalledDatabase(ProductLayout.StaticResourceDirectory);
                return "SQLite lexicon verification passed.";
            });
            return;
        }

        if (e.Args.Contains("--verify-package", StringComparer.OrdinalIgnoreCase))
        {
            InstallerVerification result = InstallerVerifier.VerifyPackage(ProductLayout.PayloadDirectory);
            WriteInstallVerificationLog(result.Message);
            Environment.ExitCode = result.Succeeded ? 0 : 1;
            Shutdown();
            return;
        }

        if (e.Args.Contains("--install-and-verify", StringComparer.OrdinalIgnoreCase))
        {
            InstallerVerification result = global::EnputMethod.Installer.MainWindow.InstallAndVerify();
            WriteInstallVerificationLog(result.Message);
            Environment.ExitCode = result.Succeeded ? 0 : 1;
            Shutdown();
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void RunHeadless(Func<string> action)
    {
        try
        {
            WriteInstallVerificationLog(action());
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            WriteInstallVerificationLog(ex.ToString());
            Environment.ExitCode = 1;
        }
        finally { Current.Shutdown(); }
    }

    private static void WriteInstallVerificationLog(string message)
    {
        string directory = ProductLayout.UserDataDirectory;
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, InstallVerificationLogFileName), message);
    }
}