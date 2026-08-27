using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.VisualBasic.FileIO;

namespace EnputMethod.Installer;

public partial class MainWindow : Window
{
    private const string EcdictUrl = "https://raw.githubusercontent.com/skywind3000/ECDICT/master/ecdict.csv";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int InstallInputMethodDelegate();

    public MainWindow() => InitializeComponent();

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        string message;
        try
        {
            int hr = InvokeNativeInstaller();
            if (hr >= 0)
            {
                EnsureUserConfiguration();
            }
            message = hr >= 0
                ? "安装完成。请切换到其他输入法后再切回 Enput Method。"
                : $"安装失败 (0x{hr:X8})。";
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            message = "安装程序文件不完整或版本不匹配。请将整个安装程序文件夹中的文件放在一起后重试。";
        }

        MessageBox.Show(message, "Enput Method");
        Close();
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
        CopyDefaultFile("shortcut.json", destinationDirectory);
        CopyDefaultFile("dictionary.txt", destinationDirectory);
        CopyDefaultFile("suggestions.json", destinationDirectory);
        CopyDefaultFile("emoji.json", destinationDirectory);
        MergeDefaultTranslations(destinationDirectory);
        EnsureFullTranslationDictionary(destinationDirectory);
        CopyDefaultThemes(destinationDirectory);
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
            if (bundled?["entries"] is not JsonArray bundledEntries || installed?["entries"] is not JsonArray installedEntries) return;

            var existingWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode? entry in installedEntries)
            {
                if (entry is JsonObject objectEntry && objectEntry["text"]?.GetValue<string>() is string text) existingWords.Add(text);
            }

            bool changed = false;
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

    private static void EnsureFullTranslationDictionary(string destinationDirectory)
    {
        string destination = Path.Combine(destinationDirectory, "translations.ecdict.jsonl");
        if (File.Exists(destination) && new FileInfo(destination).Length > 1024) return;

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
            if (!File.Exists(destination)) File.Copy(source, destination);
        }
    }
}
