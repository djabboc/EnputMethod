using System;
using System.IO;

namespace EnputMethod.Installer;

internal static class ProductLayout
{
    internal const string ProductName = "Enput Method";

    internal static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);

    internal static string StaticResourceDirectory => Path.Combine(InstallDirectory, "Resources");

    internal static string UserDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName, "UserData");

    internal static string LegacyUserDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);

    internal static string PayloadDirectory
    {
        get
        {
            string payload = Path.Combine(AppContext.BaseDirectory, "payload");
            return Directory.Exists(payload) ? payload : AppContext.BaseDirectory;
        }
    }

    internal static string PackageResourceDirectory
    {
        get
        {
            string resources = Path.Combine(PayloadDirectory, "Resources");
            return Directory.Exists(resources) ? resources : PayloadDirectory;
        }
    }
}