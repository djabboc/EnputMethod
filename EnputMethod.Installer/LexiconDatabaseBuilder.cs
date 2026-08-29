using System.Text.Json;
using System.IO;

namespace EnputMethod.Installer;

internal static class LexiconDatabaseBuilder
{
    internal const int SchemaVersion = 1;

    internal static void CreateOrMigrate(string userDirectory, string packageDirectory)
    {
        string databasePath = Path.Combine(userDirectory, "enput.db");
        if (File.Exists(databasePath) && File.Exists(Path.Combine(userDirectory, "enput.db.ready"))) return;

        string pending = databasePath + ".pending";
        if (File.Exists(pending)) File.Delete(pending);
        string seed = Path.Combine(packageDirectory, "enput.seed.db");
        if (!HasLegacyLexicon(userDirectory) && File.Exists(seed))
        {
            File.Copy(seed, pending, true);
            ValidateDatabase(pending);
            File.Move(pending, databasePath, true);
            File.WriteAllText(Path.Combine(userDirectory, "enput.db.ready"), SchemaVersion.ToString());
            return;
        }
        using (var database = new NativeSqliteConnection(pending))
        {
            CreateSchema(database);
            database.Execute("BEGIN IMMEDIATE;");
            try
            {
                ImportWords(database, PickInput(userDirectory, packageDirectory, "dictionary.txt"));
                ImportSuggestions(database, PickInput(userDirectory, packageDirectory, "suggestions.json"));
                ImportEmoji(database, PickInput(userDirectory, packageDirectory, "emoji.json"));
                ImportTranslations(database, PickInput(userDirectory, packageDirectory, "translations.json"), 0);
                ImportTranslationLines(database, Path.Combine(userDirectory, "translations.ecdict.jsonl"), 10);
                ImportTranslationLines(database, Path.Combine(userDirectory, "translations.cc-cedict.jsonl"), 20);
                database.Execute($"INSERT INTO metadata(key, value) VALUES('schemaVersion', '{SchemaVersion}') ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
                database.Execute("COMMIT;");
            }
            catch
            {
                database.Execute("ROLLBACK;");
                throw;
            }
            if (database.ScalarInt("SELECT COUNT(*) FROM words;") < 100 || database.ScalarInt("SELECT COUNT(*) FROM emoji;") < 100) throw new InvalidOperationException("SQLite lexicon validation failed.");
        }
        File.Move(pending, databasePath, true);
        File.WriteAllText(Path.Combine(userDirectory, "enput.db.ready"), SchemaVersion.ToString());
        DeleteLegacyJson(userDirectory);
    }

    internal static void ImportDownloadedTranslations(string userDirectory)
    {
        string ecdict = Path.Combine(userDirectory, "translations.ecdict.jsonl");
        string cedict = Path.Combine(userDirectory, "translations.cc-cedict.jsonl");
        if (!File.Exists(ecdict) && !File.Exists(cedict)) return;
        using var database = new NativeSqliteConnection(Path.Combine(userDirectory, "enput.db"));
        database.Execute("BEGIN IMMEDIATE;");
        try
        {
            ImportTranslationLines(database, ecdict, 10);
            ImportTranslationLines(database, cedict, 20);
            database.Execute("COMMIT;");
        }
        catch
        {
            database.Execute("ROLLBACK;");
            throw;
        }
        if (File.Exists(ecdict)) { File.Delete(ecdict); File.WriteAllText(Path.Combine(userDirectory, "enput.db.ecdict.ready"), SchemaVersion.ToString()); }
        if (File.Exists(cedict)) { File.Delete(cedict); File.WriteAllText(Path.Combine(userDirectory, "enput.db.cc-cedict.ready"), SchemaVersion.ToString()); }
    }

    private static bool HasLegacyLexicon(string directory) => new[] { "suggestions.json", "emoji.json", "translations.json", "translations.ecdict.jsonl", "translations.cc-cedict.jsonl" }
        .Any(name => File.Exists(Path.Combine(directory, name)));

    private static void ValidateDatabase(string path)
    {
        using var database = new NativeSqliteConnection(path);
        if (database.ScalarInt("SELECT CAST(value AS INTEGER) FROM metadata WHERE key = 'schemaVersion';") != SchemaVersion ||
            database.ScalarInt("SELECT COUNT(*) FROM words;") < 100 || database.ScalarInt("SELECT COUNT(*) FROM emoji;") < 100) throw new InvalidOperationException("SQLite seed validation failed.");
    }

    internal static void VerifyInstalledDatabase(string userDirectory)
    {
        string databasePath = Path.Combine(userDirectory, "enput.db");
        if (!File.Exists(databasePath) || !File.Exists(Path.Combine(userDirectory, "enput.db.ready"))) throw new InvalidOperationException("SQLite lexicon is not ready.");
        using var database = new NativeSqliteConnection(databasePath);
        if (database.ScalarInt("SELECT CAST(value AS INTEGER) FROM metadata WHERE key = 'schemaVersion';") != SchemaVersion) throw new InvalidOperationException("SQLite schema version is invalid.");
        if (database.ScalarInt("SELECT COUNT(*) FROM words WHERE normalized >= 'he' AND normalized < 'he' || char(65535);") < 3) throw new InvalidOperationException("Word prefix lookup validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM suggestions WHERE trigger = 'can' AND candidate = 'can i help you?';") != 1) throw new InvalidOperationException("Phrase suggestion validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM emoji_keyword WHERE normalized = 'fire';") < 1 || database.ScalarInt("SELECT COUNT(*) FROM emoji_keyword WHERE normalized = 'saw';") < 1) throw new InvalidOperationException("Emoji lookup validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM words WHERE normalized >= 'h' AND normalized < ('h' || char(65535)) AND normalized LIKE '%h%p%y%';") < 1) throw new InvalidOperationException("Ordered word subsequence validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM emoji_keyword WHERE normalized >= 'p' AND normalized < ('p' || char(65535)) AND normalized LIKE '%p%i%g%n%o%s%e%';") < 1) throw new InvalidOperationException("Ordered Emoji subsequence validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM translation_entry WHERE key IN ('braces', 'hug');") < 2) throw new InvalidOperationException("Translation lookup validation failed.");
        foreach (string legacyName in new[] { "suggestions.json", "emoji.json", "translations.json", "translations.ecdict.jsonl", "translations.cc-cedict.jsonl" })
        {
            if (File.Exists(Path.Combine(userDirectory, legacyName))) throw new InvalidOperationException($"Legacy runtime lexicon file remains: {legacyName}");
        }
    }

    private static string PickInput(string userDirectory, string packageDirectory, string name)
    {
        string user = Path.Combine(userDirectory, name);
        return File.Exists(user) ? user : Path.Combine(packageDirectory, name);
    }

    private static void CreateSchema(NativeSqliteConnection database) => database.Execute("""
        CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE words(normalized TEXT PRIMARY KEY, text TEXT NOT NULL, ordinal INTEGER NOT NULL);
        CREATE INDEX words_prefix ON words(normalized, ordinal);
        CREATE TABLE suggestions(trigger TEXT NOT NULL, kind INTEGER NOT NULL, candidate TEXT NOT NULL, ordinal INTEGER NOT NULL, priority INTEGER NOT NULL, PRIMARY KEY(trigger, kind, candidate));
        CREATE INDEX suggestions_trigger ON suggestions(trigger, priority DESC, ordinal);
        CREATE TABLE emoji(emoji TEXT PRIMARY KEY, priority INTEGER NOT NULL);
        CREATE TABLE emoji_keyword(emoji TEXT NOT NULL REFERENCES emoji(emoji), normalized TEXT NOT NULL, keyword TEXT NOT NULL, PRIMARY KEY(emoji, normalized));
        CREATE INDEX emoji_keyword_prefix ON emoji_keyword(normalized);
        CREATE TABLE translation_entry(key TEXT NOT NULL, source TEXT NOT NULL, rank INTEGER NOT NULL, text TEXT NOT NULL, PRIMARY KEY(key, source));
        CREATE TABLE translation_part(key TEXT NOT NULL, source TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(key, source, ordinal));
        CREATE TABLE translation_meaning(key TEXT NOT NULL, source TEXT NOT NULL, language TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(key, source, language, ordinal));
        CREATE TABLE translation_example(key TEXT NOT NULL, source TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(key, source, ordinal));
        CREATE INDEX translation_lookup ON translation_entry(key, rank);
        """);

    private static void ImportWords(NativeSqliteConnection database, string path)
    {
        using NativeSqliteStatement insert = database.Prepare("INSERT OR REPLACE INTO words(normalized, text, ordinal) VALUES(?, ?, ?);");
        int ordinal = 0;
        foreach (string raw in File.ReadLines(path))
        {
            string text = raw.Trim();
            if (text.Length == 0) continue;
            insert.BindText(1, text.ToLowerInvariant()); insert.BindText(2, text); insert.BindInt(3, ordinal++); insert.Execute();
        }
    }

    private static void ImportSuggestions(NativeSqliteConnection database, string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array) return;
        using NativeSqliteStatement insert = database.Prepare("INSERT OR REPLACE INTO suggestions(trigger, kind, candidate, ordinal, priority) VALUES(?, ?, ?, ?, ?);");
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("text", out JsonElement triggerValue)) continue;
            string? trigger = triggerValue.GetString();
            if (string.IsNullOrWhiteSpace(trigger)) continue;
            int priority = entry.TryGetProperty("priority", out JsonElement priorityValue) && priorityValue.TryGetInt32(out int parsedPriority) ? parsedPriority : 0;
            ImportSuggestionValues(entry, "next", 0, trigger, priority, insert);
            ImportSuggestionValues(entry, "phrases", 1, trigger, priority, insert);
        }
    }

    private static void ImportSuggestionValues(JsonElement entry, string name, int kind, string trigger, int priority, NativeSqliteStatement insert)
    {
        if (!entry.TryGetProperty(name, out JsonElement values) || values.ValueKind != JsonValueKind.Array) return;
        int ordinal = 0;
        foreach (JsonElement value in values.EnumerateArray())
        {
            string? candidate = value.GetString();
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            insert.BindText(1, trigger.ToLowerInvariant()); insert.BindInt(2, kind); insert.BindText(3, candidate); insert.BindInt(4, ordinal++); insert.BindInt(5, priority); insert.Execute();
        }
    }

    private static void ImportEmoji(NativeSqliteConnection database, string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array) return;
        using NativeSqliteStatement insertEmoji = database.Prepare("INSERT OR REPLACE INTO emoji(emoji, priority) VALUES(?, ?);");
        using NativeSqliteStatement insertKeyword = database.Prepare("INSERT OR REPLACE INTO emoji_keyword(emoji, normalized, keyword) VALUES(?, ?, ?);");
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("emoji", out JsonElement emojiValue)) continue;
            string? emoji = emojiValue.GetString();
            if (string.IsNullOrWhiteSpace(emoji) || !entry.TryGetProperty("keywords", out JsonElement keywords) || keywords.ValueKind != JsonValueKind.Array) continue;
            int priority = entry.TryGetProperty("priority", out JsonElement priorityValue) && priorityValue.TryGetInt32(out int parsedPriority) ? parsedPriority : 0;
            insertEmoji.BindText(1, emoji); insertEmoji.BindInt(2, priority); insertEmoji.Execute();
            foreach (JsonElement keywordValue in keywords.EnumerateArray())
            {
                string? keyword = keywordValue.GetString();
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                insertKeyword.BindText(1, emoji); insertKeyword.BindText(2, keyword.ToLowerInvariant()); insertKeyword.BindText(3, keyword); insertKeyword.Execute();
            }
        }
    }

    private static void ImportTranslationLines(NativeSqliteConnection database, string path, int rank)
    {
        if (!File.Exists(path)) return;
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0) continue;
            using JsonDocument document = JsonDocument.Parse(line);
            ImportTranslation(database, document.RootElement, rank);
        }
    }

    private static void ImportTranslations(NativeSqliteConnection database, string path, int rank)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Array) return;
        foreach (JsonElement entry in entries.EnumerateArray()) ImportTranslation(database, entry, rank);
    }

    private static void ImportTranslation(NativeSqliteConnection database, JsonElement entry, int rank)
    {
        if (!entry.TryGetProperty("text", out JsonElement textValue)) return;
        string? text = textValue.GetString();
        if (string.IsNullOrWhiteSpace(text)) return;
        string source = entry.TryGetProperty("source", out JsonElement sourceValue) ? sourceValue.GetString() ?? $"import-{rank}" : $"import-{rank}";
        string key = text.ToLowerInvariant();
        using NativeSqliteStatement insertEntry = database.Prepare("INSERT OR REPLACE INTO translation_entry(key, source, rank, text) VALUES(?, ?, ?, ?);");
        insertEntry.BindText(1, key); insertEntry.BindText(2, source); insertEntry.BindInt(3, rank); insertEntry.BindText(4, text); insertEntry.Execute();
        ImportTranslationArray(database, entry, "partOfSpeech", key, source, "translation_part", null);
        if (entry.TryGetProperty("translations", out JsonElement translations) && translations.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty language in translations.EnumerateObject()) ImportTranslationArray(database, language.Value, null, key, source, "translation_meaning", language.Name);
        }
        if (entry.TryGetProperty("examples", out JsonElement examples) && examples.ValueKind == JsonValueKind.Array)
        {
            int ordinal = 0;
            using NativeSqliteStatement insert = database.Prepare("INSERT OR REPLACE INTO translation_example(key, source, ordinal, value) VALUES(?, ?, ?, ?);");
            foreach (JsonElement example in examples.EnumerateArray())
            {
                if (!example.TryGetProperty("text", out JsonElement value)) continue;
                string? exampleText = value.GetString(); if (string.IsNullOrWhiteSpace(exampleText)) continue;
                insert.BindText(1, key); insert.BindText(2, source); insert.BindInt(3, ordinal++); insert.BindText(4, exampleText); insert.Execute();
            }
        }
    }

    private static void ImportTranslationArray(NativeSqliteConnection database, JsonElement owner, string? property, string key, string source, string table, string? language)
    {
        JsonElement values = property is null ? owner : owner.TryGetProperty(property, out JsonElement propertyValue) ? propertyValue : default;
        if (values.ValueKind != JsonValueKind.Array) return;
        string sql = language is null ? $"INSERT OR REPLACE INTO {table}(key, source, ordinal, value) VALUES(?, ?, ?, ?);" : $"INSERT OR REPLACE INTO {table}(key, source, language, ordinal, value) VALUES(?, ?, ?, ?, ?);";
        using NativeSqliteStatement insert = database.Prepare(sql);
        int ordinal = 0;
        foreach (JsonElement value in values.EnumerateArray())
        {
            string? text = value.GetString(); if (string.IsNullOrWhiteSpace(text)) continue;
            insert.BindText(1, key); insert.BindText(2, source);
            if (language is not null) { insert.BindText(3, language); insert.BindInt(4, ordinal++); insert.BindText(5, text); }
            else { insert.BindInt(3, ordinal++); insert.BindText(4, text); }
            insert.Execute();
        }
    }

    private static void DeleteLegacyJson(string directory)
    {
        bool importedEcdict = File.Exists(Path.Combine(directory, "translations.ecdict.jsonl"));
        bool importedCcCedict = File.Exists(Path.Combine(directory, "translations.cc-cedict.jsonl"));
        foreach (string name in new[] { "suggestions.json", "emoji.json", "translations.json", "translations.ecdict.jsonl", "translations.cc-cedict.jsonl" })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) File.Delete(path);
        }
        if (importedEcdict) File.WriteAllText(Path.Combine(directory, "enput.db.ecdict.ready"), SchemaVersion.ToString());
        if (importedCcCedict) File.WriteAllText(Path.Combine(directory, "enput.db.cc-cedict.ready"), SchemaVersion.ToString());
    }
}
