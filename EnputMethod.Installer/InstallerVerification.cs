using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace EnputMethod.Installer;

internal sealed record InstallerVerification(bool Succeeded, string Message)
{
    internal static InstallerVerification Success(string message) => new(true, message);
    internal static InstallerVerification Failure(string message) => new(false, message);
}

internal static class InstallerVerifier
{
    private const string TextServiceClsid = "{9C8945D5-01DF-48F4-A8DB-57E8B6A1EB10}";
    private static readonly string[] RequiredPackageFiles =
    [
        "EnputMethod.Tsf.dll", "config.json", "shortcut.json", "dictionary.txt", "suggestions.json", "emoji.json", "translations.json",
        "Overlay\\EnputMethod.Overlay.exe", "Overlay\\EnputMethod.Overlay.dll", "Overlay\\EnputMethod.Overlay.deps.json", "Overlay\\EnputMethod.Overlay.runtimeconfig.json", "Overlay\\EmojiAssets\\1f600.png", "Overlay\\EmojiAssets\\2764.png", "Overlay\\TWEMOJI-LICENSE.txt",
        "themes\\dark.json", "themes\\eye-care.json", "themes\\light.json", "themes\\paper.json",
    ];

    internal static InstallerVerification VerifyPackage(string packageDirectory)
    {
        if (!Directory.Exists(packageDirectory)) return InstallerVerification.Failure($"Installer package directory is missing: {packageDirectory}");
        string[] missing = RequiredPackageFiles.Where(file => !File.Exists(Path.Combine(packageDirectory, file))).ToArray();
        return missing.Length == 0
            ? InstallerVerification.Success("Installer package is complete.")
            : InstallerVerification.Failure($"Installer package is missing: {string.Join(", ", missing)}");
    }

    internal static InstallerVerification VerifySystemInstallation(string packageDirectory)
    {
        InstallerVerification package = VerifyPackage(packageDirectory);
        if (!package.Succeeded) return package;

        using RegistryKey? registration = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\CLSID\{TextServiceClsid}\InprocServer32");
        string? installedDll = registration?.GetValue(null) as string;
        string sourceDll = Path.Combine(packageDirectory, "EnputMethod.Tsf.dll");
        if (string.IsNullOrWhiteSpace(installedDll) || !File.Exists(installedDll))
        {
            return InstallerVerification.Failure("Installed TSF registration or DLL is missing.");
        }
        if (!FilesMatch(sourceDll, installedDll))
        {
            return InstallerVerification.Failure("Installed TSF registration or DLL does not match the package.");
        }

        string sourceOverlay = Path.Combine(packageDirectory, "Overlay");
        string installedOverlay = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Enput Method", "Overlay");
        foreach (string sourceFile in Directory.EnumerateFiles(sourceOverlay, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceOverlay, sourceFile);
            string destinationFile = Path.Combine(installedOverlay, relativePath);
            if (!File.Exists(destinationFile) || !FilesMatch(sourceFile, destinationFile))
            {
                return InstallerVerification.Failure($"Installed Overlay file does not match the package: {relativePath}");
            }
        }

        return InstallerVerification.Success("System installation matches the package.");
    }

    private static bool FilesMatch(string left, string right)
    {
        FileInfo leftInfo = new(left);
        FileInfo rightInfo = new(right);
        if (leftInfo.Length != rightInfo.Length) return false;
        using FileStream leftStream = File.OpenRead(left);
        using FileStream rightStream = File.OpenRead(right);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(leftStream), SHA256.HashData(rightStream));
    }
}
