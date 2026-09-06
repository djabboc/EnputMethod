using System.Text.Json;
using System.IO;

namespace EnputMethod.Installer;

internal static class LexiconDatabaseBuilder
{
    internal const int SchemaVersion = 4;
    private const string BuiltinPhraseVersion = "wordnet-3.1-20260829";
    private const string ModernLexiconVersion = "modern-terms-20260906.1";
    private const string ModernLexiconSource = "enput-modern-20260906.1";
    private const string LexemeVersion = "lexeme-identity-20260906.2";
    private const string LexemeSource = "enput-curated-20260906.1";
    private const string LexemeLicense = "Product-curated supplement; authority-source migration pending.";
    private const string CaseLexiconVersion = "case-terms-20260906.1";
    private const string CaseLexiconSource = "enput-case-20260905";
    private static readonly string[] CoreAcademicPhrases =
    [
        "machine learning", "deep learning", "artificial intelligence", "data science", "computer vision", "natural language processing", "software engineering", "distributed systems", "cloud computing", "database management", "operating system", "computer network", "information security", "cyber security", "human computer interaction",
        "behavioral economics", "game theory", "financial market", "monetary policy", "fiscal policy", "supply chain", "market research", "business strategy", "corporate governance", "risk management", "cost benefit analysis", "gross domestic product", "interest rate", "exchange rate", "venture capital",
        "social psychology", "clinical psychology", "cognitive neuroscience", "mental health", "personality trait", "decision making", "emotional intelligence", "cognitive behavioral therapy", "developmental psychology", "research methodology",
        "electrical engineering", "mechanical engineering", "chemical engineering", "materials science", "systems engineering", "control system", "signal processing", "structural engineering", "renewable energy", "quality assurance",
        "constitutional law", "criminal law", "civil law", "contract law", "intellectual property", "due process", "legal liability", "court order", "burden of proof", "law enforcement",
        "new york", "los angeles", "san francisco", "washington dc", "united states", "united kingdom", "european union", "south korea", "hong kong", "silicon valley"
    ];
    private static readonly string[] ModernWords = ["Spiderman", "AT&T", "R&B", "ChatGPT", "OpenAI", "TikTok", "GitHub", "Discord", "K-pop", "meme", "rizz", "stan", "slay", "doomscrolling", "deepfake", "livestream", "vlog", "cosplay", "e-sports"];
    private static readonly string[] ModernPhrases = ["The White House", "Donald Trump", "Washington DC", "New York", "Monte Carlo", "Taylor Swift", "Michael Jackson"];
    private sealed record CaseWord(string Text, bool CanonicalCaseRequired);
    private static readonly CaseWord[] CaseWords =
    [
        new("AT&T", true), new("R&B", true), new("ChatGPT", true), new("OpenAI", true), new("TikTok", true), new("GitHub", true), new("Discord", true), new("K-pop", true),
        new("Chicago", true), new("Manhattan", true), new("polish", false), new("Polish", true)
    ];
    private sealed record ModernTranslation(string Key, string Text, string[] Parts, string[] ChineseMeanings, string? Id = null);
    private static readonly ModernTranslation[] ModernTranslations =
    [
        new("spiderman", "Spider-Man", ["proper noun"], ["蜘蛛侠（漫威超级英雄角色）"]),
        new("bars", "bars", ["noun; informal, rap"], ["酒吧；（说唱/网络语境）歌词、押韵或说唱水平，常指“歌词很强”"]),
        new("the white house", "The White House", ["proper noun"], ["白宫（美国总统官邸与行政办公地）"]),
        new("washington dc", "Washington, D.C.", ["proper noun"], ["华盛顿哥伦比亚特区（美国首都）"]),
        new("donald trump", "Donald Trump", ["proper noun"], ["唐纳德·特朗普（美国政治人物）"]),
        new("at&t", "AT&T", ["proper noun"], ["美国电话电报公司（美国电信企业）"]),
        new("r&b", "R&B", ["noun"], ["节奏布鲁斯（Rhythm and Blues）音乐风格"]),
        new("chatgpt", "ChatGPT", ["proper noun"], ["OpenAI 开发的生成式人工智能对话服务"]),
        new("openai", "OpenAI", ["proper noun"], ["人工智能研究与产品公司"]),
        new("tiktok", "TikTok", ["proper noun"], ["短视频社交平台"]),
        new("github", "GitHub", ["proper noun"], ["代码托管与协作平台"]),
        new("discord", "Discord", ["proper noun"], ["社区聊天与语音平台"]),
        new("k-pop", "K-pop", ["noun"], ["韩国流行音乐"]),
        new("chicago", "Chicago", ["proper noun"], ["芝加哥（美国伊利诺伊州城市）"]),
        new("manhattan", "Manhattan", ["proper noun"], ["曼哈顿（纽约市行政区）"]),
        new("new york", "New York", ["proper noun"], ["纽约（美国城市与州名）"]),
        new("monte carlo", "Monte Carlo", ["proper noun"], ["蒙特卡洛（摩纳哥地区）"]),
        new("taylor swift", "Taylor Swift", ["proper noun"], ["泰勒·斯威夫特（美国歌手、词曲作者）"]),
        new("michael jackson", "Michael Jackson", ["proper noun"], ["迈克尔·杰克逊（美国歌手、舞者）"]),
        new("polish", "polish", ["verb/noun"], ["擦亮；润色；光泽剂"], "enput:lexeme:polish:common"),
        new("polish", "Polish", ["adjective/noun"], ["波兰的；波兰人；波兰语"], "enput:lexeme:polish:language"),
        new("meme", "meme", ["noun; internet"], ["网络模因；在网络中传播、模仿和再创作的内容"]),
        new("rizz", "rizz", ["noun; slang"], ["魅力、撩人能力（网络俚语）"]),
        new("stan", "stan", ["noun/verb; internet"], ["狂热粉丝；狂热追随（网络用语）"]),
        new("slay", "slay", ["verb; slang"], ["表现惊艳、做得极好（网络俚语）"]),
        new("doomscrolling", "doomscrolling", ["noun; internet"], ["刷看大量负面新闻或内容而难以停止"]),
        new("deepfake", "deepfake", ["noun"], ["利用深度学习生成或篡改的逼真音视频"]),
        new("livestream", "livestream", ["noun/verb"], ["直播；进行直播"]),
        new("vlog", "vlog", ["noun"], ["视频博客"]),
        new("cosplay", "cosplay", ["noun/verb"], ["角色扮演；进行角色扮演"]),
        new("e-sports", "e-sports", ["noun"], ["电子竞技"])
    ];

