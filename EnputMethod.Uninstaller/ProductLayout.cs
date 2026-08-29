using System;
using System.IO;

namespace EnputMethod.Uninstaller;

internal static class ProductLayout
{
    internal static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Enput Method");

    internal static string PayloadDirectory
    {
        get
        {
            string payload = Path.Combine(AppContext.BaseDirectory, "payload");
            return Directory.Exists(payload) ? payload : AppContext.BaseDirectory;
        }
    }
}