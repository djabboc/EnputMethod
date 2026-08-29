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
    private const string TextServiceProfileGuid = "{55F31085-E7CD-4886-BB80-1D61CE392107}";
    private const string KeyboardCategoryGuid = "{34745C63-B2F0-4784-8B67-5E12C8701A31}";
    private static readonly string[] RequiredPayloadFiles =
    [
        "EnputMethod.Tsf.dll",
        "Overlay\\EnputMethod.Overlay.exe", "Overlay\\EnputMethod.Overlay.dll", "Overlay\\EnputMethod.Overlay.deps.json", "Overlay\\EnputMethod.Overlay.runtimeconfig.json", "Overlay\\EmojiAssets\\1f600.png", "Overlay\\EmojiAssets\\2764.png", "Overlay\\TWEMOJI-LICENSE.txt",
        "Resources\\config.json", "Resources\\shortcut.json", "Resources\\dictionary.txt", "Resources\\enput.seed.db", "Resources\\wordnet-phrases.txt", "Resources\\WORDNET-ATTRIBUTION.txt",
        "Resources\\themes\\dark.json", "Resources\\themes\\eye-care.json", "Resources\\themes\\light.json", "Resources\\themes\\paper.json",
    ];

    internal static InstallerVerification VerifyPackage(string payloadDirectory)
    {
        if (!Directory.Exists(payloadDirectory)) return InstallerVerification.Failure($"Installer payload directory is missing: {payloadDirectory}");
        string[] missing = RequiredPayloadFiles.Where(file => !File.Exists(Path.Combine(payloadDirectory, file))).ToArray();
        return missing.Length == 0
            ? InstallerVerification.Success("Installer payload is complete.")
            : InstallerVerification.Failure($"Installer payload is missing: {string.Join(", ", missing)}");
    }

    internal static InstallerVerification VerifySystemInstallation(string payloadDirectory)
    {
        InstallerVerification package = VerifyPackage(payloadDirectory);
        if (!package.Succeeded) return package;

        using RegistryKey? registration = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\CLSID\{TextServiceClsid}\InprocServer32");
        string? installedDll = registration?.GetValue(null) as string;
        string sourceDll = Path.Combine(payloadDirectory, "EnputMethod.Tsf.dll");
        if (string.IsNullOrWhiteSpace(installedDll) || !File.Exists(installedDll))
        {
            return InstallerVerification.Failure("Installed TSF registration or DLL is missing.");
        }
        if (!installedDll.StartsWith(ProductLayout.InstallDirectory, StringComparison.OrdinalIgnoreCase) || !FilesMatch(sourceDll, installedDll))
        {
            return InstallerVerification.Failure("Installed TSF registration does not point to the verified Program Files product directory.");
        }

        string tipRoot = $@"SOFTWARE\Microsoft\CTF\TIP\{TextServiceClsid}";
        using RegistryKey? profile = Registry.LocalMachine.OpenSubKey($@"{tipRoot}\LanguageProfile\0x00000804\{TextServiceProfileGuid}");
        using RegistryKey? keyboardCategory = Registry.LocalMachine.OpenSubKey($@"{tipRoot}\Category\Category\{KeyboardCategoryGuid}\{TextServiceClsid}");
        if (profile is null || keyboardCategory is null)
        {
            return InstallerVerification.Failure("Installed TSF profile or keyboard category is missing.");
        }

        string sourceOverlay = Path.Combine(payloadDirectory, "Overlay");
        string installedOverlay = Path.Combine(ProductLayout.InstallDirectory, "Overlay");
        foreach (string sourceFile in Directory.EnumerateFiles(sourceOverlay, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceOverlay, sourceFile);
            string destinationFile = Path.Combine(installedOverlay, relativePath);
            if (!File.Exists(destinationFile) || !FilesMatch(sourceFile, destinationFile))
            {
                return InstallerVerification.Failure($"Installed Overlay file does not match the payload: {relativePath}");
            }
        }

        string resources = ProductLayout.StaticResourceDirectory;
        string[] missingResources = RequiredPayloadFiles.Where(file => file.StartsWith("Resources\\", StringComparison.Ordinal))
            .Select(file => file["Resources\\".Length..])
            .Where(file => !File.Exists(Path.Combine(resources, file))).ToArray();
        if (missingResources.Length != 0)
        {
            return InstallerVerification.Failure($"Installed static resources are missing: {string.Join(", ", missingResources)}");
        }

        return InstallerVerification.Success("System installation matches the payload; static resources are in the Program Files product directory.");
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