    internal static void CreateOrMigrate(string userDirectory, string packageDirectory)
    {
        string databasePath = Path.Combine(userDirectory, "enput.db");
        string readyMarker = Path.Combine(userDirectory, "enput.db.ready");
        if (File.Exists(databasePath) && File.Exists(readyMarker))
        {
            using var existing = new NativeSqliteConnection(databasePath);
            MigrateSchema(existing);
            EnsureBuiltInCandidates(existing, packageDirectory);
            return;
        }
        if (File.Exists(databasePath))
        {
            // Upgrades preserve an already valid static database. Earlier builds could
            // omit the marker while leaving a complete database in Program Files.
            using (var existing = new NativeSqliteConnection(databasePath))
            {
                MigrateSchema(existing);
                EnsureBuiltInCandidates(existing, packageDirectory);
            }
            ValidateDatabase(databasePath);
            File.WriteAllText(readyMarker, SchemaVersion.ToString());
            return;
        }

        // A failed legacy installer may leave enput.db.pending behind. Each current
        // installation uses an isolated staging name so it cannot race with that file.
        string pending = databasePath + "." + Guid.NewGuid().ToString("N") + ".pending";
        string seed = Path.Combine(packageDirectory, "enput.seed.db");
        if (!HasLegacyLexicon(userDirectory) && File.Exists(seed))
        {
            File.Copy(seed, pending, true);
            using (var database = new NativeSqliteConnection(pending)) { MigrateSchema(database); EnsureBuiltInCandidates(database, packageDirectory); }
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
                EnsureBuiltInCandidates(database, packageDirectory);
                database.Execute($"INSERT INTO metadata(key, value) VALUES('schemaVersion', '{SchemaVersion}') ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
                database.Execute("COMMIT;");
            }
            catch
            {
                database.Execute("ROLLBACK;");
                throw;
            }
            if (database.ScalarInt("SELECT COUNT(*) FROM words;") < 100 || database.ScalarInt("SELECT COUNT(*) FROM word_case_variant;") < 2 || database.ScalarInt("SELECT COUNT(*) FROM emoji;") < 100) throw new InvalidOperationException("SQLite lexicon validation failed.");
        }
        File.Move(pending, databasePath, true);
        File.WriteAllText(Path.Combine(userDirectory, "enput.db.ready"), SchemaVersion.ToString());
        DeleteLegacyJson(userDirectory);
    }

