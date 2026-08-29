using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.VisualBasic.FileIO;

namespace EnputMethod.Installer;

public partial class MainWindow : Window
{
    private const string EcdictUrl = "https://raw.githubusercontent.com/skywind3000/ECDICT/master/ecdict.csv";
    private const string CcCedictUrl = "https://www.mdbg.net/chinese/export/cedict/cedict_1_0_ts_utf-8_mdbg.txt.gz";
    private static readonly Regex EnglishWordPattern = new(@"[A-Za-z][A-Za-z'-]*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int InstallInputMethodDelegate();

    public MainWindow() => InitializeComponent();

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallerVerification result = InstallAndVerify();
        MessageBox.Show(result.Message, "Enput Method");
        Close();
    }

    internal static InstallerVerification InstallAndVerify()
    {
        InstallerVerification package = InstallerVerifier.VerifyPackage(AppContext.BaseDirectory);
        if (!package.Succeeded) return package;
        try
        {
            int hr = InvokeNativeInstaller();
            if (hr < 0) return InstallerVerification.Failure($"Native TSF installation failed (0x{hr:X8}).");
            DeployOverlay();
            EnsureUserConfiguration();
            return InstallerVerifier.VerifySystemInstallation(AppContext.BaseDirectory);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            return InstallerVerification.Failure("Installation files are incomplete, incompatible, or still in use.");
        }

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
        MigrateLegacyConfiguration(destinationDirectory);
        CopyDefaultFile("config.json", destinationDirectory);
        MergeMissingConfigurationFields(destinationDirectory);
        MigrateDefaultFontSize(destinationDirectory);
        CopyDefaultFile("shortcut.json", destinationDirectory);
        CopyDefaultFile("dictionary.txt", destinationDirectory);
        LexiconDatabaseBuilder.CreateOrMigrate(destinationDirectory, AppContext.BaseDirectory);
        EnsureFullTranslationDictionary(destinationDirectory);
        EnsureCcCedictEnglishIndex(destinationDirectory);
        LexiconDatabaseBuilder.ImportDownloadedTranslations(destinationDirectory);
        CopyDefaultThemes(destinationDirectory);
    }

    private static void DeployOverlay()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "Overlay");
        string destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Enput Method", "Overlay");
        using var updateMutex = new Mutex(false, @"Local\EnputMethod.Overlay.Updating.v1");
        bool lockTaken = false;
        try
        {
            try
            {
                lockTaken = updateMutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                lockTaken = true;
            }

            if (!lockTaken) throw new IOException("Another Overlay update is already in progress.");

            Directory.CreateDirectory(destination);
            StopInstalledOverlay(destination);
            foreach (string file in Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(source, file);
                string destinationFile = Path.Combine(destination, relativePath);
                CopyOverlayFile(file, destinationFile, destination);
            }
        }
        finally
        {
            if (lockTaken) updateMutex.ReleaseMutex();
        }
    }

    private static void CopyOverlayFile(string sourceFile, string destinationFile, string overlayDirectory)
    {
        const int attempts = 6;
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        for (int attempt = 0; attempt < attempts; ++attempt)
        {
            try
            {
                File.Copy(sourceFile, destinationFile, true);
                return;
            }
            catch (IOException) when (attempt + 1 < attempts)
            {
                StopInstalledOverlay(overlayDirectory);
                Thread.Sleep(100);
            }
        }
    }
    private static void StopInstalledOverlay(string overlayDirectory)
    {
        string executable = Path.GetFullPath(Path.Combine(overlayDirectory, "EnputMethod.Overlay.exe"));
        foreach (Process process in Process.GetProcessesByName("EnputMethod.Overlay"))
        {
            try
            {
                if (!string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), executable, StringComparison.OrdinalIgnoreCase)) continue;
                _ = process.CloseMainWindow();
                if (!process.WaitForExit(2000))
                {
                    process.Kill(true);
                    _ = process.WaitForExit(2000);
                }
            }
            catch (InvalidOperationException)
            {
                // The companion can exit between process discovery and shutdown.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Only inaccessible processes are skipped; the deployed files will remain untouched if locked.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void MigrateLegacyConfiguration(string destinationDirectory)
    {
        string configuration = Path.Combine(destinationDirectory, "config.json");
        string legacyConfiguration = Path.Combine(destinationDirectory, "conf.json");
        if (!File.Exists(configuration) && File.Exists(legacyConfiguration) && !IsLegacyDefaultConfiguration(legacyConfiguration))
        {
            File.Copy(legacyConfiguration, configuration);
        }
    }

    private static bool IsLegacyDefaultConfiguration(string filePath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.EnumerateObject().Count() == 1
                && root.TryGetProperty("candidateCount", out JsonElement count)
                && count.ValueKind == JsonValueKind.Number
                && count.GetInt32() == 4;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void CopyDefaultFile(string fileName, string destinationDirectory)
    {
        string destination = Path.Combine(destinationDirectory, fileName);
        if (!File.Exists(destination))
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, fileName), destination);
        }
    }

    private static void MigrateDefaultFontSize(string destinationDirectory)
    {
        string configuration = Path.Combine(destinationDirectory, "config.json");
        try
        {
            JsonObject? settings = JsonNode.Parse(File.ReadAllText(configuration)) as JsonObject;
            if (settings?["fontSize"] is not JsonValue value || !value.TryGetValue<int>(out int fontSize) || fontSize != 16) return;
            settings["fontSize"] = 18;
            File.WriteAllText(configuration, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (JsonException)
        {
            // Keep a user's malformed configuration untouched.
        }
        catch (IOException)
        {
            // Configuration can be held briefly while the text service starts.
        }
    }

    private static void MergeDefaultSuggestions(string destinationDirectory)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "suggestions.json");
        string destination = Path.Combine(destinationDirectory, "suggestions.json");
        if (!File.Exists(destination))
        {
            File.Copy(source, destination);
            return;
        }

        try
        {
            JsonObject? bundled = JsonNode.Parse(File.ReadAllText(source)) as JsonObject;
            JsonObject? installed = JsonNode.Parse(File.ReadAllText(destination)) as JsonObject;
            if (bundled?["entries"] is not JsonArray bundledEntries || installed?["entries"] is not JsonArray installedEntries) return;

            var entriesByText = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode? entry in installedEntries)
            {
                if (entry is JsonObject objectEntry && objectEntry["text"]?.GetValue<string>() is string text && !string.IsNullOrWhiteSpace(text)) entriesByText.TryAdd(text, objectEntry);
            }

            bool changed = false;
            foreach (JsonNode? entry in bundledEntries)
            {
                if (entry is not JsonObject bundledEntry || bundledEntry["text"]?.GetValue<string>() is not string text || string.IsNullOrWhiteSpace(text)) continue;
                if (!entriesByText.TryGetValue(text, out JsonObject? installedEntry))
                {
                    JsonObject copy = (JsonObject)bundledEntry.DeepClone();
                    installedEntries.Add(copy);
                    entriesByText.Add(text, copy);
                    changed = true;
                    continue;
                }
                changed |= MergeSuggestionList(installedEntry, bundledEntry, "next");
                changed |= MergeSuggestionList(installedEntry, bundledEntry, "phrases");
                changed |= MergeSuggestionPriority(installedEntry, bundledEntry);
            }
            if (changed) File.WriteAllText(destination, installed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (JsonException)
        {
            // Leave a user's malformed custom dictionary untouched.
        }
        catch (IOException)
        {
            // The input method can hold the file briefly while it starts.
        }
    }

    private static void MergeMissingConfigurationFields(string destinationDirectory)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "config.json");
        string destination = Path.Combine(destinationDirectory, "config.json");
        try
        {
            JsonObject? bundled = JsonNode.Parse(File.ReadAllText(source)) as JsonObject;
            JsonObject? installed = JsonNode.Parse(File.ReadAllText(destination)) as JsonObject;
            if (bundled is null || installed is null) return;

            bool changed = false;
            foreach ((string key, JsonNode? value) in bundled)
            {
                if (installed.ContainsKey(key)) continue;
                installed[key] = value?.DeepClone();
                changed = true;
            }
            if (changed) File.WriteAllText(destination, installed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (JsonException)
        {
            // Keep malformed user configuration untouched.
        }
        catch (IOException)
        {
            // The text service can read configuration while installation is running.
        }
    }

    private static bool MergeSuggestionList(JsonObject installedEntry, JsonObject bundledEntry, string propertyName)
    {
        if (bundledEntry[propertyName] is not JsonArray bundledValues) return false;
        JsonArray installedValues = installedEntry[propertyName] as JsonArray ?? new JsonArray();
        bool changed = !ReferenceEquals(installedEntry[propertyName], installedValues);
        if (changed) installedEntry[propertyName] = installedValues;
        var existingValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? valueNode in installedValues)
        {
            if (valueNode is JsonValue value && value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text)) existingValues.Add(text);
        }
        foreach (JsonNode? valueNode in bundledValues)
        {
            if (valueNode is not JsonValue value || !value.TryGetValue<string>(out string? text) || string.IsNullOrWhiteSpace(text) || !existingValues.Add(text)) continue;
            installedValues.Add(text);
            changed = true;
        }
        return changed;
    }

    private static bool MergeSuggestionPriority(JsonObject installedEntry, JsonObject bundledEntry)
    {
        if (bundledEntry["priority"] is not JsonValue bundledValue || !bundledValue.TryGetValue<int>(out int bundledPriority)) return false;
        if (installedEntry["priority"] is JsonValue installedValue && installedValue.TryGetValue<int>(out int installedPriority) && installedPriority >= bundledPriority) return false;
        installedEntry["priority"] = bundledPriority;
        return true;
    }
    private static void MergeDefaultEmojiDictionary(string destinationDirectory)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "emoji.json");
        string destination = Path.Combine(destinationDirectory, "emoji.json");
        if (!File.Exists(destination))
        {
            File.Copy(source, destination);
            return;
        }

        try
        {
            JsonObject? bundled = JsonNode.Parse(File.ReadAllText(source)) as JsonObject;
            JsonArray? bundledEntries = bundled?["entries"] as JsonArray;
            JsonNode? installedDocument = JsonNode.Parse(File.ReadAllText(destination));
            if (bundledEntries is null || installedDocument is not JsonObject installed) return;

            bool changed = false;
            JsonArray? installedEntries = installed["entries"] as JsonArray;
            if (installedEntries is null)
            {
                installedEntries = new JsonArray();
                foreach ((string keyword, JsonNode? emojiValue) in installed)
                {
                    if (emojiValue is not JsonValue value || !value.TryGetValue<string>(out string? emoji) || string.IsNullOrWhiteSpace(keyword) || string.IsNullOrWhiteSpace(emoji)) continue;
                    installedEntries.Add(new JsonObject { ["emoji"] = emoji, ["keywords"] = new JsonArray(keyword) });
                }
                if (installedEntries.Count == 0) return;
                installed.Clear();
                installed["entries"] = installedEntries;
                changed = true;
            }

            var entriesByEmoji = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            foreach (JsonNode? node in installedEntries)
            {
                if (node is not JsonObject entry || EmojiValue(entry) is not string emoji || string.IsNullOrWhiteSpace(emoji)) continue;
                entriesByEmoji.TryAdd(emoji, entry);
            }

            foreach (JsonNode? node in bundledEntries)
            {
                if (node is not JsonObject bundledEntry || EmojiValue(bundledEntry) is not string emoji || string.IsNullOrWhiteSpace(emoji)) continue;
                if (!entriesByEmoji.TryGetValue(emoji, out JsonObject? installedEntry))
                {
                    JsonObject copy = (JsonObject)bundledEntry.DeepClone();
                    installedEntries.Add(copy);
                    entriesByEmoji.Add(emoji, copy);
                    changed = true;
                    continue;
                }
                changed |= MergeEmojiKeywords(installedEntry, bundledEntry);
                changed |= MergeEmojiPriority(installedEntry, bundledEntry);
            }

            if (changed) File.WriteAllText(destination, installed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (JsonException)
        {
            // Leave a user's malformed custom dictionary untouched.
        }
        catch (IOException)
        {
            // The input method can hold the file briefly while it starts.
        }
    }

    private static string? EmojiValue(JsonObject entry) => entry["emoji"] is JsonValue value && value.TryGetValue<string>(out string? emoji) ? emoji : null;

    private static bool MergeEmojiKeywords(JsonObject installedEntry, JsonObject bundledEntry)
    {
        if (bundledEntry["keywords"] is not JsonArray bundledKeywords) return false;
        JsonArray installedKeywords = installedEntry["keywords"] as JsonArray ?? new JsonArray();
        bool changed = !ReferenceEquals(installedEntry["keywords"], installedKeywords);
        if (changed) installedEntry["keywords"] = installedKeywords;
        var existingKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? keywordNode in installedKeywords)
        {
            if (keywordNode is JsonValue keywordValue && keywordValue.TryGetValue<string>(out string? keyword) && !string.IsNullOrWhiteSpace(keyword)) existingKeywords.Add(keyword);
        }
        foreach (JsonNode? keywordNode in bundledKeywords)
        {
            if (keywordNode is not JsonValue keywordValue || !keywordValue.TryGetValue<string>(out string? keyword) || string.IsNullOrWhiteSpace(keyword) || !existingKeywords.Add(keyword)) continue;
            installedKeywords.Add(keyword);
            changed = true;
        }
        return changed;
    }

    private static bool MergeEmojiPriority(JsonObject installedEntry, JsonObject bundledEntry)
    {
        if (bundledEntry["priority"] is not JsonValue bundledValue || !bundledValue.TryGetValue<int>(out int bundledPriority)) return false;
        if (installedEntry["priority"] is JsonValue installedValue && installedValue.TryGetValue<int>(out int installedPriority) && installedPriority >= bundledPriority) return false;
        installedEntry["priority"] = bundledPriority;
        return true;
    }

    private static void MergeDefaultTranslations(string destinationDirectory)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "translations.json");
        string destination = Path.Combine(destinationDirectory, "translations.json");
        if (!File.Exists(destination))
        {
            File.Copy(source, destination);
            return;
        }

        try
        {
            JsonObject? bundled = JsonNode.Parse(File.ReadAllText(source)) as JsonObject;
            JsonObject? installed = JsonNode.Parse(File.ReadAllText(destination)) as JsonObject;
            if (bundled?["entries"] is not JsonArray bundledEntries || installed is null) return;

            bool changed = MigrateLegacyTranslations(installed);
            if (installed["entries"] is not JsonArray installedEntries) return;

            var existingWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode? entry in installedEntries)
            {
                if (entry is JsonObject objectEntry && objectEntry["text"]?.GetValue<string>() is string text) existingWords.Add(text);
            }

            foreach (JsonNode? entry in bundledEntries)
            {
                if (entry is not JsonObject objectEntry || objectEntry["text"]?.GetValue<string>() is not string text || !existingWords.Add(text)) continue;
                installedEntries.Add(objectEntry.DeepClone());
                changed = true;
            }
            if (changed) File.WriteAllText(destination, installed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (JsonException)
        {
            // Leave a user's malformed custom dictionary untouched.
        }
        catch (IOException)
        {
            // The input method can hold the file briefly while it starts.
        }
    }

    private static bool MigrateLegacyTranslations(JsonObject installed)
    {
        if (installed["entries"] is JsonArray) return false;

        var entries = new JsonArray();
        foreach ((string text, JsonNode? node) in installed)
        {
            if (node is not JsonObject legacyEntry || string.IsNullOrWhiteSpace(text)) continue;
            var entry = new JsonObject { ["text"] = text };
            if (legacyEntry["partOfSpeech"] is JsonValue partOfSpeech && partOfSpeech.TryGetValue<string>(out string? part) && !string.IsNullOrWhiteSpace(part)) entry["partOfSpeech"] = new JsonArray(part);
            if (legacyEntry["chinese"] is JsonValue chinese && chinese.TryGetValue<string>(out string? translation) && !string.IsNullOrWhiteSpace(translation)) entry["translations"] = new JsonObject { ["zh-CN"] = new JsonArray(translation) };
            if (legacyEntry["english"] is JsonValue english && english.TryGetValue<string>(out string? example) && !string.IsNullOrWhiteSpace(example)) entry["examples"] = new JsonArray(new JsonObject { ["text"] = example });
            if (legacyEntry["source"] is JsonValue source && source.TryGetValue<string>(out string? sourceText) && !string.IsNullOrWhiteSpace(sourceText)) entry["source"] = sourceText;
            entries.Add(entry);
        }
        installed.Clear();
        installed["entries"] = entries;
        return true;
    }
    private static void EnsureCcCedictEnglishIndex(string destinationDirectory)
    {
        string destination = Path.Combine(destinationDirectory, "translations.cc-cedict.jsonl");
        if (File.Exists(Path.Combine(destinationDirectory, "enput.db.cc-cedict.ready")))
        {
            try
            {
                WriteCcCedictAttribution(destinationDirectory);
            }
            catch (IOException)
            {
                // The completed index remains usable when the attribution file is held briefly.
            }
            return;
        }

        string downloadedArchive = destination + ".download.gz";
        string pendingIndex = destination + ".pending";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using Stream source = client.GetStreamAsync(CcCedictUrl).GetAwaiter().GetResult();
            using (var target = new FileStream(downloadedArchive, FileMode.Create, FileAccess.Write, FileShare.None)) source.CopyTo(target);
            ConvertCcCedictToEnglishIndex(downloadedArchive, pendingIndex);
            File.Move(pendingIndex, destination, true);
            WriteCcCedictAttribution(destinationDirectory);
        }
        catch (HttpRequestException)
        {
            // ECDICT and the user dictionary remain available when the supplemental download fails.
        }
        catch (InvalidDataException)
        {
            // Keep a previously completed index intact when an upstream archive is malformed.
        }
        catch (IOException)
        {
            // Keep any existing dictionary intact when another input-method process holds a file.
        }
        finally
        {
            if (File.Exists(downloadedArchive)) File.Delete(downloadedArchive);
            if (File.Exists(pendingIndex)) File.Delete(pendingIndex);
        }
    }

    private static void ConvertCcCedictToEnglishIndex(string sourcePath, string destinationPath)
    {
        var index = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        using var compressed = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var decompressed = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressed, new UTF8Encoding(false, true));
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            int firstSpace = line.IndexOf(' ');
            int pinyinStart = firstSpace < 0 ? -1 : line.IndexOf(" [", firstSpace + 1, StringComparison.Ordinal);
            int definitionsStart = pinyinStart < 0 ? -1 : line.IndexOf("] /", pinyinStart, StringComparison.Ordinal);
            if (firstSpace <= 0 || pinyinStart <= firstSpace || definitionsStart < 0) continue;
            string chinese = line.Substring(firstSpace + 1, pinyinStart - firstSpace - 1);
            if (string.IsNullOrWhiteSpace(chinese)) continue;
            foreach (Match match in EnglishWordPattern.Matches(line.AsSpan(definitionsStart + 3).ToString()))
            {
                string key = match.Value.ToLowerInvariant();
                if (key.Length < 3) continue;
                if (!index.TryGetValue(key, out SortedSet<string>? translations))
                {
                    translations = new SortedSet<string>(StringComparer.Ordinal);
                    index.Add(key, translations);
                }
                if (translations.Count < 8) translations.Add(chinese);
            }
        }

        using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });
        foreach ((string key, SortedSet<string> translations) in index)
        {
            writer.WriteStartObject();
            writer.WriteString("key", key);
            writer.WriteString("text", key);
            writer.WritePropertyName("translations");
            writer.WriteStartObject();
            writer.WriteStartArray("zh-CN");
            foreach (string translation in translations) writer.WriteStringValue(translation);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteString("source", "CC-CEDICT 1.0 (CC BY-SA 4.0; https://www.mdbg.net/chinese/dictionary?page=cc-cedict)");
            writer.WriteEndObject();
            writer.Flush();
            output.WriteByte((byte)'\n');
            writer.Reset();
        }
    }

    private static void WriteCcCedictAttribution(string destinationDirectory)
    {
        string attribution = Path.Combine(destinationDirectory, "CC-CEDICT-ATTRIBUTION.txt");
        File.WriteAllText(attribution, "This product includes a derived English-to-Chinese lookup index from CC-CEDICT 1.0.\nSource: https://www.mdbg.net/chinese/dictionary?page=cc-cedict\nLicense: CC BY-SA 4.0 (https://creativecommons.org/licenses/by-sa/4.0/).\n");
    }
    private static void EnsureFullTranslationDictionary(string destinationDirectory)
    {
        string destination = Path.Combine(destinationDirectory, "translations.ecdict.jsonl");
        if (File.Exists(Path.Combine(destinationDirectory, "enput.db.ecdict.ready"))) return;

        string downloadedCsv = destination + ".download";
        string pendingDictionary = destination + ".pending";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using Stream source = client.GetStreamAsync(EcdictUrl).GetAwaiter().GetResult();
            using (var target = new FileStream(downloadedCsv, FileMode.Create, FileAccess.Write, FileShare.None)) source.CopyTo(target);
            ConvertEcdictToJsonLines(downloadedCsv, pendingDictionary);
            File.Move(pendingDictionary, destination, true);
        }
        catch (HttpRequestException)
        {
            // The compact bundled dictionary remains available when the network is unavailable.
        }
        catch (IOException)
        {
            // Keep any existing dictionary intact when another input-method process holds a file.
        }
        finally
        {
            if (File.Exists(downloadedCsv)) File.Delete(downloadedCsv);
            if (File.Exists(pendingDictionary)) File.Delete(pendingDictionary);
        }
    }

    private static void ConvertEcdictToJsonLines(string sourcePath, string destinationPath)
    {
        using var parser = new TextFieldParser(sourcePath, Encoding.UTF8, true) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        _ = parser.ReadFields(); // CSV column names.
        using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false });
        while (!parser.EndOfData)
        {
            string[]? fields = parser.ReadFields();
            if (fields is null || fields.Length < 5 || string.IsNullOrWhiteSpace(fields[0])) continue;
            string word = fields[0].Trim();
            writer.WriteStartObject();
            writer.WriteString("key", word.ToLowerInvariant());
            writer.WriteString("text", word);
            WritePartOfSpeech(writer, fields[4]);
            writer.WritePropertyName("translations");
            writer.WriteStartObject();
            WriteMeanings(writer, "en", fields[2]);
            WriteMeanings(writer, "zh-CN", fields[3]);
            writer.WriteEndObject();
            writer.WriteString("source", "ECDICT 1.0.28 (MIT License)");
            writer.WriteEndObject();
            writer.Flush();
            output.WriteByte((byte)'\n');
            writer.Reset();
        }
    }

    private static void WritePartOfSpeech(Utf8JsonWriter writer, string value)
    {
        writer.WriteStartArray("partOfSpeech");
        foreach (string part in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string label = part.Split(':', 2)[0].Trim();
            if (!string.IsNullOrEmpty(label)) writer.WriteStringValue(label);
        }
        writer.WriteEndArray();
    }

    private static void WriteMeanings(Utf8JsonWriter writer, string language, string value)
    {
        writer.WriteStartArray(language);
        foreach (string line in value.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string meaning = line.Trim();
            if (!string.IsNullOrEmpty(meaning)) writer.WriteStringValue(meaning);
        }
        writer.WriteEndArray();
    }

    private static void CopyDefaultThemes(string destinationDirectory)
    {
        string sourceDirectory = Path.Combine(AppContext.BaseDirectory, "themes");
        string targetDirectory = Path.Combine(destinationDirectory, "themes");
        Directory.CreateDirectory(targetDirectory);
        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "*.json"))
        {
            string destination = Path.Combine(targetDirectory, Path.GetFileName(source));
            if (!File.Exists(destination))
            {
                File.Copy(source, destination);
                continue;
            }

            MergeMissingThemeFields(source, destination);
        }
    }

    private static void MergeMissingThemeFields(string source, string destination)
    {
        try
        {
            JsonObject? bundled = JsonNode.Parse(File.ReadAllText(source)) as JsonObject;
            JsonObject? installed = JsonNode.Parse(File.ReadAllText(destination)) as JsonObject;
            if (bundled is null || installed is null) return;

            bool changed = false;
            foreach ((string key, JsonNode? value) in bundled)
            {
                if (installed.ContainsKey(key)) continue;
                installed[key] = value?.DeepClone();
                changed = true;
            }
            if (changed) File.WriteAllText(destination, installed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (JsonException)
        {
            // Leave a user's malformed theme untouched.
        }
        catch (IOException)
        {
            // The input method can hold a theme briefly while it starts.
        }
    }
}