    internal static bool HasTranslationSource(string userDirectory, string sourcePrefix)
    {
        string databasePath = Path.Combine(userDirectory, "enput.db");
        if (!File.Exists(databasePath)) return false;
        try
        {
            using var database = new NativeSqliteConnection(databasePath);
            return database.ScalarInt($"SELECT COUNT(*) FROM translation_entry WHERE source LIKE '{sourcePrefix}';") > 0;
        }
        catch (Exception)
        {
            return false;
        }
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
        if (database.ScalarInt("SELECT COUNT(*) FROM words WHERE normalized >= 'he' AND normalized < 'he' || char(65535);") < 3 || database.ScalarInt("SELECT COUNT(*) FROM word_case_variant WHERE normalized IN ('at&t', 'r&b', 'polish');") < 4 || database.ScalarInt("SELECT COUNT(*) FROM word_case_variant WHERE text = 'Polish' COLLATE BINARY AND canonical_case_required = 1;") != 1 || database.ScalarInt("SELECT COUNT(*) FROM word_case_variant WHERE text = 'polish' COLLATE BINARY AND canonical_case_required = 0;") != 1) throw new InvalidOperationException("Word prefix lookup validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM suggestions WHERE trigger = 'can' AND candidate = 'can i help you?';") != 1) throw new InvalidOperationException("Phrase suggestion validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM suggestions WHERE trigger = 'empire' AND candidate = 'empire state building';") != 1) throw new InvalidOperationException("Built-in compact phrase validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM suggestions WHERE candidate IN ('new york', 'computer science', 'machine learning', 'contract law');") < 4) throw new InvalidOperationException("Bundled domain phrase validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM emoji_keyword WHERE normalized = 'fire';") < 1 || database.ScalarInt("SELECT COUNT(*) FROM emoji_keyword WHERE normalized = 'saw';") < 1) throw new InvalidOperationException("Emoji lookup validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM words WHERE normalized >= 'h' AND normalized < ('h' || char(65535)) AND normalized LIKE '%h%p%y%';") < 1) throw new InvalidOperationException("Ordered word subsequence validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM emoji_keyword WHERE normalized = 'pig_nose';") != 1) throw new InvalidOperationException("Ordered Emoji subsequence validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM translation_entry WHERE key IN ('braces', 'hug');") < 2) throw new InvalidOperationException("Legacy translation lookup validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM lexeme WHERE text IN ('Spider-Man', 'bars', 'Washington, D.C.', 'AT&T', 'R&B', 'ChatGPT', 'meme', 'Taylor Swift', 'Michael Jackson');") < 9) throw new InvalidOperationException("Lexeme lookup validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_meaning m JOIN lexeme l ON l.id = m.lexeme_id WHERE l.text = 'Spider-Man' COLLATE BINARY AND m.language = 'zh-CN' AND m.value LIKE '%蜘蛛侠%';") < 1) throw new InvalidOperationException("Modern translation validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM lexeme WHERE normalized = 'polish' AND text IN ('polish', 'Polish');") != 2) throw new InvalidOperationException("Case-sensitive lexeme identity validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM lexeme WHERE id IN ('enput:lexeme:polish:common', 'enput:lexeme:polish:language');") != 2) throw new InvalidOperationException("Stable lexeme identifier validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_meaning m JOIN lexeme l ON l.id = m.lexeme_id WHERE l.text = 'polish' COLLATE BINARY AND m.value LIKE '%擦亮%';") < 1 || database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_meaning m JOIN lexeme l ON l.id = m.lexeme_id WHERE l.text = 'polish' COLLATE BINARY AND m.value LIKE '%波兰语%';") != 0 || database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_meaning m JOIN lexeme l ON l.id = m.lexeme_id WHERE l.text = 'Polish' COLLATE BINARY AND m.value LIKE '%波兰语%';") < 1 || database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_meaning m JOIN lexeme l ON l.id = m.lexeme_id WHERE l.text = 'Polish' COLLATE BINARY AND m.value LIKE '%擦亮%';") != 0) throw new InvalidOperationException("Lexeme sense isolation validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM suggestions WHERE candidate IN ('The White House', 'Donald Trump', 'Washington DC', 'New York', 'Monte Carlo', 'Taylor Swift', 'Michael Jackson');") < 7) throw new InvalidOperationException("Modern phrase validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM word_case_variant WHERE text IN ('AT&T', 'R&B', 'Chicago', 'Manhattan', 'polish', 'Polish');") < 6) throw new InvalidOperationException("Modern word validation failed.");
        if (database.ScalarInt("SELECT COUNT(*) FROM suggestions WHERE kind = 1 AND candidate COLLATE NOCASE >= 'donald' AND candidate COLLATE NOCASE < ('donald' || char(65535)) AND candidate = 'Donald Trump';") != 1) throw new InvalidOperationException("Case-insensitive modern phrase lookup validation failed.");
        foreach (string legacyName in new[] { "suggestions.json", "emoji.json", "translations.json", "translations.ecdict.jsonl", "translations.cc-cedict.jsonl" })
        {
            if (File.Exists(Path.Combine(userDirectory, legacyName))) throw new InvalidOperationException($"Legacy runtime lexicon file remains: {legacyName}");
        }
    }

    internal static string AuditInstalledDatabase(string userDirectory, string outputPath)
    {
        string databasePath = Path.Combine(userDirectory, "enput.db");
        if (!File.Exists(databasePath) || !File.Exists(Path.Combine(userDirectory, "enput.db.ready"))) throw new InvalidOperationException("SQLite lexicon is not ready.");
        using var database = new NativeSqliteConnection(databasePath);

        // Each aggregate is a full-table assertion. The report deliberately records
        // missing legacy metadata rather than inferring a category from spelling.
        var report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            databasePath,
            integrityCheck = database.ScalarText("PRAGMA integrity_check;"),
            schemaVersion = database.ScalarInt("SELECT CAST(value AS INTEGER) FROM metadata WHERE key = 'schemaVersion';"),
            tableCounts = new
            {
                words = database.ScalarInt("SELECT COUNT(*) FROM words;"),
                wordCaseVariants = database.ScalarInt("SELECT COUNT(*) FROM word_case_variant;"),
                suggestions = database.ScalarInt("SELECT COUNT(*) FROM suggestions;"),
                emoji = database.ScalarInt("SELECT COUNT(*) FROM emoji;"),
                emojiKeywords = database.ScalarInt("SELECT COUNT(*) FROM emoji_keyword;"),
                translationEntries = database.ScalarInt("SELECT COUNT(*) FROM translation_entry;"),
                translationMeanings = database.ScalarInt("SELECT COUNT(*) FROM translation_meaning;"),
                translationParts = database.ScalarInt("SELECT COUNT(*) FROM translation_part;"),
                translationExamples = database.ScalarInt("SELECT COUNT(*) FROM translation_example;"),
                lexemes = database.ScalarInt("SELECT COUNT(*) FROM lexeme;"),
                lexemeTranslationEntries = database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_entry;"),
                lexemeTranslationMeanings = database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_meaning;"),
                lexemeTranslationParts = database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_part;"),
                lexemeTranslationExamples = database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_example;")
            },
            fullScanFindings = new
            {
                invalidWordRows = database.ScalarInt("SELECT COUNT(*) FROM words WHERE trim(normalized) = '' OR trim(text) = '';"),
                wordNormalizedMismatches = database.ScalarInt("SELECT COUNT(*) FROM words WHERE lower(text) <> normalized;"),
                invalidCaseVariantRows = database.ScalarInt("SELECT COUNT(*) FROM word_case_variant WHERE trim(normalized) = '' OR trim(text) = '' OR trim(source) = '';"),
                caseVariantNormalizedMismatches = database.ScalarInt("SELECT COUNT(*) FROM word_case_variant WHERE lower(text) <> normalized;"),
                canonicalCaseRequiredVariants = database.ScalarInt("SELECT COUNT(*) FROM word_case_variant WHERE canonical_case_required <> 0;"),
                ambiguousCaseVariantKeys = database.ScalarInt("SELECT COUNT(*) FROM (SELECT normalized FROM word_case_variant GROUP BY normalized HAVING COUNT(*) > 1);"),
                ambiguousCaseVariantKeySamples = database.QueryTextColumn("SELECT normalized FROM word_case_variant GROUP BY normalized HAVING COUNT(*) > 1 ORDER BY normalized LIMIT 100;"),
                invalidSuggestionRows = database.ScalarInt("SELECT COUNT(*) FROM suggestions WHERE trim(trigger) = '' OR trim(candidate) = '';"),
                invalidTranslationRows = database.ScalarInt("SELECT COUNT(*) FROM translation_entry WHERE trim(key) = '' OR trim(source) = '' OR trim(text) = '';"),
                translationsWithoutMeaning = database.ScalarInt("SELECT COUNT(*) FROM translation_entry e WHERE NOT EXISTS (SELECT 1 FROM translation_meaning m WHERE m.key = e.key AND m.source = e.source);"),
                orphanTranslationParts = database.ScalarInt("SELECT COUNT(*) FROM translation_part p WHERE NOT EXISTS (SELECT 1 FROM translation_entry e WHERE e.key = p.key AND e.source = p.source);"),
                orphanTranslationMeanings = database.ScalarInt("SELECT COUNT(*) FROM translation_meaning m WHERE NOT EXISTS (SELECT 1 FROM translation_entry e WHERE e.key = m.key AND e.source = m.source);"),
                orphanTranslationExamples = database.ScalarInt("SELECT COUNT(*) FROM translation_example x WHERE NOT EXISTS (SELECT 1 FROM translation_entry e WHERE e.key = x.key AND e.source = x.source);"),
                invalidLexemeRows = database.ScalarInt("SELECT COUNT(*) FROM lexeme WHERE trim(id) = '' OR trim(normalized) = '' OR trim(text) = '' OR trim(category) = '' OR trim(source) = '' OR trim(license) = '';"),
                lexemeCanonicalIndexDifferences = database.ScalarInt("SELECT COUNT(*) FROM lexeme WHERE lower(text) <> normalized;"),
                lexemeCanonicalIndexDifferenceSamples = database.QueryTextColumn("SELECT text || ' -> ' || normalized FROM lexeme WHERE lower(text) <> normalized ORDER BY text LIMIT 100;"),
                ambiguousLexemeNormalizedKeys = database.ScalarInt("SELECT COUNT(*) FROM (SELECT normalized FROM lexeme GROUP BY normalized HAVING COUNT(*) > 1);"),
                ambiguousLexemeNormalizedKeySamples = database.QueryTextColumn("SELECT normalized FROM lexeme GROUP BY normalized HAVING COUNT(*) > 1 ORDER BY normalized LIMIT 100;"),
                lexemesWithoutMeaning = database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_entry e WHERE NOT EXISTS (SELECT 1 FROM lexeme_translation_meaning m WHERE m.lexeme_id = e.lexeme_id AND m.source = e.source);"),
                orphanLexemeTranslationEntries = database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_entry e WHERE NOT EXISTS (SELECT 1 FROM lexeme l WHERE l.id = e.lexeme_id);"),
                orphanLexemeTranslationParts = database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_part p WHERE NOT EXISTS (SELECT 1 FROM lexeme_translation_entry e WHERE e.lexeme_id = p.lexeme_id AND e.source = p.source);"),
                orphanLexemeTranslationMeanings = database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_meaning m WHERE NOT EXISTS (SELECT 1 FROM lexeme_translation_entry e WHERE e.lexeme_id = m.lexeme_id AND e.source = m.source);"),
                orphanLexemeTranslationExamples = database.ScalarInt("SELECT COUNT(*) FROM lexeme_translation_example x WHERE NOT EXISTS (SELECT 1 FROM lexeme_translation_entry e WHERE e.lexeme_id = x.lexeme_id AND e.source = x.source);"),
                foreignKeyViolations = database.ScalarInt("SELECT COUNT(*) FROM pragma_foreign_key_check;")
            },
            provenance = new
            {
                metadataRows = database.ScalarInt("SELECT COUNT(*) FROM metadata;"),
                wordRowsWithPerEntrySource = 0,
                suggestionRowsWithPerEntrySource = 0,
                wordRowsWithPerEntryType = 0,
                suggestionRowsWithPerEntryType = 0,
                lexemeRowsWithPerEntrySource = database.ScalarInt("SELECT COUNT(*) FROM lexeme WHERE trim(source) <> '';"),
                lexemeRowsWithPerEntryType = database.ScalarInt("SELECT COUNT(*) FROM lexeme WHERE trim(category) <> '';"),
                lexemeRowsWithPerEntryLicense = database.ScalarInt("SELECT COUNT(*) FROM lexeme WHERE trim(license) <> '';"),
                note = "Legacy words and suggestions tables do not carry per-entry source or semantic type. Lexeme rows carry explicit identity metadata. The audit does not infer proper nouns, meanings, or capitalization from spelling."
            }
        };

        string directory = Path.GetDirectoryName(outputPath) ?? throw new InvalidOperationException("Audit output path has no directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return $"SQLite lexicon audit completed: {outputPath}";
    }

    private static string PickInput(string userDirectory, string packageDirectory, string name)
    {
        string user = Path.Combine(userDirectory, name);
        return File.Exists(user) ? user : Path.Combine(packageDirectory, name);
    }

    private static void EnsureBuiltInCandidates(NativeSqliteConnection database, string packageDirectory)
    {
        database.Execute("INSERT OR IGNORE INTO suggestions(trigger, kind, candidate, ordinal, priority) VALUES('empire', 1, 'empire state building', 0, 100);");
        if (database.ScalarInt($"SELECT COUNT(*) FROM metadata WHERE key = 'builtinPhraseVersion' AND value = '{BuiltinPhraseVersion}';") == 0)
        {
            using NativeSqliteStatement insert = database.Prepare("INSERT OR IGNORE INTO suggestions(trigger, kind, candidate, ordinal, priority) VALUES(?, 1, ?, ?, ?);");
            int ordinal = 1000;
            foreach (string phrase in CoreAcademicPhrases) InsertPhrase(insert, phrase, ordinal++, 50);
            string wordNetPath = Path.Combine(packageDirectory, "wordnet-phrases.txt");
            if (!File.Exists(wordNetPath)) throw new InvalidOperationException("Bundled WordNet phrase source is missing.");
            foreach (string phrase in File.ReadLines(wordNetPath)) InsertPhrase(insert, phrase, ordinal++, 5);
            database.Execute($"INSERT INTO metadata(key, value) VALUES('builtinPhraseVersion', '{BuiltinPhraseVersion}') ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
        }
        EnsureModernLexicon(database);
        EnsureCaseLexicon(database);
    }

    private static void EnsureModernLexicon(NativeSqliteConnection database)
    {
        if (database.ScalarInt($"SELECT COUNT(*) FROM metadata WHERE key = 'modernLexiconVersion' AND value = '{ModernLexiconVersion}';") == 0)
        {
            using (NativeSqliteStatement insertWord = database.Prepare("INSERT OR IGNORE INTO words(normalized, text, ordinal) VALUES(?, ?, ?);"))
            {
                int ordinal = -1000;
                foreach (string word in ModernWords)
                {
                    insertWord.BindText(1, word.ToLowerInvariant()); insertWord.BindText(2, word); insertWord.BindInt(3, ordinal++); insertWord.Execute();
                }
            }
            using (NativeSqliteStatement insertPhrase = database.Prepare("INSERT OR IGNORE INTO suggestions(trigger, kind, candidate, ordinal, priority) VALUES(?, 1, ?, ?, ?);"))
            {
                int ordinal = 0;
                foreach (string phrase in ModernPhrases) InsertPhrase(insertPhrase, phrase, ordinal++, 200, preserveCase: true);
            }
            database.Execute($"INSERT INTO metadata(key, value) VALUES('modernLexiconVersion', '{ModernLexiconVersion}') ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
        }

        EnsureModernLexemes(database);
    }

    private static void EnsureModernLexemes(NativeSqliteConnection database)
    {
        if (database.ScalarInt($"SELECT COUNT(*) FROM metadata WHERE key = 'lexemeVersion' AND value = '{LexemeVersion}';") != 0) return;

        database.Execute("DELETE FROM translation_part WHERE source LIKE 'enput-modern-%'; DELETE FROM translation_meaning WHERE source LIKE 'enput-modern-%'; DELETE FROM translation_example WHERE source LIKE 'enput-modern-%'; DELETE FROM translation_entry WHERE source LIKE 'enput-modern-%';");
        database.Execute("DELETE FROM lexeme_translation_part WHERE source LIKE 'enput-curated-%'; DELETE FROM lexeme_translation_meaning WHERE source LIKE 'enput-curated-%'; DELETE FROM lexeme_translation_example WHERE source LIKE 'enput-curated-%'; DELETE FROM lexeme_translation_entry WHERE source LIKE 'enput-curated-%'; DELETE FROM lexeme WHERE source LIKE 'enput-curated-%';");

        using NativeSqliteStatement insertLexeme = database.Prepare("INSERT INTO lexeme(id, normalized, text, category, source, license, priority) VALUES(?, ?, ?, ?, ?, ?, 300);");
        using NativeSqliteStatement insertEntry = database.Prepare("INSERT INTO lexeme_translation_entry(lexeme_id, source, rank, text) VALUES(?, ?, 0, ?);");
        using NativeSqliteStatement insertPart = database.Prepare("INSERT INTO lexeme_translation_part(lexeme_id, source, ordinal, value) VALUES(?, ?, ?, ?);");
        using NativeSqliteStatement insertMeaning = database.Prepare("INSERT INTO lexeme_translation_meaning(lexeme_id, source, language, ordinal, value) VALUES(?, ?, 'zh-CN', ?, ?);");
        foreach (ModernTranslation translation in ModernTranslations)
        {
            string id = translation.Id ?? $"enput:lexeme:{translation.Key}";
            string category = translation.Parts.Length > 0 ? translation.Parts[0] : "unspecified";
            insertLexeme.BindText(1, id); insertLexeme.BindText(2, translation.Key); insertLexeme.BindText(3, translation.Text); insertLexeme.BindText(4, category); insertLexeme.BindText(5, LexemeSource); insertLexeme.BindText(6, LexemeLicense); insertLexeme.Execute();
            insertEntry.BindText(1, id); insertEntry.BindText(2, LexemeSource); insertEntry.BindText(3, translation.Text); insertEntry.Execute();
            for (int ordinal = 0; ordinal < translation.Parts.Length; ++ordinal)
            {
                insertPart.BindText(1, id); insertPart.BindText(2, LexemeSource); insertPart.BindInt(3, ordinal); insertPart.BindText(4, translation.Parts[ordinal]); insertPart.Execute();
            }
            for (int ordinal = 0; ordinal < translation.ChineseMeanings.Length; ++ordinal)
            {
                insertMeaning.BindText(1, id); insertMeaning.BindText(2, LexemeSource); insertMeaning.BindInt(3, ordinal); insertMeaning.BindText(4, translation.ChineseMeanings[ordinal]); insertMeaning.Execute();
            }
        }
        database.Execute($"INSERT INTO metadata(key, value) VALUES('lexemeVersion', '{LexemeVersion}') ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
    }

    private static void EnsureCaseLexicon(NativeSqliteConnection database)
    {
        if (database.ScalarInt($"SELECT COUNT(*) FROM metadata WHERE key = 'caseLexiconVersion' AND value = '{CaseLexiconVersion}';") != 0) return;
        database.Execute($"DELETE FROM word_case_variant WHERE source LIKE '{CaseLexiconSource}%';");
        using NativeSqliteStatement insert = database.Prepare("INSERT INTO word_case_variant(normalized, text, ordinal, priority, source, canonical_case_required) VALUES(?, ?, ?, 300, ?, ?);");
        int ordinal = 0;
        foreach (CaseWord word in CaseWords)
        {
            insert.BindText(1, word.Text.ToLowerInvariant()); insert.BindText(2, word.Text); insert.BindInt(3, ordinal++); insert.BindText(4, CaseLexiconSource); insert.BindInt(5, word.CanonicalCaseRequired ? 1 : 0); insert.Execute();
        }
        database.Execute($"INSERT INTO metadata(key, value) VALUES('caseLexiconVersion', '{CaseLexiconVersion}') ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
    }

    private static void InsertPhrase(NativeSqliteStatement insert, string rawPhrase, int ordinal, int priority, bool preserveCase = false)
    {
        string phrase = rawPhrase.Trim();
        if (!preserveCase) phrase = phrase.ToLowerInvariant();
        int separator = phrase.IndexOf(' ');
        if (separator is <= 0 || separator == phrase.Length - 1 || phrase.Length > 120) return;
        insert.BindText(1, phrase[..separator].ToLowerInvariant()); insert.BindText(2, phrase); insert.BindInt(3, ordinal); insert.BindInt(4, priority); insert.Execute();
    }

    private static void CreateSchema(NativeSqliteConnection database) => database.Execute("""
        CREATE TABLE metadata(key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE words(normalized TEXT PRIMARY KEY, text TEXT NOT NULL, ordinal INTEGER NOT NULL);
        CREATE INDEX words_prefix ON words(normalized, ordinal);
        CREATE TABLE word_case_variant(normalized TEXT NOT NULL, text TEXT NOT NULL, ordinal INTEGER NOT NULL, priority INTEGER NOT NULL, source TEXT NOT NULL, canonical_case_required INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(normalized, text));
        CREATE INDEX word_case_variant_prefix ON word_case_variant(normalized, priority DESC, ordinal);
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
        CREATE TABLE lexeme(id TEXT PRIMARY KEY, normalized TEXT NOT NULL, text TEXT NOT NULL COLLATE BINARY, category TEXT NOT NULL, source TEXT NOT NULL, license TEXT NOT NULL, priority INTEGER NOT NULL);
        CREATE INDEX lexeme_exact ON lexeme(text, priority DESC);
        CREATE INDEX lexeme_normalized ON lexeme(normalized, priority DESC);
        CREATE TABLE lexeme_translation_entry(lexeme_id TEXT NOT NULL REFERENCES lexeme(id), source TEXT NOT NULL, rank INTEGER NOT NULL, text TEXT NOT NULL, PRIMARY KEY(lexeme_id, source));
        CREATE TABLE lexeme_translation_part(lexeme_id TEXT NOT NULL, source TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(lexeme_id, source, ordinal));
        CREATE TABLE lexeme_translation_meaning(lexeme_id TEXT NOT NULL, source TEXT NOT NULL, language TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(lexeme_id, source, language, ordinal));
        CREATE TABLE lexeme_translation_example(lexeme_id TEXT NOT NULL, source TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(lexeme_id, source, ordinal));
        """);

    private static void MigrateSchema(NativeSqliteConnection database)
    {
        database.Execute("CREATE TABLE IF NOT EXISTS word_case_variant(normalized TEXT NOT NULL, text TEXT NOT NULL, ordinal INTEGER NOT NULL, priority INTEGER NOT NULL, source TEXT NOT NULL, PRIMARY KEY(normalized, text)); CREATE INDEX IF NOT EXISTS word_case_variant_prefix ON word_case_variant(normalized, priority DESC, ordinal); CREATE TABLE IF NOT EXISTS lexeme(id TEXT PRIMARY KEY, normalized TEXT NOT NULL, text TEXT NOT NULL COLLATE BINARY, category TEXT NOT NULL, source TEXT NOT NULL, license TEXT NOT NULL, priority INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS lexeme_exact ON lexeme(text, priority DESC); CREATE INDEX IF NOT EXISTS lexeme_normalized ON lexeme(normalized, priority DESC); CREATE TABLE IF NOT EXISTS lexeme_translation_entry(lexeme_id TEXT NOT NULL REFERENCES lexeme(id), source TEXT NOT NULL, rank INTEGER NOT NULL, text TEXT NOT NULL, PRIMARY KEY(lexeme_id, source)); CREATE TABLE IF NOT EXISTS lexeme_translation_part(lexeme_id TEXT NOT NULL, source TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(lexeme_id, source, ordinal)); CREATE TABLE IF NOT EXISTS lexeme_translation_meaning(lexeme_id TEXT NOT NULL, source TEXT NOT NULL, language TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(lexeme_id, source, language, ordinal)); CREATE TABLE IF NOT EXISTS lexeme_translation_example(lexeme_id TEXT NOT NULL, source TEXT NOT NULL, ordinal INTEGER NOT NULL, value TEXT NOT NULL, PRIMARY KEY(lexeme_id, source, ordinal));");
        if (database.QueryTextColumn("SELECT name FROM pragma_table_info('word_case_variant') WHERE name = 'canonical_case_required';").Count == 0) database.Execute("ALTER TABLE word_case_variant ADD COLUMN canonical_case_required INTEGER NOT NULL DEFAULT 0;");
        database.Execute($"INSERT INTO metadata(key, value) VALUES('schemaVersion', '{SchemaVersion}') ON CONFLICT(key) DO UPDATE SET value = excluded.value;");
    }

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
