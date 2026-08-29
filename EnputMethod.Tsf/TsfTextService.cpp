#include <windows.h>
#include <d2d1_3.h>
#include <dwrite.h>
#include <textstor.h>
#include <msctf.h>
#include "CandidateRanking.h"
#include "CandidateSelection.h"
#include "JsonObjectReader.h"
#include "OverlayClient.h"
#include "OverlayDiagnostics.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cwctype>
#include <filesystem>
#include <fstream>
#include <functional>
#include <iterator>
#include <limits>
#include <memory>
#include <new>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#pragma comment(lib, "d2d1.lib")
#pragma comment(lib, "dwrite.lib")

// A system-registered, transparent TSF keyboard profile.  It exists as a real input
// method in Windows, while leaving normal English keystrokes to the app.
namespace {
constexpr CLSID kTextServiceClsid = { 0x9c8945d5, 0x01df, 0x48f4, { 0xa8, 0xdb, 0x57, 0xe8, 0xb6, 0xa1, 0xeb, 0x10 } };
constexpr GUID kProfileGuid = { 0x55f31085, 0xe7cd, 0x4886, { 0xbb, 0x80, 0x1d, 0x61, 0xce, 0x39, 0x21, 0x07 } };
constexpr LANGID kChineseSimplified = MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED);
constexpr LANGID kLegacyEnglishUs = MAKELANGID(LANG_ENGLISH, SUBLANG_ENGLISH_US);
HMODULE g_module = nullptr;
long g_objectCount = 0;
long g_lockCount = 0;

std::wstring UserDataDirectory() {
    wchar_t localAppData[MAX_PATH]{};
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, ARRAYSIZE(localAppData))) return {};
    return std::wstring(localAppData) + L"\\Enput Method";
}

std::string ReadUtf8File(const std::wstring& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) return {};
    return { std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>() };
}

std::wstring Utf8ToWide(const std::string& text) {
    if (text.empty()) return {};
    const int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0);
    if (!length) return {};
    std::wstring result(static_cast<size_t>(length), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), result.data(), length);
    return result;
}

std::string WideToUtf8(const std::wstring& text) {
    if (text.empty()) return {};
    const int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    if (!length) return {};
    std::string result(static_cast<size_t>(length), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), result.data(), length, nullptr, nullptr);
    return result;
}

std::string JsonString(const std::wstring& text) {
    const std::string utf8 = WideToUtf8(text);
    std::string result = "\"";
    for (const unsigned char character : utf8) {
        switch (character) {
        case '\\': result += "\\\\"; break;
        case '\"': result += "\\\""; break;
        case '\b': result += "\\b"; break;
        case '\f': result += "\\f"; break;
        case '\n': result += "\\n"; break;
        case '\r': result += "\\r"; break;
        case '\t': result += "\\t"; break;
        default:
            if (character < 0x20) {
                constexpr char hex[] = "0123456789abcdef";
                result += "\\u00";
                result += hex[(character >> 4) & 0x0f];
                result += hex[character & 0x0f];
            } else {
                result += static_cast<char>(character);
            }
        }
    }
    result += '\"';
    return result;
}

std::wstring OverlayExecutablePath() {
    wchar_t modulePath[MAX_PATH]{};
    if (!g_module || !GetModuleFileNameW(g_module, modulePath, ARRAYSIZE(modulePath))) return {};
    return std::filesystem::path(modulePath).parent_path().append(L"Overlay").append(L"EnputMethod.Overlay.exe").wstring();
}

struct ThemeStyle {
    COLORREF background = RGB(31, 41, 55);
    COLORREF foreground = RGB(243, 244, 246);
    COLORREF border = RGB(75, 85, 99);
    COLORREF selectedBackground = RGB(55, 65, 81);
    COLORREF selectedForeground = RGB(255, 255, 255);
    COLORREF selectedBorder = RGB(96, 165, 250);
    int borderWidth = 1;
    int cornerRadius = 8;
    int padding = 10;
    int rowHeight = 28;
    int shadowSize = 8;
    int selectedBorderWidth = 1;
    int selectedCornerRadius = 5;
    COLORREF translationBackground = RGB(31, 41, 55);
    COLORREF translationForeground = RGB(243, 244, 246);
    COLORREF translationTitleForeground = RGB(255, 255, 255);
    COLORREF translationBorder = RGB(75, 85, 99);
    COLORREF translationScrollbarTrack = RGB(75, 85, 99);
    COLORREF translationScrollbarThumb = RGB(96, 165, 250);
    int translationBorderWidth = 1;
    int translationCornerRadius = 8;
    int translationPadding = 10;
    int translationWidth = 380;
    int translationMaxHeight = 420;
};

struct ShortcutConfiguration {
    std::vector<WPARAM> selectCurrent{ VK_TAB };
    std::vector<WPARAM> previousPage{ VK_OEM_MINUS, VK_SUBTRACT };
    std::vector<WPARAM> nextPage{ VK_OEM_PLUS, VK_ADD };
    std::vector<WPARAM> selectPrevious{ VK_UP };
    std::vector<WPARAM> selectNext{ VK_DOWN };
    std::vector<WPARAM> toggleEmojiMode{ VK_F2 };
    std::vector<WPARAM> toggleTranslationWindow{ VK_F3 };
};

struct RuntimeConfiguration {
    int candidateCount = 9;
    bool horizontal = false;
    bool appendSpaceAfterSelection = true;
    bool adaptiveCandidateRanking = true;
    bool preserveCase = true;
    bool avoidScreenEdges = true;
    std::wstring fontFamily = L"Segoe UI";
    int fontSize = 16;
    BYTE opacity = 255;
    ThemeStyle theme{};
    ShortcutConfiguration shortcuts{};
};

std::wstring ConfigurationPath() {
    const std::wstring directory = UserDataDirectory();
    if (directory.empty()) return {};
    const std::wstring current = directory + L"\\config.json";
    if (GetFileAttributesW(current.c_str()) != INVALID_FILE_ATTRIBUTES) return current;
    return directory + L"\\conf.json"; // Compatibility with releases before config.json.
}

std::wstring ShortcutPath() {
    const std::wstring directory = UserDataDirectory();
    return directory.empty() ? std::wstring{} : directory + L"\\shortcut.json";
}

WPARAM ShortcutKey(const std::string& name) {
    static const std::unordered_map<std::string, WPARAM> keys{
        { "Tab", VK_TAB }, { "Minus", VK_OEM_MINUS }, { "Plus", VK_OEM_PLUS },
        { "NumpadSubtract", VK_SUBTRACT }, { "NumpadAdd", VK_ADD },
        { "Up", VK_UP }, { "Down", VK_DOWN }, { "Space", VK_SPACE },
        { "Enter", VK_RETURN }, { "Escape", VK_ESCAPE }, { "F2", VK_F2 }, { "F3", VK_F3 }
    };
    const auto named = keys.find(name);
    if (named != keys.end()) return named->second;
    if (name.size() == 1 && ((name[0] >= 'A' && name[0] <= 'Z') || (name[0] >= '0' && name[0] <= '9'))) return name[0];
    return 0;
}

std::vector<WPARAM> ShortcutKeys(const enput::json::Object& object, const char* action, const std::vector<WPARAM>& fallback) {
    const std::vector<std::string>* names = enput::json::StringArray(object, action);
    if (!names) return fallback;
    std::vector<WPARAM> keys;
    for (const std::string& name : *names) {
        const WPARAM key = ShortcutKey(name);
        if (!key) return fallback;
        if (std::find(keys.begin(), keys.end(), key) == keys.end()) keys.push_back(key);
    }
    return keys;
}

ShortcutConfiguration LoadShortcutConfiguration() {
    ShortcutConfiguration shortcuts;
    enput::json::Object object;
    if (!enput::json::ReadObject(ReadUtf8File(ShortcutPath()), &object)) return shortcuts;
    shortcuts.selectCurrent = enput::json::StringArray(object, "selectCurrent")
        ? ShortcutKeys(object, "selectCurrent", shortcuts.selectCurrent)
        : ShortcutKeys(object, "selectFirst", shortcuts.selectCurrent);
    shortcuts.previousPage = ShortcutKeys(object, "previousPage", shortcuts.previousPage);
    shortcuts.nextPage = ShortcutKeys(object, "nextPage", shortcuts.nextPage);
    shortcuts.selectPrevious = ShortcutKeys(object, "selectPrevious", shortcuts.selectPrevious);
    shortcuts.selectNext = ShortcutKeys(object, "selectNext", shortcuts.selectNext);
    shortcuts.toggleEmojiMode = ShortcutKeys(object, "toggleEmojiMode", shortcuts.toggleEmojiMode);
    shortcuts.toggleTranslationWindow = ShortcutKeys(object, "toggleTranslationWindow", shortcuts.toggleTranslationWindow);
    return shortcuts;
}

bool HasShortcut(const std::vector<WPARAM>& keys, WPARAM key) {
    return std::find(keys.begin(), keys.end(), key) != keys.end();
}

bool IsSafeThemeName(const std::string& name) {
    if (name.empty() || name.size() > 64) return false;
    return std::all_of(name.begin(), name.end(), [](char character) {
        return (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '-';
    });
}

COLORREF ColorOr(const enput::json::Object& object, const char* key, COLORREF fallback) {
    const std::string text = enput::json::StringOr(object, key, "");
    if (text.size() != 7 || text[0] != '#') return fallback;
    int channels[3]{};
    for (int channel = 0; channel < 3; ++channel) {
        const char high = text[1 + channel * 2];
        const char low = text[2 + channel * 2];
        const auto hexValue = [](char value) -> int {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        };
        const int highValue = hexValue(high);
        const int lowValue = hexValue(low);
        if (highValue < 0 || lowValue < 0) return fallback;
        channels[channel] = highValue * 16 + lowValue;
    }
    return RGB(channels[0], channels[1], channels[2]);
}

ThemeStyle LoadTheme(const std::string& name) {
    ThemeStyle theme;
    const std::wstring directory = UserDataDirectory();
    if (!IsSafeThemeName(name) || directory.empty()) return theme;
    enput::json::Object object;
    if (!enput::json::ReadObject(ReadUtf8File(directory + L"\\themes\\" + Utf8ToWide(name) + L".json"), &object)) return theme;
    theme.background = ColorOr(object, "background", theme.background);
    theme.foreground = ColorOr(object, "foreground", theme.foreground);
    theme.border = ColorOr(object, "border", theme.border);
    theme.selectedBackground = ColorOr(object, "selectedBackground", theme.selectedBackground);
    theme.selectedForeground = ColorOr(object, "selectedForeground", theme.selectedForeground);
    theme.selectedBorder = ColorOr(object, "selectedBorder", theme.selectedBorder);
    theme.borderWidth = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "borderWidth", theme.borderWidth))), 0, 6);
    theme.cornerRadius = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "cornerRadius", theme.cornerRadius))), 0, 32);
    theme.padding = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "padding", theme.padding))), 4, 32);
    theme.rowHeight = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "rowHeight", theme.rowHeight))), 20, 64);
    theme.shadowSize = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "shadowSize", theme.shadowSize))), 0, 24);
    theme.selectedBorderWidth = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "selectedBorderWidth", theme.selectedBorderWidth))), 0, 6);
    theme.selectedCornerRadius = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "selectedCornerRadius", theme.selectedCornerRadius))), 0, 32);
    theme.translationBackground = ColorOr(object, "translationBackground", theme.background);
    theme.translationForeground = ColorOr(object, "translationForeground", theme.foreground);
    theme.translationTitleForeground = ColorOr(object, "translationTitleForeground", theme.selectedForeground);
    theme.translationBorder = ColorOr(object, "translationBorder", theme.border);
    theme.translationScrollbarTrack = ColorOr(object, "translationScrollbarTrack", theme.border);
    theme.translationScrollbarThumb = ColorOr(object, "translationScrollbarThumb", theme.selectedBorder);
    theme.translationBorderWidth = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "translationBorderWidth", theme.borderWidth))), 0, 6);
    theme.translationCornerRadius = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "translationCornerRadius", theme.cornerRadius))), 0, 32);
    theme.translationPadding = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "translationPadding", theme.padding))), 4, 32);
    theme.translationWidth = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "translationWidth", theme.translationWidth))), 220, 720);
    theme.translationMaxHeight = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "translationMaxHeight", theme.translationMaxHeight))), 100, 720);
    return theme;
}

RuntimeConfiguration LoadRuntimeConfiguration() {
    RuntimeConfiguration configuration;
    enput::json::Object object;
    if (enput::json::ReadObject(ReadUtf8File(ConfigurationPath()), &object)) {
        configuration.candidateCount = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "candidateCount", configuration.candidateCount))), 1, 9);
        configuration.horizontal = enput::json::StringOr(object, "layout", "vertical") == "horizontal";
        configuration.appendSpaceAfterSelection = enput::json::BooleanOr(object, "appendSpaceAfterSelection", configuration.appendSpaceAfterSelection);
        configuration.adaptiveCandidateRanking = enput::json::BooleanOr(object, "adaptiveCandidateRanking", configuration.adaptiveCandidateRanking);
        configuration.preserveCase = enput::json::BooleanOr(object, "preserveCase", configuration.preserveCase);
        configuration.avoidScreenEdges = enput::json::BooleanOr(object, "avoidScreenEdges", configuration.avoidScreenEdges);
        configuration.fontFamily = Utf8ToWide(enput::json::StringOr(object, "fontFamily", "Segoe UI"));
        if (configuration.fontFamily.empty()) configuration.fontFamily = L"Segoe UI";
        configuration.fontSize = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "fontSize", configuration.fontSize))), 10, 32);
        const double opacity = std::clamp(enput::json::NumberOr(object, "opacity", 1.0), 0.2, 1.0);
        configuration.opacity = static_cast<BYTE>(std::lround(opacity * 255.0));
        configuration.theme = LoadTheme(enput::json::StringOr(object, "theme", "dark"));
    }
    configuration.shortcuts = LoadShortcutConfiguration();
    return configuration;
}

std::vector<std::wstring> DefaultDictionary() {
    return {
        L"a", L"about", L"above", L"after", L"again", L"all", L"also", L"always", L"and", L"another", L"any", L"are", L"as", L"at",
        L"be", L"because", L"become", L"before", L"between", L"both", L"but", L"by", L"can", L"come", L"could", L"day", L"do", L"down",
        L"each", L"even", L"every", L"example", L"find", L"first", L"for", L"from", L"function", L"get", L"give", L"go", L"good", L"great", L"have",
        L"he", L"health", L"hear", L"heart", L"heavy", L"hello", L"help", L"her", L"here", L"high", L"him", L"his", L"how", L"I", L"if", L"in",
        L"information", L"input", L"into", L"is", L"it", L"its", L"just", L"know", L"language", L"like", L"look", L"make", L"many", L"may", L"me",
        L"method", L"more", L"most", L"my", L"new", L"no", L"not", L"now", L"of", L"on", L"one", L"only", L"or", L"other", L"our", L"out", L"over",
        L"people", L"place", L"please", L"project", L"prototype", L"put", L"really", L"right", L"say", L"see", L"service", L"she", L"should", L"simple", L"so",
        L"some", L"system", L"take", L"than", L"the", L"this", L"that", L"they", L"there", L"their", L"them", L"then", L"these", L"think", L"thing", L"those",
        L"though", L"thought", L"three", L"through", L"thank", L"time", L"to", L"two", L"up", L"use", L"very", L"want", L"way", L"we", L"well", L"what",
        L"when", L"where", L"which", L"who", L"will", L"with", L"would", L"write", L"year", L"you", L"your"
    };
}

const std::vector<std::wstring>& LoadDictionary() {
    struct Cache {
        std::wstring path;
        WIN32_FILE_ATTRIBUTE_DATA attributes{};
        bool hasAttributes = false;
        std::vector<std::wstring> words;
    };
    static Cache cache;
    const std::wstring directory = UserDataDirectory();
    const std::wstring path = directory.empty() ? std::wstring{} : directory + L"\\dictionary.txt";
    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    const bool fileAvailable = !path.empty() && GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &attributes);
    if (cache.path == path && cache.hasAttributes == fileAvailable && !cache.words.empty() &&
        (!fileAvailable || (CompareFileTime(&cache.attributes.ftLastWriteTime, &attributes.ftLastWriteTime) == 0 &&
                            cache.attributes.nFileSizeHigh == attributes.nFileSizeHigh && cache.attributes.nFileSizeLow == attributes.nFileSizeLow))) {
        return cache.words;
    }

    const std::string contents = fileAvailable ? ReadUtf8File(path) : std::string{};
    std::vector<std::wstring> words;
    size_t start = 0;
    while (start < contents.size()) {
        size_t end = contents.find('\n', start);
        if (end == std::string::npos) end = contents.size();
        std::string line = contents.substr(start, end - start);
        if (!line.empty() && line.back() == '\r') line.pop_back();
        const std::wstring word = Utf8ToWide(line);
        if (!word.empty()) words.push_back(word);
        start = end + 1;
    }
    cache.path = path;
    cache.attributes = attributes;
    cache.hasAttributes = fileAvailable;
    cache.words = words.empty() ? DefaultDictionary() : std::move(words);
    return cache.words;
}

std::wstring Lowercase(std::wstring text) {
    std::transform(text.begin(), text.end(), text.begin(), [](wchar_t character) { return static_cast<wchar_t>(towlower(character)); });
    return text;
}

constexpr wchar_t kCandidateFrequencyRegistryKey[] = L"Software\\Enput Method\\CandidateFrequency";

enput::CandidateFrequencyMap& CandidateFrequencies() {
    static enput::CandidateFrequencyMap frequencies = [] {
        enput::CandidateFrequencyMap loaded;
        HKEY key{};
        if (RegOpenKeyExW(HKEY_CURRENT_USER, kCandidateFrequencyRegistryKey, 0, KEY_READ, &key) != ERROR_SUCCESS) return loaded;
        wchar_t valueName[256]{};
        for (DWORD index = 0;; ++index) {
            DWORD characterCount = ARRAYSIZE(valueName);
            DWORD type{};
            DWORD value{};
            DWORD valueSize = sizeof(value);
            const LONG status = RegEnumValueW(key, index, valueName, &characterCount, nullptr, &type, reinterpret_cast<BYTE*>(&value), &valueSize);
            if (status == ERROR_NO_MORE_ITEMS) break;
            if (status != ERROR_SUCCESS || type != REG_DWORD || valueSize != sizeof(value)) continue;
            const std::wstring normalized = enput::CandidateFrequencyKey(valueName);
            if (normalized.empty()) continue;
            loaded[normalized] = static_cast<unsigned int>(std::clamp(value, static_cast<DWORD>(1), static_cast<DWORD>(1000000)));
        }
        RegCloseKey(key);
        return loaded;
    }();
    return frequencies;
}

void RecordCandidateSelection(const std::wstring& candidate) {
    const std::wstring keyName = enput::CandidateFrequencyKey(candidate);
    if (keyName.empty()) return;
    enput::CandidateFrequencyMap& frequencies = CandidateFrequencies();
    if (!frequencies.contains(keyName) && frequencies.size() >= 4096) return;
    unsigned int& frequency = frequencies[keyName];
    if (frequency < 1000000u) ++frequency;
    HKEY registry{};
    if (RegCreateKeyExW(HKEY_CURRENT_USER, kCandidateFrequencyRegistryKey, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &registry, nullptr) != ERROR_SUCCESS) return;
    RegSetValueExW(registry, keyName.c_str(), 0, REG_DWORD, reinterpret_cast<const BYTE*>(&frequency), sizeof(frequency));
    RegCloseKey(registry);
}

struct SuggestionEntry {
    std::wstring text;
    std::vector<std::wstring> next;
    std::vector<std::wstring> phrases;
    int priority = 0;
};

std::wstring SuggestionDictionaryPath() {
    const std::wstring directory = UserDataDirectory();
    return directory.empty() ? std::wstring{} : directory + L"\\suggestions.json";
}

std::vector<std::wstring> JsonStrings(const enput::json::Value* value) {
    std::vector<std::wstring> result;
    if (!value || value->type != enput::json::Value::Type::Array) return result;
    for (const enput::json::Value& item : value->array) {
        if (item.type != enput::json::Value::Type::String) continue;
        const std::wstring text = Utf8ToWide(item.string);
        if (!text.empty()) result.push_back(text);
    }
    return result;
}

const std::vector<SuggestionEntry>& LoadSuggestionDictionary() {
    struct Cache {
        WIN32_FILE_ATTRIBUTE_DATA attributes{};
        bool available = false;
        std::vector<SuggestionEntry> entries;
    };
    static Cache cache;
    const std::wstring path = SuggestionDictionaryPath();
    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    const bool available = !path.empty() && GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &attributes);
    if (cache.available == available && (!available || (CompareFileTime(&cache.attributes.ftLastWriteTime, &attributes.ftLastWriteTime) == 0 &&
        cache.attributes.nFileSizeHigh == attributes.nFileSizeHigh && cache.attributes.nFileSizeLow == attributes.nFileSizeLow))) return cache.entries;

    std::vector<SuggestionEntry> entries;
    enput::json::Value document;
    const std::string contents = available ? ReadUtf8File(path) : std::string{};
    const enput::json::Value* values = enput::json::ReadDocument(contents, &document) ? enput::json::ObjectValue(document, "entries") : nullptr;
    if (values && values->type == enput::json::Value::Type::Array) {
        for (const enput::json::Value& value : values->array) {
            const enput::json::Value* textValue = enput::json::ObjectValue(value, "text");
            if (!textValue || textValue->type != enput::json::Value::Type::String) continue;
            SuggestionEntry entry;
            entry.text = Utf8ToWide(textValue->string);
            if (entry.text.empty()) continue;
            entry.next = JsonStrings(enput::json::ObjectValue(value, "next"));
            entry.phrases = JsonStrings(enput::json::ObjectValue(value, "phrases"));
            const enput::json::Value* priority = enput::json::ObjectValue(value, "priority");
            if (priority && priority->type == enput::json::Value::Type::Number) entry.priority = static_cast<int>(priority->number);
            entries.push_back(std::move(entry));
        }
    }
    std::stable_sort(entries.begin(), entries.end(), [](const SuggestionEntry& left, const SuggestionEntry& right) { return left.priority > right.priority; });
    cache.attributes = attributes;
    cache.available = available;
    cache.entries = std::move(entries);
    return cache.entries;
}

struct EmojiEntry {
    std::wstring emoji;
    std::vector<std::wstring> keywords;
};

std::wstring EmojiDictionaryPath() {
    const std::wstring directory = UserDataDirectory();
    return directory.empty() ? std::wstring{} : directory + L"\\emoji.json";
}

const std::vector<EmojiEntry>& LoadEmojiDictionary() {
    struct Cache {
        WIN32_FILE_ATTRIBUTE_DATA attributes{};
        bool available = false;
        std::vector<EmojiEntry> entries;
    };
    static Cache cache;
    const std::wstring path = EmojiDictionaryPath();
    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    const bool available = !path.empty() && GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &attributes);
    if (cache.available == available && (!available || (CompareFileTime(&cache.attributes.ftLastWriteTime, &attributes.ftLastWriteTime) == 0 &&
        cache.attributes.nFileSizeHigh == attributes.nFileSizeHigh && cache.attributes.nFileSizeLow == attributes.nFileSizeLow))) return cache.entries;

    std::vector<EmojiEntry> entries;
    enput::json::Value document;
    const std::string contents = available ? ReadUtf8File(path) : std::string{};
    const bool parsed = enput::json::ReadDocument(contents, &document);
    const enput::json::Value* values = parsed ? enput::json::ObjectValue(document, "entries") : nullptr;
    if (values && values->type == enput::json::Value::Type::Array) {
        for (const enput::json::Value& value : values->array) {
            const enput::json::Value* emoji = enput::json::ObjectValue(value, "emoji");
            if (!emoji || emoji->type != enput::json::Value::Type::String) continue;
            EmojiEntry entry;
            entry.emoji = Utf8ToWide(emoji->string);
            entry.keywords = JsonStrings(enput::json::ObjectValue(value, "keywords"));
            if (!entry.emoji.empty() && !entry.keywords.empty()) entries.push_back(std::move(entry));
        }
    } else if (parsed && document.type == enput::json::Value::Type::Object) {
        for (const auto& [keyword, emoji] : document.object) {
            if (emoji.type != enput::json::Value::Type::String || keyword.empty()) continue;
            EmojiEntry entry;
            entry.emoji = Utf8ToWide(emoji.string);
            entry.keywords.push_back(Utf8ToWide(keyword));
            if (!entry.emoji.empty() && !entry.keywords.front().empty()) entries.push_back(std::move(entry));
        }
    }
    cache.attributes = attributes;
    cache.available = available;
    cache.entries = std::move(entries);
    return cache.entries;
}

struct TranslationEntry {
    std::wstring text;
    std::vector<std::wstring> partsOfSpeech;
    std::vector<std::pair<std::wstring, std::vector<std::wstring>>> translations;
    std::wstring example;
    std::wstring source;
};

std::wstring TranslationDictionaryPath() {
    const std::wstring directory = UserDataDirectory();
    return directory.empty() ? std::wstring{} : directory + L"\\translations.json";
}

std::wstring FullTranslationDictionaryPath() {
    const std::wstring directory = UserDataDirectory();
    return directory.empty() ? std::wstring{} : directory + L"\\translations.ecdict.jsonl";
}

bool ParseTranslationEntry(const enput::json::Value& value, TranslationEntry* entry) {
    if (!entry) return false;
    const enput::json::Value* text = enput::json::ObjectValue(value, "text");
    if (!text || text->type != enput::json::Value::Type::String) return false;
    TranslationEntry parsed;
    parsed.text = Utf8ToWide(text->string);
    parsed.partsOfSpeech = JsonStrings(enput::json::ObjectValue(value, "partOfSpeech"));
    parsed.source = Utf8ToWide(enput::json::ObjectValue(value, "source") && enput::json::ObjectValue(value, "source")->type == enput::json::Value::Type::String ? enput::json::ObjectValue(value, "source")->string : "");
    const enput::json::Value* translations = enput::json::ObjectValue(value, "translations");
    if (translations && translations->type == enput::json::Value::Type::Object) {
        for (const auto& [language, meanings] : translations->object) parsed.translations.emplace_back(Utf8ToWide(language), JsonStrings(&meanings));
    }
    const enput::json::Value* examples = enput::json::ObjectValue(value, "examples");
    if (examples && examples->type == enput::json::Value::Type::Array && !examples->array.empty()) {
        const enput::json::Value* example = enput::json::ObjectValue(examples->array.front(), "text");
        if (example && example->type == enput::json::Value::Type::String) parsed.example = Utf8ToWide(example->string);
    }
    if (parsed.text.empty()) return false;
    *entry = std::move(parsed);
    return true;
}

const std::vector<TranslationEntry>& LoadTranslationDictionary() {
    static std::vector<TranslationEntry> entries;
    static bool loaded = false;
    if (loaded) return entries;
    loaded = true;
    enput::json::Value document;
    const enput::json::Value* values = enput::json::ReadDocument(ReadUtf8File(TranslationDictionaryPath()), &document) ? enput::json::ObjectValue(document, "entries") : nullptr;
    if (!values || values->type != enput::json::Value::Type::Array) return entries;
    for (const enput::json::Value& value : values->array) {
        TranslationEntry entry;
        if (ParseTranslationEntry(value, &entry)) entries.push_back(std::move(entry));
    }
    return entries;
}

const TranslationEntry* FindFullTranslation(const std::wstring& lower) {
    const std::wstring path = FullTranslationDictionaryPath();
    std::error_code error;
    const uintmax_t fileSize = std::filesystem::file_size(path, error);
    if (error || fileSize == 0) return nullptr;
    std::ifstream input(path, std::ios::binary);
    if (!input) return nullptr;
    uintmax_t first = 0;
    uintmax_t last = fileSize;
    static TranslationEntry result;
    for (int attempt = 0; attempt < 48 && first < last; ++attempt) {
        const uintmax_t middle = first + (last - first) / 2;
        input.clear();
        uintmax_t lineOffset = middle;
        // A forward-only skip can discard the only remaining candidate line. Seek to the line containing middle instead.
        while (lineOffset > 0) {
            input.seekg(static_cast<std::streamoff>(lineOffset - 1));
            if (!input) return nullptr;
            const int previousCharacter = input.get();
            if (previousCharacter == '\n') break;
            --lineOffset;
        }
        input.clear();
        input.seekg(static_cast<std::streamoff>(lineOffset));
        if (!input) return nullptr;
        const std::streampos lineStart = input.tellg();
        std::string line;
        if (!std::getline(input, line) || line.empty()) return nullptr;
        enput::json::Value document;
        const enput::json::Value* key = enput::json::ReadDocument(line, &document) ? enput::json::ObjectValue(document, "key") : nullptr;
        if (!key || key->type != enput::json::Value::Type::String) return nullptr;
        const std::wstring candidate = Utf8ToWide(key->string);
        if (candidate == lower) return ParseTranslationEntry(document, &result) ? &result : nullptr;
        const std::streampos nextLine = input.tellg();
        if (candidate < lower) {
            if (nextLine < 0 || static_cast<uintmax_t>(nextLine) <= first) return nullptr;
            first = static_cast<uintmax_t>(nextLine);
        } else {
            if (lineStart < 0 || static_cast<uintmax_t>(lineStart) >= last) return nullptr;
            last = static_cast<uintmax_t>(lineStart);
        }
    }
    return nullptr;
}

const TranslationEntry* FindTranslation(const std::wstring& text) {
    const std::wstring lower = Lowercase(text);
    const std::vector<TranslationEntry>& entries = LoadTranslationDictionary();
    const auto entry = std::find_if(entries.begin(), entries.end(), [&lower](const TranslationEntry& value) { return Lowercase(value.text) == lower; });
    return entry == entries.end() ? FindFullTranslation(lower) : &*entry;
}

class KeyEditSession;
class CandidateClickEditSession;
class OverlayRefreshEditSession;

class CandidateWindow final {
public:
    ~CandidateWindow() {
        if (font_) DeleteObject(font_);
        if (window_) DestroyWindow(window_);
    }

    void Show(ITfContext* context, TfEditCookie cookie, ITfRange* range, const std::vector<std::wstring>& candidates, const RuntimeConfiguration& configuration, size_t page, size_t pageCount, size_t selectedIndex, bool capsLock, bool preservePosition, std::wstring modeMarker, std::function<void(int)> actionCallback) {
        const std::vector<std::wstring> previousCandidates = candidates_;
        const size_t previousPage = page_;
        const size_t previousPageCount = pageCount_;
        const size_t previousSelectedIndex = selectedIndex_;
        const bool previousCapsLock = capsLock_;
        const std::wstring previousModeMarker = modeMarker_;
        candidates_ = candidates;
        configuration_ = configuration;
        page_ = page;
        pageCount_ = pageCount;
        selectedIndex_ = selectedIndex;
        capsLock_ = capsLock;
        modeMarker_ = std::move(modeMarker);
        actionCallback_ = std::move(actionCallback);
        if (candidates_.empty()) { Hide(); return; }
        if (!EnsureWindow()) return;
        ConfigureWindow();

        ITfContextView* view{};
        RECT textRect{};
        BOOL clipped{};
        if (FAILED(context->GetActiveView(&view))) return;
        const HRESULT hr = view->GetTextExt(cookie, range, &textRect, &clipped);
        view->Release();
        if (FAILED(hr)) return;

        const SIZE size = MeasureWindow();
        int positionX = textRect.left;
        int positionY = textRect.bottom + 2;
        if (configuration_.avoidScreenEdges) {
            MONITORINFO monitorInfo{ sizeof(monitorInfo) };
            const HMONITOR monitor = MonitorFromRect(&textRect, MONITOR_DEFAULTTONEAREST);
            if (monitor && GetMonitorInfoW(monitor, &monitorInfo)) {
                const RECT& workArea = monitorInfo.rcWork;
                if (positionY + size.cy > workArea.bottom && textRect.top - 2 - size.cy >= workArea.top) positionY = textRect.top - 2 - size.cy;
                const int left = static_cast<int>(workArea.left);
                const int top = static_cast<int>(workArea.top);
                const int maximumX = static_cast<int>(workArea.right) - size.cx;
                const int maximumY = static_cast<int>(workArea.bottom) - size.cy;
                positionX = maximumX < left ? left : std::clamp(positionX, left, maximumX);
                positionY = maximumY < top ? top : std::clamp(positionY, top, maximumY);
            }
        }
        const bool candidateSelectionChanged = IsWindowVisible(window_) && previousCandidates == candidates_ &&
            previousPage == page_ && previousPageCount == pageCount_ && previousCapsLock == capsLock_ &&
            previousModeMarker == modeMarker_ && previousSelectedIndex != selectedIndex_;
        // A selection key must not move the candidate window when the application's composition bounds settle.
        if (preservePosition || candidateSelectionChanged) {
            positionX = positionX_;
            positionY = positionY_;
        }
        const bool sizeChanged = size.cx != size_.cx || size.cy != size_.cy;
        const bool positionChanged = positionX != positionX_ || positionY != positionY_;
        const bool wasVisible = IsWindowVisible(window_) != FALSE;
        const bool geometryChanged = !wasVisible || sizeChanged || positionChanged;
        if (sizeChanged) {
            const int diameter = (std::max)(1, configuration_.theme.cornerRadius * 2);
            SetWindowRgn(window_, CreateRoundRectRgn(0, 0, size.cx, size.cy, diameter, diameter), FALSE);
            size_ = size;
        }
        if (geometryChanged) {
            // A hidden layered window retains its previous pixels. Keep them transparent until the new frame is ready.
            if (!wasVisible) SetLayeredWindowAttributes(window_, 0, 0, LWA_ALPHA);
            SetWindowPos(window_, HWND_TOPMOST, positionX, positionY, size.cx, size.cy,
                         SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOREDRAW);
            positionX_ = positionX;
            positionY_ = positionY;
            // Show the final candidate content in one paint, without exposing the window setup frame.
            RedrawWindow(window_, nullptr, nullptr, RDW_INVALIDATE | RDW_UPDATENOW);
            if (!wasVisible) SetLayeredWindowAttributes(window_, 0, configuration_.opacity, LWA_ALPHA);
            return;
        }
        const bool selectionOnly = !sizeChanged && !positionChanged && candidateSelectionChanged;
        if (selectionOnly) {
            // Color-font rendering requires one contiguous paint region; leave the footer marker untouched.
            if (modeMarker_ == L"EMOJI") {
                const RECT candidateRows = CandidateRows();
                InvalidateRect(window_, &candidateRows, FALSE);
            } else {
                const RECT previousRow = CandidateRow(previousSelectedIndex);
                const RECT selectedRow = CandidateRow(selectedIndex_);
                InvalidateRect(window_, &previousRow, FALSE);
                InvalidateRect(window_, &selectedRow, FALSE);
            }
        } else {
            InvalidateRect(window_, nullptr, FALSE);
        }
    }

    void Hide() {
        candidates_.clear();
        actionCallback_ = {};
        if (window_) ShowWindow(window_, SW_HIDE);
    }

    bool GetScreenBounds(RECT* bounds) const {
        return bounds && window_ && IsWindowVisible(window_) && GetWindowRect(window_, bounds);
    }

private:
    static constexpr wchar_t kClassName[] = L"EnputMethodCandidateWindow";

    void ConfigureWindow() {
        const bool emojiFont = modeMarker_ == L"EMOJI";
        if (font_ && fontFamily_ == configuration_.fontFamily && fontSize_ == configuration_.fontSize &&
            opacity_ == configuration_.opacity && usesEmojiFont_ == emojiFont) return;
        if (font_) DeleteObject(font_);
        HDC dc = GetDC(window_);
        const int dpi = GetDeviceCaps(dc, LOGPIXELSY);
        ReleaseDC(window_, dc);
        const wchar_t* fontFamily = emojiFont ? L"Segoe UI Emoji" : configuration_.fontFamily.c_str();
        font_ = CreateFontW(-MulDiv(configuration_.fontSize, dpi, 72), 0, 0, 0,
                            FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                            CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, fontFamily);
        SetLayeredWindowAttributes(window_, 0, configuration_.opacity, LWA_ALPHA);
        fontFamily_ = configuration_.fontFamily;
        fontSize_ = configuration_.fontSize;
        opacity_ = configuration_.opacity;
        usesEmojiFont_ = emojiFont;
    }

    SIZE MeasureWindow() const {
        HDC dc = GetDC(window_);
        HGDIOBJ previous = font_ ? SelectObject(dc, font_) : nullptr;
        const int padding = configuration_.theme.padding;
        const int rowHeight = configuration_.theme.rowHeight;
        int width = padding * 2;
        if (configuration_.horizontal) {
            for (size_t index = 0; index < candidates_.size(); ++index) width += LabelSize(dc, index).cx + padding * 2;
        } else {
            int widest = 0;
            for (size_t index = 0; index < candidates_.size(); ++index) widest = (std::max)(widest, static_cast<int>(LabelSize(dc, index).cx));
            width = (std::max)(220, widest + padding * 2);
        }
        if (pageCount_ > 1 || capsLock_ || !modeMarker_.empty()) {
            int footerWidth = padding * 2;
            if (capsLock_ || !modeMarker_.empty()) {
                std::wstring marker = capsLock_ ? L"CAPS" : L"";
                if (!marker.empty() && !modeMarker_.empty()) marker += L" ";
                marker += modeMarker_;
                SIZE markerSize{};
                GetTextExtentPoint32W(dc, marker.c_str(), static_cast<int>(marker.size()), &markerSize);
                footerWidth += markerSize.cx + padding;
            }
            if (pageCount_ > 1) {
                const std::wstring pageText = L"Page " + std::to_wstring(page_ + 1) + L"/" + std::to_wstring(pageCount_);
                SIZE pageSize{};
                GetTextExtentPoint32W(dc, pageText.c_str(), static_cast<int>(pageText.size()), &pageSize);
                footerWidth += pageSize.cx + rowHeight * 2;
            }
            width = (std::max)(width, footerWidth);
        }
        if (previous) SelectObject(dc, previous);
        ReleaseDC(window_, dc);
        const int footerHeight = (pageCount_ > 1 || capsLock_ || !modeMarker_.empty()) ? rowHeight : 0;
        const int height = (configuration_.horizontal ? rowHeight + padding * 2 : rowHeight * static_cast<int>(candidates_.size()) + padding * 2) + footerHeight;
        return { width + configuration_.theme.shadowSize, height + configuration_.theme.shadowSize };
    }

    SIZE LabelSize(HDC dc, size_t index) const {
        const std::wstring label = std::to_wstring(index + 1) + L".  " + VisibleCandidate(candidates_[index]);
        SIZE size{};
        GetTextExtentPoint32W(dc, label.c_str(), static_cast<int>(label.size()), &size);
        return size;
    }

    static std::wstring VisibleCandidate(std::wstring candidate) {
        std::replace(candidate.begin(), candidate.end(), static_cast<wchar_t>(0x1F), L' ');
        return candidate;
    }

    static void DrawEmojiLabel(HDC dc, const RECT& row, const std::wstring& label, COLORREF color, int fontSize) {
        ID2D1Factory* d2dFactory{};
        ID2D1DCRenderTarget* target{};
        IDWriteFactory* writeFactory{};
        IDWriteTextFormat* format{};
        ID2D1SolidColorBrush* brush{};
        if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, &d2dFactory))) return;
        const D2D1_RENDER_TARGET_PROPERTIES properties = D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_DEFAULT,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_IGNORE));
        if (FAILED(d2dFactory->CreateDCRenderTarget(&properties, &target)) ||
            FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(&writeFactory))) ||
            FAILED(writeFactory->CreateTextFormat(L"Segoe UI Emoji", nullptr, DWRITE_FONT_WEIGHT_NORMAL, DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL,
                                                   static_cast<float>(MulDiv(fontSize, 96, 72)), L"", &format)) ||
            FAILED(target->CreateSolidColorBrush(D2D1::ColorF(GetRValue(color) / 255.0f, GetGValue(color) / 255.0f, GetBValue(color) / 255.0f), &brush))) {
            if (brush) brush->Release(); if (format) format->Release(); if (writeFactory) writeFactory->Release(); if (target) target->Release(); d2dFactory->Release(); return;
        }
        RECT bounds{}; GetClipBox(dc, &bounds);
        if (SUCCEEDED(target->BindDC(dc, &bounds))) {
            format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
            target->BeginDraw();
            target->DrawTextW(label.c_str(), static_cast<UINT32>(label.size()), format,
                              D2D1::RectF(static_cast<float>(row.left), static_cast<float>(row.top), static_cast<float>(row.right), static_cast<float>(row.bottom)),
                              brush, D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT);
            target->EndDraw();
        }
        brush->Release(); format->Release(); writeFactory->Release(); target->Release(); d2dFactory->Release();
    }

    int HitTestAction(POINT point) const {
        const int padding = configuration_.theme.padding;
        const int rowHeight = configuration_.theme.rowHeight;
        const int surfaceRight = size_.cx - configuration_.theme.shadowSize;
        const int surfaceBottom = size_.cy - configuration_.theme.shadowSize;
        const bool hasFooter = pageCount_ > 1 || capsLock_ || !modeMarker_.empty();
        const int rowsBottom = surfaceBottom - padding - (hasFooter ? rowHeight : 0);
        if (point.y >= padding && point.y < rowsBottom) {
            if (!configuration_.horizontal) {
                const int index = (point.y - padding) / rowHeight;
                return index >= 0 && index < static_cast<int>(candidates_.size()) ? index : -3;
            }
            HDC dc = GetDC(window_);
            HGDIOBJ previous = font_ ? SelectObject(dc, font_) : nullptr;
            int left = padding;
            for (size_t index = 0; index < candidates_.size(); ++index) {
                const int right = left + LabelSize(dc, index).cx + padding * 2;
                if (point.x >= left && point.x < right) {
                    if (previous) SelectObject(dc, previous);
                    ReleaseDC(window_, dc);
                    return static_cast<int>(index);
                }
                left = right;
            }
            if (previous) SelectObject(dc, previous);
            ReleaseDC(window_, dc);
        }
        if (pageCount_ > 1 && point.y >= rowsBottom && point.y < surfaceBottom - padding) {
            const RECT previousButton{ FooterNavigationLeft(), rowsBottom, FooterNavigationLeft() + rowHeight, surfaceBottom - padding };
            const RECT nextButton{ surfaceRight - padding - rowHeight, rowsBottom, surfaceRight - padding, surfaceBottom - padding };
            if (PtInRect(&previousButton, point)) return -1;
            if (PtInRect(&nextButton, point)) return -2;
        }
        return -3;
    }

    int FooterNavigationLeft() const {
        const int padding = configuration_.theme.padding;
        if (!capsLock_ && modeMarker_.empty()) return padding;
        HDC dc = GetDC(window_);
        HGDIOBJ previous = font_ ? SelectObject(dc, font_) : nullptr;
        std::wstring marker = capsLock_ ? L"CAPS" : L"";
        if (!marker.empty() && !modeMarker_.empty()) marker += L" ";
        marker += modeMarker_;
        SIZE markerSize{};
        GetTextExtentPoint32W(dc, marker.c_str(), static_cast<int>(marker.size()), &markerSize);
        if (previous) SelectObject(dc, previous);
        ReleaseDC(window_, dc);
        return (std::min)(size_.cx - configuration_.theme.shadowSize - padding, padding + markerSize.cx + padding);
    }

    RECT FooterRow() const {
        const int padding = configuration_.theme.padding;
        const int rowHeight = configuration_.theme.rowHeight;
        const int surfaceBottom = size_.cy - configuration_.theme.shadowSize;
        return { padding, surfaceBottom - rowHeight - padding, size_.cx - configuration_.theme.shadowSize - padding, surfaceBottom - padding };
    }

    RECT CandidateRow(size_t index) const {
        const int padding = configuration_.theme.padding;
        const int rowHeight = configuration_.theme.rowHeight;
        const int surfaceRight = size_.cx - configuration_.theme.shadowSize;
        if (!configuration_.horizontal) return { padding, padding + static_cast<int>(index) * rowHeight, surfaceRight - padding, padding + static_cast<int>(index + 1) * rowHeight };
        HDC dc = GetDC(window_);
        HGDIOBJ previous = font_ ? SelectObject(dc, font_) : nullptr;
        int left = padding;
        for (size_t row = 0; row < index; ++row) left += LabelSize(dc, row).cx + padding * 2;
        const int right = left + LabelSize(dc, index).cx + padding * 2;
        if (previous) SelectObject(dc, previous);
        ReleaseDC(window_, dc);
        return { left, padding, right, padding + rowHeight };
    }

    RECT CandidateRows() const {
        const int padding = configuration_.theme.padding;
        const int rowHeight = configuration_.theme.rowHeight;
        const int contentHeight = configuration_.horizontal ? rowHeight : rowHeight * static_cast<int>(candidates_.size());
        return { 0, 0, size_.cx, padding * 2 + contentHeight };
    }

    bool EnsureWindow() {
        if (window_) return true;
        WNDCLASSEXW windowClass{ sizeof(windowClass) };
        windowClass.lpfnWndProc = WindowProc;
        windowClass.hInstance = g_module;
        windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        windowClass.lpszClassName = kClassName;
        RegisterClassExW(&windowClass);
        window_ = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED,
                                  kClassName, L"", WS_POPUP,
                                  0, 0, 0, 0, nullptr, nullptr, g_module, this);
        return window_ != nullptr;
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM, LPARAM parameter) {
        if (message == WM_NCCREATE) {
            const auto* create = reinterpret_cast<const CREATESTRUCTW*>(parameter);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(create->lpCreateParams));
        }
        auto* self = reinterpret_cast<CandidateWindow*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_PAINT && self) {
            PAINTSTRUCT paint{};
            HDC paintDc = BeginPaint(window, &paint);
            RECT client{};
            GetClientRect(window, &client);
            HDC bufferDc = CreateCompatibleDC(paintDc);
            HBITMAP buffer = bufferDc ? CreateCompatibleBitmap(paintDc, client.right, client.bottom) : nullptr;
            HGDIOBJ previousBitmap = buffer ? SelectObject(bufferDc, buffer) : nullptr;
            HDC dc = buffer ? bufferDc : paintDc;
            const int shadow = self->configuration_.theme.shadowSize;
            RECT surface{ 0, 0, client.right - shadow, client.bottom - shadow };
            HBRUSH shadowBrush = CreateSolidBrush(self->configuration_.theme.border);
            if (shadow > 0) {
                RECT shadowRect{ shadow, shadow, client.right, client.bottom };
                FillRect(dc, &shadowRect, shadowBrush);
            }
            DeleteObject(shadowBrush);
            HBRUSH background = CreateSolidBrush(self->configuration_.theme.background);
            HPEN border = CreatePen(PS_SOLID, self->configuration_.theme.borderWidth, self->configuration_.theme.border);
            HGDIOBJ previousBrush = SelectObject(dc, background);
            HGDIOBJ previousPen = SelectObject(dc, border);
            RoundRect(dc, surface.left, surface.top, surface.right, surface.bottom,
                      self->configuration_.theme.cornerRadius * 2, self->configuration_.theme.cornerRadius * 2);
            SelectObject(dc, previousBrush);
            SelectObject(dc, previousPen);
            DeleteObject(background);
            DeleteObject(border);
            SetBkMode(dc, TRANSPARENT);
            HGDIOBJ previousFont = self->font_ ? SelectObject(dc, self->font_) : nullptr;
            const int padding = self->configuration_.theme.padding;
            const int rowHeight = self->configuration_.theme.rowHeight;
            int horizontalOffset = padding;
            for (size_t index = 0; index < self->candidates_.size(); ++index) {
                SIZE labelSize = self->LabelSize(dc, index);
                RECT row{};
                if (self->configuration_.horizontal) {
                    row = { horizontalOffset, padding, horizontalOffset + labelSize.cx + padding * 2, padding + rowHeight };
                    horizontalOffset = row.right;
                } else {
                    row = { padding, padding + static_cast<int>(index) * rowHeight, surface.right - padding,
                            padding + static_cast<int>(index + 1) * rowHeight };
                }
                const bool selected = index == self->selectedIndex_;
                const bool hovered = static_cast<int>(index) == self->hoverAction_;
                if (selected || hovered) {
                    HBRUSH highlightBrush = CreateSolidBrush(self->configuration_.theme.selectedBackground);
                    FillRect(dc, &row, highlightBrush);
                    DeleteObject(highlightBrush);
                    if (selected && self->configuration_.theme.selectedBorderWidth > 0) {
                        HPEN selectedBorder = CreatePen(PS_SOLID, self->configuration_.theme.selectedBorderWidth, self->configuration_.theme.selectedBorder);
                        HGDIOBJ previousSelectedBorder = SelectObject(dc, selectedBorder);
                        HGDIOBJ previousSelectedBrush = SelectObject(dc, GetStockObject(HOLLOW_BRUSH));
                        RoundRect(dc, row.left, row.top, row.right, row.bottom,
                                  self->configuration_.theme.selectedCornerRadius * 2, self->configuration_.theme.selectedCornerRadius * 2);
                        SelectObject(dc, previousSelectedBorder);
                        SelectObject(dc, previousSelectedBrush);
                        DeleteObject(selectedBorder);
                    }
                }
                SetTextColor(dc, selected ? self->configuration_.theme.selectedForeground : self->configuration_.theme.foreground);
                const std::wstring label = std::to_wstring(index + 1) + L".  " + VisibleCandidate(self->candidates_[index]);
                if (self->modeMarker_ == L"EMOJI") DrawEmojiLabel(dc, row, label, selected ? self->configuration_.theme.selectedForeground : self->configuration_.theme.foreground, self->configuration_.fontSize);
                else DrawTextW(dc, label.c_str(), static_cast<int>(label.size()), &row, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
            }
            if (self->pageCount_ > 1 || self->capsLock_ || !self->modeMarker_.empty()) {
                const std::wstring pageText = L"Page " + std::to_wstring(self->page_ + 1) + L"/" + std::to_wstring(self->pageCount_);
                RECT pageRow{ padding, surface.bottom - rowHeight - padding, surface.right - padding, surface.bottom - padding };
                int navigationLeft = pageRow.left;
                if (self->capsLock_ || !self->modeMarker_.empty()) {
                    std::wstring capsText = self->capsLock_ ? L"CAPS" : L"";
                    if (!capsText.empty() && !self->modeMarker_.empty()) capsText += L" ";
                    capsText += self->modeMarker_;
                    SetTextColor(dc, self->configuration_.theme.selectedForeground);
                    DrawTextW(dc, capsText.c_str(), static_cast<int>(capsText.size()), &pageRow, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                    RECT markerSize{ 0, 0, 0, 0 };
                    DrawTextW(dc, capsText.c_str(), static_cast<int>(capsText.size()), &markerSize, DT_CALCRECT | DT_SINGLELINE);
                    navigationLeft = (std::min)(pageRow.right, pageRow.left + markerSize.right + padding);
                }
                if (self->pageCount_ > 1) {
                    const std::wstring previousText = L"<";
                    const std::wstring nextText = L">";
                    RECT navigationRow{ navigationLeft, pageRow.top, pageRow.right, pageRow.bottom };
                    RECT previousButton{ navigationRow.left, navigationRow.top, navigationRow.left + rowHeight, navigationRow.bottom };
                    RECT nextButton{ navigationRow.right - rowHeight, navigationRow.top, navigationRow.right, navigationRow.bottom };
                    if (self->hoverAction_ == -1 || self->hoverAction_ == -2) {
                        HBRUSH hover = CreateSolidBrush(self->configuration_.theme.selectedBackground);
                        FillRect(dc, self->hoverAction_ == -1 ? &previousButton : &nextButton, hover);
                        DeleteObject(hover);
                    }
                    SetTextColor(dc, self->hoverAction_ == -1 ? self->configuration_.theme.selectedForeground : self->configuration_.theme.foreground);
                    DrawTextW(dc, previousText.c_str(), static_cast<int>(previousText.size()), &previousButton, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                    SetTextColor(dc, self->hoverAction_ == -2 ? self->configuration_.theme.selectedForeground : self->configuration_.theme.foreground);
                    DrawTextW(dc, nextText.c_str(), static_cast<int>(nextText.size()), &nextButton, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                    RECT pageNumber{ navigationRow.left + rowHeight, navigationRow.top, navigationRow.right - rowHeight, navigationRow.bottom };
                    DrawTextW(dc, pageText.c_str(), static_cast<int>(pageText.size()), &pageNumber, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                }
            }
            if (previousFont) SelectObject(dc, previousFont);
            if (buffer) {
                BitBlt(paintDc, paint.rcPaint.left, paint.rcPaint.top,
                       paint.rcPaint.right - paint.rcPaint.left, paint.rcPaint.bottom - paint.rcPaint.top,
                       bufferDc, paint.rcPaint.left, paint.rcPaint.top, SRCCOPY);
                SelectObject(bufferDc, previousBitmap);
                DeleteObject(buffer);
            }
            if (bufferDc) DeleteDC(bufferDc);
            EndPaint(window, &paint);
            return 0;
        }
        if (message == WM_NCHITTEST) return HTCLIENT;
        if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
        if (message == WM_ERASEBKGND) return 1;
        if (message == WM_SETCURSOR && self) {
            POINT point{};
            GetCursorPos(&point);
            ScreenToClient(window, &point);
            if (self->HitTestAction(point) != -3) {
                SetCursor(LoadCursorW(nullptr, IDC_HAND));
                return TRUE;
            }
        }
        if (message == WM_MOUSEMOVE && self) {
            const POINT point{ static_cast<short>(LOWORD(parameter)), static_cast<short>(HIWORD(parameter)) };
            const int hoverAction = self->HitTestAction(point);
            if (hoverAction != self->hoverAction_) {
                const int previousAction = self->hoverAction_;
                self->hoverAction_ = hoverAction;
                if (previousAction >= 0) {
                    const RECT row = self->CandidateRow(static_cast<size_t>(previousAction));
                    InvalidateRect(window, &row, FALSE);
                }
                if (hoverAction >= 0) {
                    const RECT row = self->CandidateRow(static_cast<size_t>(hoverAction));
                    InvalidateRect(window, &row, FALSE);
                }
                if (previousAction < 0 || hoverAction < 0) {
                    const RECT footer = self->FooterRow();
                    InvalidateRect(window, &footer, FALSE);
                }
            }
            SetCursor(LoadCursorW(nullptr, hoverAction != -3 ? IDC_HAND : IDC_ARROW));
            TRACKMOUSEEVENT tracking{ sizeof(tracking), TME_LEAVE, window, 0 };
            TrackMouseEvent(&tracking);
            return 0;
        }
        if (message == WM_MOUSELEAVE && self) {
            if (self->hoverAction_ != -3) {
                const int previousAction = self->hoverAction_;
                self->hoverAction_ = -3;
                if (previousAction >= 0) {
                    const RECT row = self->CandidateRow(static_cast<size_t>(previousAction));
                    InvalidateRect(window, &row, FALSE);
                } else {
                    const RECT footer = self->FooterRow();
                    InvalidateRect(window, &footer, FALSE);
                }
            }
            return 0;
        }
        if (message == WM_LBUTTONDOWN && self) {
            const POINT point{ static_cast<short>(LOWORD(parameter)), static_cast<short>(HIWORD(parameter)) };
            self->pressedAction_ = self->HitTestAction(point);
            if (self->pressedAction_ != -3) SetCapture(window);
            return 0;
        }
        if (message == WM_LBUTTONUP && self && self->actionCallback_) {
            const POINT point{ static_cast<short>(LOWORD(parameter)), static_cast<short>(HIWORD(parameter)) };
            const int action = self->HitTestAction(point);
            if (GetCapture() == window) ReleaseCapture();
            if (action != -3 && action == self->pressedAction_) self->actionCallback_(action);
            self->pressedAction_ = -3;
            return 0;
        }
        return DefWindowProcW(window, message, 0, parameter);
    }

    HWND window_ = nullptr;
    HFONT font_ = nullptr;
    std::wstring fontFamily_;
    int fontSize_ = 0;
    BYTE opacity_ = 0;
    bool usesEmojiFont_ = false;
    RuntimeConfiguration configuration_{};
    std::vector<std::wstring> candidates_;
    size_t page_ = 0;
    size_t pageCount_ = 0;
    size_t selectedIndex_ = 0;
    bool capsLock_ = false;
    std::wstring modeMarker_;
    std::function<void(int)> actionCallback_;
    SIZE size_{};
    int positionX_ = 0;
    int positionY_ = 0;
    int hoverAction_ = -3;
    int pressedAction_ = -3;
};

class TranslationWindow final {
public:
    ~TranslationWindow() { if (font_) DeleteObject(font_); if (window_) DestroyWindow(window_); }

    void Show(ITfContext* context, TfEditCookie cookie, ITfRange* range, const TranslationEntry* entry, const RuntimeConfiguration& configuration, const RECT* candidateBounds) {
        if (!entry) { Hide(); return; }
        configuration_ = configuration;
        lines_.clear();
        lines_.push_back(entry->text);
        if (!entry->partsOfSpeech.empty()) {
            std::wstring parts;
            for (size_t index = 0; index < entry->partsOfSpeech.size(); ++index) { if (index) parts += L", "; parts += entry->partsOfSpeech[index]; }
            lines_.push_back(parts);
        }
        for (const auto& [language, meanings] : entry->translations) {
            std::wstring line = language + L": ";
            for (size_t index = 0; index < meanings.size(); ++index) { if (index) line += L"; "; line += meanings[index]; }
            lines_.push_back(line);
        }
        if (!entry->example.empty()) lines_.push_back(L"Example: " + entry->example);
        if (!entry->source.empty()) lines_.push_back(L"Source: " + entry->source);
        if (!EnsureWindow()) return;
        ConfigureWindow();
        ITfContextView* view{}; RECT textRect{}; BOOL clipped{};
        if (FAILED(context->GetActiveView(&view))) return;
        const HRESULT hr = view->GetTextExt(cookie, range, &textRect, &clipped);
        view->Release();
        if (FAILED(hr)) return;
        const int width = configuration_.theme.translationWidth;
        const int maximumHeight = configuration_.theme.translationMaxHeight;
        const int padding = configuration_.theme.translationPadding;
        const int unscrolledContentHeight = MeasureContentHeight(width - padding * 2);
        hasScrollbar_ = padding * 2 + unscrolledContentHeight > maximumHeight;
        contentWidth_ = width - padding * 2 - (hasScrollbar_ ? kScrollbarWidth + padding : 0);
        contentHeight_ = MeasureContentHeight(contentWidth_);
        const int height = (std::min)(maximumHeight, padding * 2 + contentHeight_);
        viewportHeight_ = height - padding * 2;
        scrollOffset_ = 0;
        const RECT anchor = candidateBounds ? *candidateBounds : textRect;
        int left = candidateBounds ? candidateBounds->right + 12 : textRect.right + 12;
        int top = candidateBounds ? candidateBounds->top : textRect.bottom + 2;
        if (configuration_.avoidScreenEdges) {
            MONITORINFO info{ sizeof(info) };
            const HMONITOR monitor = MonitorFromRect(&anchor, MONITOR_DEFAULTTONEAREST);
            if (monitor && GetMonitorInfoW(monitor, &info)) {
                if (candidateBounds && left + width > info.rcWork.right && candidateBounds->left - 12 - width >= info.rcWork.left) left = candidateBounds->left - 12 - width;
                left = std::clamp(left, static_cast<int>(info.rcWork.left), static_cast<int>(info.rcWork.right) - width);
                top = std::clamp(top, static_cast<int>(info.rcWork.top), static_cast<int>(info.rcWork.bottom) - height);
            }
        }
        size_ = { width, height };
        SetWindowPos(window_, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void Hide() { if (window_) ShowWindow(window_, SW_HIDE); }

private:
    static constexpr wchar_t kClassName[] = L"EnputMethodTranslationWindow";

    void ConfigureWindow() {
        if (font_) DeleteObject(font_);
        HDC dc = GetDC(window_); const int dpi = GetDeviceCaps(dc, LOGPIXELSY); ReleaseDC(window_, dc);
        font_ = CreateFontW(-MulDiv(configuration_.fontSize, dpi, 72), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, configuration_.fontFamily.c_str());
        SetLayeredWindowAttributes(window_, 0, configuration_.opacity, LWA_ALPHA);
    }

    int MeasureContentHeight(int width) const {
        HDC dc = GetDC(window_);
        HGDIOBJ previous = font_ ? SelectObject(dc, font_) : nullptr;
        int height = 0;
        for (const std::wstring& value : lines_) {
            RECT line{ 0, 0, width, 0 };
            DrawTextW(dc, value.c_str(), static_cast<int>(value.size()), &line, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
            height += (std::max)(configuration_.theme.rowHeight, static_cast<int>(line.bottom - line.top)) + 2;
        }
        if (previous) SelectObject(dc, previous);
        ReleaseDC(window_, dc);
        return (std::max)(0, height - 2);
    }

    int MaximumScrollOffset() const { return (std::max)(0, contentHeight_ - viewportHeight_); }

    int ScrollbarThumbHeight() const {
        if (!hasScrollbar_ || contentHeight_ <= 0) return 0;
        return (std::max)(20, viewportHeight_ * viewportHeight_ / contentHeight_);
    }

    int ScrollbarThumbTop() const {
        const int travel = viewportHeight_ - ScrollbarThumbHeight();
        const int maximum = MaximumScrollOffset();
        return configuration_.theme.translationPadding + (maximum > 0 ? scrollOffset_ * travel / maximum : 0);
    }

    bool IsOverScrollbar(POINT point) const {
        return hasScrollbar_ && point.x >= size_.cx - configuration_.theme.translationPadding - kScrollbarWidth &&
            point.x < size_.cx - configuration_.theme.translationPadding && point.y >= configuration_.theme.translationPadding &&
            point.y < configuration_.theme.translationPadding + viewportHeight_;
    }

    void SetScrollOffset(int offset) {
        const int clamped = std::clamp(offset, 0, MaximumScrollOffset());
        if (clamped == scrollOffset_) return;
        scrollOffset_ = clamped;
        InvalidateRect(window_, nullptr, FALSE);
    }

    void DragScrollbarTo(int y) {
        const int thumbHeight = ScrollbarThumbHeight();
        const int travel = viewportHeight_ - thumbHeight;
        const int maximum = MaximumScrollOffset();
        if (travel <= 0 || maximum <= 0) return;
        SetScrollOffset((y - configuration_.theme.translationPadding - dragOffset_) * maximum / travel);
    }

    bool EnsureWindow() {
        if (window_) return true;
        WNDCLASSEXW windowClass{ sizeof(windowClass) }; windowClass.lpfnWndProc = WindowProc; windowClass.hInstance = g_module; windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW); windowClass.lpszClassName = kClassName;
        RegisterClassExW(&windowClass);
        window_ = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED, kClassName, L"", WS_POPUP, 0, 0, 0, 0, nullptr, nullptr, g_module, this);
        return window_ != nullptr;
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM, LPARAM parameter) {
        if (message == WM_NCCREATE) SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(reinterpret_cast<const CREATESTRUCTW*>(parameter)->lpCreateParams));
        auto* self = reinterpret_cast<TranslationWindow*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == WM_PAINT && self) {
            PAINTSTRUCT paint{}; HDC dc = BeginPaint(window, &paint); RECT client{}; GetClientRect(window, &client);
            HBRUSH background = CreateSolidBrush(self->configuration_.theme.translationBackground); HPEN border = CreatePen(PS_SOLID, self->configuration_.theme.translationBorderWidth, self->configuration_.theme.translationBorder);
            HGDIOBJ oldBrush = SelectObject(dc, background); HGDIOBJ oldPen = SelectObject(dc, border);
            RoundRect(dc, 0, 0, client.right, client.bottom, self->configuration_.theme.translationCornerRadius * 2, self->configuration_.theme.translationCornerRadius * 2);
            SelectObject(dc, oldBrush); SelectObject(dc, oldPen); DeleteObject(background); DeleteObject(border);
            SetBkMode(dc, TRANSPARENT); HGDIOBJ oldFont = self->font_ ? SelectObject(dc, self->font_) : nullptr;
            const int padding = self->configuration_.theme.translationPadding;
            RECT viewport{ padding, padding, client.right - padding - (self->hasScrollbar_ ? kScrollbarWidth + padding : 0), client.bottom - padding };
            const int saved = SaveDC(dc);
            IntersectClipRect(dc, viewport.left, viewport.top, viewport.right, viewport.bottom);
            int lineTop = viewport.top - self->scrollOffset_;
            for (size_t index = 0; index < self->lines_.size(); ++index) {
                SetTextColor(dc, index == 0 ? self->configuration_.theme.translationTitleForeground : self->configuration_.theme.translationForeground);
                RECT measured{ viewport.left, lineTop, viewport.right, lineTop };
                DrawTextW(dc, self->lines_[index].c_str(), static_cast<int>(self->lines_[index].size()), &measured, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
                const int lineHeight = (std::max)(self->configuration_.theme.rowHeight, static_cast<int>(measured.bottom - measured.top));
                if (lineTop + lineHeight >= viewport.top && lineTop < viewport.bottom) {
                    RECT line{ viewport.left, lineTop, viewport.right, lineTop + lineHeight };
                    DrawTextW(dc, self->lines_[index].c_str(), static_cast<int>(self->lines_[index].size()), &line, DT_LEFT | DT_WORDBREAK);
                }
                lineTop += lineHeight + 2;
            }
            RestoreDC(dc, saved);
            if (self->hasScrollbar_) {
                RECT track{ client.right - padding - kScrollbarWidth, padding, client.right - padding, client.bottom - padding };
                HBRUSH trackBrush = CreateSolidBrush(self->configuration_.theme.translationScrollbarTrack);
                FillRect(dc, &track, trackBrush);
                DeleteObject(trackBrush);
                const int thumbTop = self->ScrollbarThumbTop();
                RECT thumb{ track.left, thumbTop, track.right, thumbTop + self->ScrollbarThumbHeight() };
                HBRUSH thumbBrush = CreateSolidBrush(self->configuration_.theme.translationScrollbarThumb);
                FillRect(dc, &thumb, thumbBrush);
                DeleteObject(thumbBrush);
            }
            if (oldFont) SelectObject(dc, oldFont); EndPaint(window, &paint); return 0;
        }
        if (message == WM_NCHITTEST) return HTCLIENT;
        if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
        if (message == WM_MOUSEWHEEL && self) {
            const int direction = GET_WHEEL_DELTA_WPARAM(parameter) > 0 ? -1 : 1;
            self->SetScrollOffset(self->scrollOffset_ + direction * self->configuration_.theme.rowHeight * 3);
            return 0;
        }
        if (message == WM_LBUTTONDOWN && self) {
            const POINT point{ static_cast<short>(LOWORD(parameter)), static_cast<short>(HIWORD(parameter)) };
            if (!self->IsOverScrollbar(point)) return 0;
            const int thumbTop = self->ScrollbarThumbTop();
            self->dragOffset_ = point.y >= thumbTop && point.y < thumbTop + self->ScrollbarThumbHeight() ? point.y - thumbTop : self->ScrollbarThumbHeight() / 2;
            self->draggingScrollbar_ = true;
            SetCapture(window);
            self->DragScrollbarTo(point.y);
            return 0;
        }
        if (message == WM_MOUSEMOVE && self && self->draggingScrollbar_) {
            const POINT point{ static_cast<short>(LOWORD(parameter)), static_cast<short>(HIWORD(parameter)) };
            self->DragScrollbarTo(point.y);
            return 0;
        }
        if (message == WM_LBUTTONUP && self && self->draggingScrollbar_) {
            self->draggingScrollbar_ = false;
            if (GetCapture() == window) ReleaseCapture();
            return 0;
        }
        return DefWindowProcW(window, message, 0, parameter);
    }

    static constexpr int kScrollbarWidth = 10;
    HWND window_ = nullptr; HFONT font_ = nullptr; RuntimeConfiguration configuration_{}; std::vector<std::wstring> lines_;
    SIZE size_{};
    int contentWidth_ = 0;
    int contentHeight_ = 0;
    int viewportHeight_ = 0;
    int scrollOffset_ = 0;
    int dragOffset_ = 0;
    bool hasScrollbar_ = false;
    bool draggingScrollbar_ = false;
};

class TextService final : public ITfTextInputProcessorEx, public ITfKeyEventSink, public ITfCompositionSink {
public:
    TextService() { InterlockedIncrement(&g_objectCount); }
    ~TextService() { ClearComposition(); Deactivate(); InterlockedDecrement(&g_objectCount); }

    STDMETHODIMP QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        if (iid == IID_IUnknown || iid == IID_ITfTextInputProcessor || iid == IID_ITfTextInputProcessorEx) *result = static_cast<ITfTextInputProcessorEx*>(this);
        else if (iid == IID_ITfKeyEventSink) *result = static_cast<ITfKeyEventSink*>(this);
        else if (iid == IID_ITfCompositionSink) *result = static_cast<ITfCompositionSink*>(this);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override { const auto refs = InterlockedDecrement(&refs_); if (!refs) delete this; return refs; }

    STDMETHODIMP Activate(ITfThreadMgr* threadManager, TfClientId clientId) override {
        if (!threadManager) return E_INVALIDARG;
        Deactivate();
        threadManager_ = threadManager; threadManager_->AddRef(); clientId_ = clientId;
        isFocused_ = true;
        CreateOverlayActionWindow();
        overlayClient_ = std::make_unique<enput::OverlayClient>(OverlayExecutablePath(), [this](enput::OverlayEvent event) { PostOverlayEvent(std::move(event)); });
        overlayClient_->Start();
        ITfKeystrokeMgr* keystrokeManager = nullptr;
        HRESULT hr = threadManager_->QueryInterface(IID_PPV_ARGS(&keystrokeManager));
        if (SUCCEEDED(hr)) {
            hr = keystrokeManager->AdviseKeyEventSink(clientId_, static_cast<ITfKeyEventSink*>(this), TRUE);
            keystrokeManager->Release();
        }
        return hr;
    }
    STDMETHODIMP ActivateEx(ITfThreadMgr* threadManager, TfClientId clientId, DWORD) override { return Activate(threadManager, clientId); }
    STDMETHODIMP Deactivate() override {
        HideOverlay();
        if (overlayClient_) {
            overlayClient_->Stop();
            overlayClient_.reset();
        }
        DestroyOverlayActionWindow();
        candidateWindow_.Hide();
        translationWindow_.Hide();
        if (threadManager_) {
            ITfKeystrokeMgr* keystrokeManager = nullptr;
            if (SUCCEEDED(threadManager_->QueryInterface(IID_PPV_ARGS(&keystrokeManager)))) {
                keystrokeManager->UnadviseKeyEventSink(clientId_);
                keystrokeManager->Release();
            }
            threadManager_->Release(); threadManager_ = nullptr;
        }
        return S_OK;
    }

    STDMETHODIMP OnSetFocus(BOOL foreground) override {
        isFocused_ = foreground != FALSE;
        if (!isFocused_) {
            HideOverlay();
            candidateWindow_.Hide();
            translationWindow_.Hide();
            return S_OK;
        }

        RequestOverlayRefresh();
        return S_OK;
    }
    STDMETHODIMP OnTestKeyDown(ITfContext*, WPARAM key, LPARAM, BOOL* eaten) override { if (!eaten) return E_INVALIDARG; *eaten = ShouldHandleKey(key); return S_OK; }
    STDMETHODIMP OnTestKeyUp(ITfContext*, WPARAM, LPARAM, BOOL* eaten) override { if (!eaten) return E_INVALIDARG; *eaten = FALSE; return S_OK; }
    STDMETHODIMP OnKeyDown(ITfContext* context, WPARAM key, LPARAM, BOOL* eaten) override;
    STDMETHODIMP OnKeyUp(ITfContext*, WPARAM, LPARAM, BOOL* eaten) override { if (!eaten) return E_INVALIDARG; *eaten = FALSE; return S_OK; }
    STDMETHODIMP OnPreservedKey(ITfContext*, REFGUID, BOOL* eaten) override { if (!eaten) return E_INVALIDARG; *eaten = FALSE; return S_OK; }
    STDMETHODIMP OnCompositionTerminated(TfEditCookie, ITfComposition* composition) override {
        if (composition == composition_) ClearComposition();
        return S_OK;
    }

    HRESULT ApplyKey(ITfContext* context, TfEditCookie cookie, WPARAM key) {
        if (HasShortcut(configuration_.shortcuts.toggleTranslationWindow, key)) {
            configuration_ = LoadRuntimeConfiguration();
            translationEnabled_ = !translationEnabled_;
            if (composition_ || detachedSuggestionActive_) return RefreshCandidates(context, cookie);
            HideOverlay();
            translationWindow_.Hide();
            return S_OK;
        }
        if (HasShortcut(configuration_.shortcuts.toggleEmojiMode, key)) {
            configuration_ = LoadRuntimeConfiguration();
            emojiMode_ = !emojiMode_;
            if (typed_.empty()) return S_OK;
            allCandidates_ = emojiMode_ ? FindEmojiCandidates(typed_) : FindCandidates(typed_);
            currentPage_ = 0;
            selectedIndex_ = 0;
            UpdateCurrentPage();
            return UpdateComposition(context, cookie);
        }
        if (emojiMode_ && key == VK_ESCAPE && typed_.empty()) { emojiMode_ = false; return S_OK; }
        if (key >= 'A' && key <= 'Z') {
            ClearDetachedSuggestions();
            const bool uppercase = (GetKeyState(VK_SHIFT) < 0) ^ ((GetKeyState(VK_CAPITAL) & 1) != 0);
            typed_.insert(cursor_, 1, static_cast<wchar_t>(uppercase ? key : key + (L'a' - L'A')));
            ++cursor_;
            configuration_ = LoadRuntimeConfiguration();
            allCandidates_ = emojiMode_ ? FindEmojiCandidates(typed_) : FindCandidates(typed_);
            currentPage_ = 0;
            UpdateCurrentPage();
            selectedIndex_ = 0;
            return UpdateComposition(context, cookie);
        }
        if (key == VK_BACK && cursor_ > 0) {
            typed_.erase(cursor_ - 1, 1);
            --cursor_;
            configuration_ = LoadRuntimeConfiguration();
            allCandidates_ = emojiMode_ ? FindEmojiCandidates(typed_) : FindCandidates(typed_);
            currentPage_ = 0;
            UpdateCurrentPage();
            selectedIndex_ = 0;
            return typed_.empty() ? FinishComposition(cookie, L"", L'\0') : UpdateComposition(context, cookie);
        }
        if (HasShortcut(configuration_.shortcuts.previousPage, key)) return MovePage(context, cookie, -1);
        if (HasShortcut(configuration_.shortcuts.nextPage, key)) return MovePage(context, cookie, 1);
        if (HasShortcut(configuration_.shortcuts.selectPrevious, key)) return MoveSelection(context, cookie, -1);
        if (HasShortcut(configuration_.shortcuts.selectNext, key)) return MoveSelection(context, cookie, 1);
        if (key == VK_LEFT && cursor_ > 0) { --cursor_; return UpdateComposition(context, cookie); }
        if (key == VK_RIGHT && cursor_ < typed_.size()) { ++cursor_; return UpdateComposition(context, cookie); }
        std::size_t candidateIndex{};
        const wchar_t selectionTrailing = configuration_.appendSpaceAfterSelection ? L' ' : L'\0';
        if (enput::TryGetCandidateIndex(key, candidates_.size(), &candidateIndex)) return CommitCandidate(context, cookie, candidates_[candidateIndex], selectionTrailing);
        if (HasShortcut(configuration_.shortcuts.selectCurrent, key) && selectedIndex_ < candidates_.size()) return CommitCandidate(context, cookie, candidates_[selectedIndex_], selectionTrailing);
        if (key == VK_SPACE && detachedSuggestionActive_) return CommitDetachedSuggestion(context, cookie, L"", L' ');
        if (key == VK_SPACE && IsSuggestionActive()) return FinishComposition(cookie, L"", L' ');
        if (key == VK_SPACE) return FinishWithSuggestions(context, cookie, typed_, L' ');
        if (key == VK_RETURN) return FinishComposition(cookie, typed_, L'\r');
        if (key == VK_ESCAPE) return FinishComposition(cookie, typed_, L'\0');
        return S_FALSE;
    }

    HRESULT CommitForPassthrough(ITfContext* context, TfEditCookie cookie) {
        if (detachedSuggestionActive_) return CommitDetachedSuggestion(context, cookie, L"", L'\0');
        return FinishComposition(cookie, typed_, L'\0');
    }

    HRESULT CommitWithCharacter(ITfContext* context, TfEditCookie cookie, wchar_t character) {
        if (detachedSuggestionActive_) return CommitDetachedSuggestion(context, cookie, L"", character);
        return FinishComposition(cookie, typed_ + character, L'\0');
    }

private:
    friend class KeyEditSession;
    friend class CandidateClickEditSession;
    friend class OverlayRefreshEditSession;

    static constexpr UINT kOverlayEventMessage = WM_APP + 73;
    static constexpr wchar_t kOverlayActionWindowClassName[] = L"EnputMethodOverlayActionWindow";

    static LRESULT CALLBACK OverlayActionWindowProc(HWND window, UINT message, WPARAM, LPARAM parameter) {
        if (message == WM_NCCREATE) {
            const auto* create = reinterpret_cast<const CREATESTRUCTW*>(parameter);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(create->lpCreateParams));
            return TRUE;
        }
        auto* self = reinterpret_cast<TextService*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (message == kOverlayEventMessage && self) {
            std::unique_ptr<enput::OverlayEvent> event(reinterpret_cast<enput::OverlayEvent*>(parameter));
            self->HandleOverlayEvent(*event);
            return 0;
        }
        return DefWindowProcW(window, message, 0, parameter);
    }

    void CreateOverlayActionWindow() {
        static const ATOM classAtom = [] {
            WNDCLASSW windowClass{};
            windowClass.lpfnWndProc = OverlayActionWindowProc;
            windowClass.hInstance = g_module;
            windowClass.lpszClassName = kOverlayActionWindowClassName;
            const ATOM registered = RegisterClassW(&windowClass);
            return registered ? registered : static_cast<ATOM>(GetLastError() == ERROR_CLASS_ALREADY_EXISTS ? 1 : 0);
        }();
        if (!classAtom || overlayActionWindow_) return;
        overlayActionWindow_ = CreateWindowExW(0, kOverlayActionWindowClassName, nullptr, 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, g_module, this);
    }

    void DestroyOverlayActionWindow() {
        if (!overlayActionWindow_) return;
        DestroyWindow(overlayActionWindow_);
        overlayActionWindow_ = nullptr;
    }

    void PostOverlayEvent(enput::OverlayEvent event) {
        auto* queuedEvent = new (std::nothrow) enput::OverlayEvent(std::move(event));
        if (!queuedEvent) return;
        if (!overlayActionWindow_ || !PostMessageW(overlayActionWindow_, kOverlayEventMessage, 0, reinterpret_cast<LPARAM>(queuedEvent))) delete queuedEvent;
    }

    void HideOverlay() {
        overlayActive_ = false;
        ++overlayStateId_;
        if (overlayClient_) {
            overlayClient_->Publish("{\"type\":\"hide\",\"clientId\":\"" + overlayClient_->ClientId() + "\",\"stateId\":" + std::to_string(overlayStateId_) + "}");
        }
    }

    void HandleOverlayEvent(const enput::OverlayEvent& event) {
        if (event.type == enput::OverlayEventType::Disconnected) {
            if (!overlayActive_) return;
            overlayActive_ = false;
            enput::WriteOverlayDiagnostic("overlay.disconnected", "native-fallback-disabled");
            return;
        }
        if (event.type == enput::OverlayEventType::Connected) {
            enput::WriteOverlayDiagnostic("overlay.connected", candidates_.empty() ? "no-candidates" : "refresh-current-state");
            if (isFocused_ && !candidates_.empty()) RequestOverlayRefresh();
            return;
        }

        if (!overlayClient_ || event.clientId != overlayClient_->ClientId() || event.stateId != overlayStateId_) return;
        if (event.action == "selectCandidate") HandleCandidateWindowAction(event.candidateIndex);
        else if (event.action == "previousPage") HandleCandidateWindowAction(-1);
        else if (event.action == "nextPage") HandleCandidateWindowAction(-2);
        else if (event.action == "dismiss") HandleCandidateWindowAction(-3);
    }

    void RequestOverlayRefresh();

    void HandleCandidateWindowAction(int action);

    HRESULT ApplyCandidateWindowAction(ITfContext* context, TfEditCookie cookie, const std::wstring& candidate, int action) {
        if (!candidate.empty()) {
            const wchar_t trailing = configuration_.appendSpaceAfterSelection ? L' ' : L'\0';
            return CommitCandidate(context, cookie, candidate, trailing);
        }
        if (action == -1) return MovePage(context, cookie, -1);
        if (action == -2) return MovePage(context, cookie, 1);
        if (action == -3) return detachedSuggestionActive_ ? CommitDetachedSuggestion(context, cookie, L"", L'\0') : FinishComposition(cookie, L"", L'\0');
        return S_FALSE;
    }

    bool ShouldHandleKey(WPARAM key) const {
        if (HasShortcut(configuration_.shortcuts.toggleTranslationWindow, key)) return true;
        if (HasShortcut(configuration_.shortcuts.toggleEmojiMode, key)) return true;
        if (emojiMode_ && key == VK_ESCAPE && typed_.empty()) return true;
        if (IsSuggestionActive() || detachedSuggestionActive_) return !IsModifierKey(key);
        if (!typed_.empty()) return !IsModifierKey(key);
        if (GetKeyState(VK_CONTROL) < 0 || GetKeyState(VK_MENU) < 0) return false;
        return key >= 'A' && key <= 'Z';
    }

    bool ShouldPassThrough(WPARAM key) const {
        if (typed_.empty()) {
            if (!IsSuggestionActive() && !detachedSuggestionActive_) return false;
            const bool hasModifier = GetKeyState(VK_CONTROL) < 0 || GetKeyState(VK_MENU) < 0;
            if (!hasModifier && key >= 'A' && key <= 'Z') return false;
            if (HasShortcut(configuration_.shortcuts.toggleTranslationWindow, key) || key == VK_SPACE || HasShortcut(configuration_.shortcuts.previousPage, key) || HasShortcut(configuration_.shortcuts.nextPage, key)) return false;
            if (HasShortcut(configuration_.shortcuts.selectPrevious, key) || HasShortcut(configuration_.shortcuts.selectNext, key)) return false;
            if (enput::TryGetCandidateIndex(key, candidates_.size(), nullptr)) return false;
            return !(HasShortcut(configuration_.shortcuts.selectCurrent, key) && selectedIndex_ < candidates_.size());
        }
        const bool hasModifier = GetKeyState(VK_CONTROL) < 0 || GetKeyState(VK_MENU) < 0;
        if (HasShortcut(configuration_.shortcuts.toggleTranslationWindow, key)) return false;
        if (HasShortcut(configuration_.shortcuts.toggleEmojiMode, key)) return false;
        if (!hasModifier && key >= 'A' && key <= 'Z') return false;
        if (key == VK_BACK) return cursor_ == 0;
        if (key == VK_SPACE || key == VK_RETURN || key == VK_ESCAPE) return false;
        if (HasShortcut(configuration_.shortcuts.previousPage, key) || HasShortcut(configuration_.shortcuts.nextPage, key)) return false;
        if (HasShortcut(configuration_.shortcuts.selectPrevious, key) || HasShortcut(configuration_.shortcuts.selectNext, key)) return false;
        if (key == VK_LEFT) return cursor_ == 0;
        if (key == VK_RIGHT) return cursor_ == typed_.size();
        if (enput::TryGetCandidateIndex(key, candidates_.size(), nullptr)) return false;
        return !(HasShortcut(configuration_.shortcuts.selectCurrent, key) && selectedIndex_ < candidates_.size());
    }

    static wchar_t PrintableCharacter(WPARAM key, LPARAM keyData) {
        BYTE keyboardState[256]{};
        if (!GetKeyboardState(keyboardState)) return L'\0';
        wchar_t character{};
        const UINT scanCode = static_cast<UINT>((keyData >> 16) & 0xff);
        const int result = ToUnicodeEx(static_cast<UINT>(key), scanCode, keyboardState, &character, 1, 0, GetKeyboardLayout(0));
        return result == 1 && iswprint(character) ? character : L'\0';
    }

    static bool IsModifierKey(WPARAM key) {
        return key == VK_SHIFT || key == VK_LSHIFT || key == VK_RSHIFT ||
               key == VK_CONTROL || key == VK_LCONTROL || key == VK_RCONTROL ||
               key == VK_MENU || key == VK_LMENU || key == VK_RMENU ||
               key == VK_LWIN || key == VK_RWIN || key == VK_CAPITAL;
    }

    static void ToLowerInPlace(std::wstring* text) {
        std::transform(text->begin(), text->end(), text->begin(), [](wchar_t character) { return static_cast<wchar_t>(towlower(character)); });
    }

    static void ToUpperInPlace(std::wstring* text) {
        std::transform(text->begin(), text->end(), text->begin(), [](wchar_t character) { return static_cast<wchar_t>(towupper(character)); });
    }

    static bool IsAllUpper(const std::wstring& text) {
        bool hasLetter = false;
        for (const wchar_t character : text) {
            if (!iswalpha(character)) continue;
            hasLetter = true;
            if (!iswupper(character)) return false;
        }
        return hasLetter;
    }

    static bool IsTitleCase(const std::wstring& text) {
        if (text.empty() || !iswupper(text.front())) return false;
        return std::all_of(text.begin() + 1, text.end(), [](wchar_t character) { return !iswalpha(character) || iswlower(character); });
    }

    static std::wstring DisplayCandidate(const std::wstring& candidate, const std::wstring& typed, const RuntimeConfiguration& configuration) {
        if (candidate.find(static_cast<wchar_t>(0x1F)) != std::wstring::npos) return candidate;
        std::wstring display = candidate;
        if (configuration.preserveCase && IsAllUpper(typed)) {
            ToUpperInPlace(&display);
        } else if (configuration.preserveCase && IsTitleCase(typed)) {
            ToLowerInPlace(&display);
            if (!display.empty()) display.front() = static_cast<wchar_t>(towupper(display.front()));
        } else if (configuration.preserveCase && !typed.empty() && std::all_of(typed.begin(), typed.end(), [](wchar_t character) { return !iswalpha(character) || iswlower(character); })) {
            ToLowerInPlace(&display);
        }
        return display;
    }

    std::vector<std::wstring> FindCandidates(const std::wstring& typed) {
        if (typed.empty()) return {};
        std::wstring lower = typed;
        ToLowerInPlace(&lower);
        std::vector<std::wstring> exactMatches;
        std::vector<std::wstring> prefixMatches;
        std::unordered_set<std::wstring> exactKeys;
        std::unordered_set<std::wstring> prefixKeys;
        const auto appendUnique = [](std::vector<std::wstring>* candidates, std::unordered_set<std::wstring>* keys, const std::wstring& candidate) {
            const std::wstring lowerCandidate = Lowercase(candidate);
            if (keys->insert(lowerCandidate).second) candidates->push_back(candidate);
        };
        for (const std::wstring& word : LoadDictionary()) {
            std::wstring candidate = word;
            ToLowerInPlace(&candidate);
            if (!candidate.starts_with(lower)) continue;
            if (candidate == lower) appendUnique(&exactMatches, &exactKeys, word);
            else appendUnique(&prefixMatches, &prefixKeys, word);
        }
        for (const SuggestionEntry& entry : LoadSuggestionDictionary()) {
            std::vector<std::wstring> phrases = entry.phrases;
            if (entry.text.find(L' ') != std::wstring::npos) phrases.insert(phrases.begin(), entry.text);
            for (const std::wstring& phrase : phrases) {
                const std::wstring candidate = Lowercase(phrase);
                if (!candidate.starts_with(lower)) continue;
                if (candidate == lower) appendUnique(&exactMatches, &exactKeys, phrase);
                else appendUnique(&prefixMatches, &prefixKeys, phrase);
            }
        }
        std::vector<std::wstring> matches;
        if (configuration_.adaptiveCandidateRanking) {
            const enput::CandidateFrequencyMap& frequencies = CandidateFrequencies();
            enput::RankCandidatesByFrequency(&exactMatches, frequencies);
            enput::RankCandidatesByFrequency(&prefixMatches, frequencies);
        }
        matches.reserve(exactMatches.size() + prefixMatches.size());
        matches.insert(matches.end(), exactMatches.begin(), exactMatches.end());
        matches.insert(matches.end(), prefixMatches.begin(), prefixMatches.end());
        return matches;
    }

    std::vector<std::wstring> FindAssociatedCandidates(const std::wstring& committedText) {
        const std::wstring lower = Lowercase(committedText);
        const std::wstring phrasePrefix = lower + L" ";
        std::vector<std::wstring> matches;
        std::unordered_set<std::wstring> keys;
        const auto appendUnique = [&matches, &keys](const std::wstring& candidate) {
            const std::wstring lowerCandidate = Lowercase(candidate);
            if (keys.insert(lowerCandidate).second) matches.push_back(candidate);
        };
        for (const SuggestionEntry& entry : LoadSuggestionDictionary()) {
            if (Lowercase(entry.text) != lower) continue;
            for (const std::wstring& candidate : entry.next) appendUnique(candidate);
            for (const std::wstring& candidate : entry.phrases) {
                const std::wstring lowerCandidate = Lowercase(candidate);
                if (lowerCandidate.starts_with(phrasePrefix)) appendUnique(candidate.substr(committedText.size() + 1));
            }
        }
        if (matches.empty()) {
            static const std::vector<std::wstring> fallback{ L"the", L"to", L"and", L"a", L"is", L"of", L"for", L"in", L"that" };
            matches = fallback;
        }
        if (configuration_.adaptiveCandidateRanking) enput::RankCandidatesByFrequency(&matches, CandidateFrequencies());
        return matches;
    }

    static std::vector<std::wstring> FindEmojiCandidates(const std::wstring& typed) {
        const std::wstring lower = Lowercase(typed);
        std::vector<std::wstring> matches;
        for (const EmojiEntry& entry : LoadEmojiDictionary()) {
            const bool matchesKeyword = std::any_of(entry.keywords.begin(), entry.keywords.end(), [&lower](const std::wstring& keyword) { return Lowercase(keyword).starts_with(lower); });
            if (!matchesKeyword || std::any_of(matches.begin(), matches.end(), [&entry](const std::wstring& existing) { return existing.starts_with(entry.emoji); })) continue;
            std::wstring candidate = entry.emoji;
            candidate += static_cast<wchar_t>(0x1F);
            for (size_t index = 0; index < entry.keywords.size(); ++index) {
                if (index) candidate += L", ";
                candidate += entry.keywords[index];
            }
            matches.push_back(std::move(candidate));
        }
        return matches;
    }

    size_t PageCount() const {
        const size_t pageSize = static_cast<size_t>(configuration_.candidateCount);
        return pageSize ? (allCandidates_.size() + pageSize - 1) / pageSize : 0;
    }

    void UpdateCurrentPage() {
        candidates_.clear();
        const size_t pageCount = PageCount();
        if (!pageCount) { currentPage_ = 0; return; }
        currentPage_ = (std::min)(currentPage_, pageCount - 1);
        const size_t first = currentPage_ * static_cast<size_t>(configuration_.candidateCount);
        const size_t last = (std::min)(allCandidates_.size(), first + static_cast<size_t>(configuration_.candidateCount));
        for (auto candidate = allCandidates_.begin() + first; candidate != allCandidates_.begin() + last; ++candidate) {
            candidates_.push_back(DisplayCandidate(*candidate, typed_, configuration_));
        }
    }

    HRESULT MovePage(ITfContext* context, TfEditCookie cookie, int direction) {
        const size_t pageCount = PageCount();
        if (pageCount < 2) return S_OK;
        const size_t previousPage = currentPage_;
        if (direction < 0 && currentPage_ > 0) --currentPage_;
        if (direction > 0 && currentPage_ + 1 < pageCount) ++currentPage_;
        if (currentPage_ == previousPage) return S_OK;
        UpdateCurrentPage();
        selectedIndex_ = 0;
        return RefreshCandidatesAtCurrentPosition(context, cookie);
    }

    HRESULT MoveSelection(ITfContext* context, TfEditCookie cookie, int direction) {
        if (candidates_.empty()) return S_OK;
        bool changed = false;
        if (direction < 0 && selectedIndex_ > 0) { --selectedIndex_; changed = true; }
        else if (direction > 0 && selectedIndex_ + 1 < candidates_.size()) { ++selectedIndex_; changed = true; }
        else if (direction < 0 && currentPage_ > 0) {
            --currentPage_;
            UpdateCurrentPage();
            selectedIndex_ = candidates_.empty() ? 0 : candidates_.size() - 1;
            changed = true;
        } else if (direction > 0 && currentPage_ + 1 < PageCount()) {
            ++currentPage_;
            UpdateCurrentPage();
            selectedIndex_ = 0;
            changed = true;
        }
        return changed ? RefreshCandidatesAtCurrentPosition(context, cookie) : S_OK;
    }

    std::string CandidateOverlayMessage(const RECT& bounds, std::uintptr_t ownerWindow, std::uint64_t stateId) const {
        std::string message = "{\"type\":\"showCandidates\",\"clientId\":\"" + overlayClient_->ClientId() +
            "\",\"stateId\":" + std::to_string(stateId) + ",\"candidates\":{\"x\":" + std::to_string(bounds.left) +
            ",\"y\":" + std::to_string(bounds.top) + ",\"ownerWindow\":" + std::to_string(ownerWindow) + ",\"items\":[";
        for (size_t index = 0; index < candidates_.size(); ++index) {
            if (index) message += ',';
            std::wstring visible = candidates_[index];
            std::replace(visible.begin(), visible.end(), static_cast<wchar_t>(0x1F), L' ');
            message += JsonString(visible);
        }
        message += "],\"page\":" + std::to_string(currentPage_) + ",\"pageCount\":" + std::to_string(PageCount()) +
            ",\"selectedIndex\":" + std::to_string(selectedIndex_) + ",\"capsLock\":" + ((GetKeyState(VK_CAPITAL) & 1) != 0 ? "true" : "false") +
            ",\"layout\":\"" + (configuration_.horizontal ? "horizontal" : "vertical") + "\"";
        if (emojiMode_) message += ",\"modeMarker\":\"EMOJI\"";
        message += ",\"theme\":" + OverlayThemeJson();
        return message + "}}";
    }

    static std::string ColorJson(COLORREF color) {
        char text[10]{};
        std::snprintf(text, sizeof(text), "\"#%02x%02x%02x\"", GetRValue(color), GetGValue(color), GetBValue(color));
        return text;
    }

    std::string OverlayThemeJson() const {
        const ThemeStyle& theme = configuration_.theme;
        return "{\"background\":" + ColorJson(theme.background) + ",\"foreground\":" + ColorJson(theme.foreground) +
            ",\"border\":" + ColorJson(theme.border) + ",\"selectedBackground\":" + ColorJson(theme.selectedBackground) +
            ",\"selectedForeground\":" + ColorJson(theme.selectedForeground) + ",\"translationBackground\":" + ColorJson(theme.translationBackground) +
            ",\"translationForeground\":" + ColorJson(theme.translationForeground) + ",\"translationTitleForeground\":" + ColorJson(theme.translationTitleForeground) +
            ",\"translationBorder\":" + ColorJson(theme.translationBorder) + ",\"fontFamily\":" + JsonString(configuration_.fontFamily) +
            ",\"fontSize\":" + std::to_string(configuration_.fontSize) + ",\"opacity\":" + std::to_string(configuration_.opacity) +
            ",\"borderWidth\":" + std::to_string(theme.borderWidth) + ",\"cornerRadius\":" + std::to_string(theme.cornerRadius) +
            ",\"padding\":" + std::to_string(theme.padding) + ",\"rowHeight\":" + std::to_string(theme.rowHeight) +
            ",\"translationBorderWidth\":" + std::to_string(theme.translationBorderWidth) + ",\"translationCornerRadius\":" + std::to_string(theme.translationCornerRadius) +
            ",\"translationPadding\":" + std::to_string(theme.translationPadding) + ",\"translationWidth\":" + std::to_string(theme.translationWidth) +
            ",\"translationMaxHeight\":" + std::to_string(theme.translationMaxHeight) + "}";
    }

    std::string TranslationOverlayMessage(const TranslationEntry& entry, const RECT& candidateBounds, std::uintptr_t ownerWindow, std::uint64_t stateId) const {
        std::wstring content;
        if (!entry.partsOfSpeech.empty()) {
            for (size_t index = 0; index < entry.partsOfSpeech.size(); ++index) {
                if (index) content += L", ";
                content += entry.partsOfSpeech[index];
            }
        }
        for (const auto& [language, meanings] : entry.translations) {
            if (!content.empty()) content += L'\n';
            content += language + L": ";
            for (size_t index = 0; index < meanings.size(); ++index) {
                if (index) content += L"; ";
                content += meanings[index];
            }
        }
        if (!entry.example.empty()) {
            if (!content.empty()) content += L'\n';
            content += L"Example: ";
            content += entry.example;
        }
        if (!entry.source.empty()) {
            if (!content.empty()) content += L'\n';
            content += L"Source: ";
            content += entry.source;
        }
        return "{\"type\":\"showTranslation\",\"clientId\":\"" + overlayClient_->ClientId() + "\",\"stateId\":" +
            std::to_string(stateId) + ",\"translation\":{\"title\":" + JsonString(entry.text) + ",\"content\":" + JsonString(content) +
            ",\"candidateRight\":" + std::to_string(candidateBounds.right) + ",\"candidateTop\":" + std::to_string(candidateBounds.top) + ",\"ownerWindow\":" + std::to_string(ownerWindow) +
            ",\"theme\":" + OverlayThemeJson() + "}}";
    }

    void PresentCandidates(ITfContext* context, TfEditCookie cookie, ITfRange* range) {
        if (!isFocused_) return;
        overlayActive_ = false;
        if (candidates_.empty()) {
            HideOverlay();
            return;
        }
        if (!overlayClient_ || !overlayClient_->IsConnected()) {
            enput::WriteOverlayDiagnostic("candidate.skipped", "overlay-not-connected");
            return;
        }
        ITfContextView* view{};
        if (FAILED(context->GetActiveView(&view))) {
            enput::WriteOverlayDiagnostic("candidate.skipped", "active-view-unavailable");
            return;
        }
        RECT textBounds{};
        HWND ownerWindow{};
        view->GetWnd(&ownerWindow);
        BOOL clipped{};
        const HRESULT boundsHr = view->GetTextExt(cookie, range, &textBounds, &clipped);
        view->Release();
        if (FAILED(boundsHr)) {
            enput::WriteOverlayDiagnostic("candidate.skipped", "text-bounds-unavailable");
            return;
        }
        RECT candidateBounds{ textBounds.left, textBounds.bottom + 2, textBounds.right, textBounds.bottom };
        const TranslationEntry* translation = translationEnabled_ && selectedIndex_ < candidates_.size() ? FindTranslation(candidates_[selectedIndex_]) : nullptr;
        const std::uint64_t stateId = ++overlayStateId_;
        std::vector<std::string> messages{ CandidateOverlayMessage(candidateBounds, reinterpret_cast<std::uintptr_t>(ownerWindow), stateId) };
        if (translation) messages.push_back(TranslationOverlayMessage(*translation, candidateBounds, reinterpret_cast<std::uintptr_t>(ownerWindow), stateId));
        else messages.push_back("{\"type\":\"hide\",\"clientId\":\"" + overlayClient_->ClientId() + "\",\"stateId\":" + std::to_string(stateId) + ",\"surface\":\"translation\"}");
        if (!overlayClient_->PublishBatch(std::move(messages))) {
            enput::WriteOverlayDiagnostic("candidate.skipped", "publish-failed");
            return;

        }
        enput::WriteOverlayDiagnostic("candidate.published", "state=" + std::to_string(stateId) + " items=" + std::to_string(candidates_.size()));
    }
    HRESULT UpdateComposition(ITfContext* context, TfEditCookie cookie) {
        if (!composition_) {
            ITfContextComposition* compositions{};
            HRESULT hr = context->QueryInterface(IID_PPV_ARGS(&compositions));
            if (FAILED(hr)) return hr;
            TF_SELECTION selection{}; ULONG fetched{};
            hr = context->GetSelection(cookie, TF_DEFAULT_SELECTION, 1, &selection, &fetched);
            if (SUCCEEDED(hr) && fetched == 1) {
                hr = compositions->StartComposition(cookie, selection.range, static_cast<ITfCompositionSink*>(this), &composition_);
                selection.range->Release();
                if (SUCCEEDED(hr)) { compositionContext_ = context; compositionContext_->AddRef(); }
            }
            compositions->Release();
            if (FAILED(hr)) return hr;
        }
        ITfRange* range{}; HRESULT hr = composition_->GetRange(&range);
        if (FAILED(hr)) return hr;
        hr = range->SetText(cookie, 0, typed_.data(), static_cast<LONG>(typed_.size()));
        if (SUCCEEDED(hr)) {
            ITfRange* caret{}; hr = range->Clone(&caret);
            if (SUCCEEDED(hr)) {
                caret->Collapse(cookie, TF_ANCHOR_START);
                LONG shifted{};
                hr = caret->ShiftStart(cookie, static_cast<LONG>(cursor_), &shifted, nullptr);
                if (SUCCEEDED(hr)) caret->Collapse(cookie, TF_ANCHOR_START);
                TF_SELECTION selection{ caret, { TF_AE_NONE, FALSE } };
                if (SUCCEEDED(hr)) hr = context->SetSelection(cookie, 1, &selection);
                caret->Release();
            }
        }
        if (SUCCEEDED(hr)) PresentCandidates(context, cookie, range);
        range->Release();
        return hr;
    }

    HRESULT FinishComposition(TfEditCookie cookie, const std::wstring& committedText, wchar_t trailing) {
        if (!composition_) return S_FALSE;
        ITfRange* range{}; HRESULT hr = composition_->GetRange(&range);
        std::wstring finalText = committedText;
        if (trailing) finalText += trailing;
        if (SUCCEEDED(hr)) { hr = range->SetText(cookie, 0, finalText.data(), static_cast<LONG>(finalText.size())); range->Release(); }
        ITfComposition* composition = composition_; composition_ = nullptr;
        typed_.clear(); cursor_ = 0; allCandidates_.clear(); candidates_.clear(); currentPage_ = 0; selectedIndex_ = 0; HideOverlay(); candidateWindow_.Hide(); translationWindow_.Hide();
        if (compositionContext_) { compositionContext_->Release(); compositionContext_ = nullptr; }
        if (SUCCEEDED(hr)) hr = composition->EndComposition(cookie);
        composition->Release();
        return hr;
    }

    HRESULT FinishWithSuggestions(ITfContext* context, TfEditCookie cookie, const std::wstring& committedText, wchar_t trailing) {
        const HRESULT hr = FinishComposition(cookie, committedText, trailing);
        if (FAILED(hr) || emojiMode_) return hr;
        lastCommittedText_ = committedText;
        allCandidates_ = FindAssociatedCandidates(committedText);
        if (allCandidates_.empty()) return hr;
        typed_.clear();
        cursor_ = 0;
        currentPage_ = 0;
        selectedIndex_ = 0;
        UpdateCurrentPage();
        detachedSuggestionActive_ = true;
        detachedSuggestionContext_ = context;
        detachedSuggestionContext_->AddRef();
        return ShowDetachedSuggestions(context, cookie);
    }

    bool IsSuggestionActive() const {
        return composition_ && typed_.empty() && !candidates_.empty();
    }

    void ClearDetachedSuggestions() {
        detachedSuggestionActive_ = false;
        if (detachedSuggestionContext_) { detachedSuggestionContext_->Release(); detachedSuggestionContext_ = nullptr; }
    }

    HRESULT ShowDetachedSuggestions(ITfContext* context, TfEditCookie cookie) {
        if (!detachedSuggestionActive_ || candidates_.empty()) return S_OK;
        TF_SELECTION selection{}; ULONG fetched{};
        HRESULT hr = context->GetSelection(cookie, TF_DEFAULT_SELECTION, 1, &selection, &fetched);
        if (FAILED(hr) || fetched != 1) return FAILED(hr) ? hr : E_FAIL;
        PresentCandidates(context, cookie, selection.range);
        selection.range->Release();
        return S_OK;
    }

    HRESULT RefreshCandidates(ITfContext* context, TfEditCookie cookie) {
        return detachedSuggestionActive_ ? ShowDetachedSuggestions(context, cookie) : UpdateComposition(context, cookie);
    }

    HRESULT RefreshCandidatesAtCurrentPosition(ITfContext* context, TfEditCookie cookie) {
        const bool previous = keepCandidateWindowPosition_;
        keepCandidateWindowPosition_ = true;
        const HRESULT hr = RefreshCandidates(context, cookie);
        keepCandidateWindowPosition_ = previous;
        return hr;
    }

    HRESULT CommitDetachedSuggestion(ITfContext* context, TfEditCookie cookie, const std::wstring& committedText, wchar_t trailing) {
        TF_SELECTION selection{}; ULONG fetched{};
        HRESULT hr = context->GetSelection(cookie, TF_DEFAULT_SELECTION, 1, &selection, &fetched);
        if (FAILED(hr) || fetched != 1) return FAILED(hr) ? hr : E_FAIL;
        std::wstring finalText = committedText;
        if (trailing) finalText += trailing;
        hr = selection.range->SetText(cookie, 0, finalText.data(), static_cast<LONG>(finalText.size()));
        selection.range->Release();
        ClearDetachedSuggestions();
        HideOverlay();
        candidateWindow_.Hide();
        translationWindow_.Hide();
        if (FAILED(hr) || committedText.empty()) return hr;
        allCandidates_ = FindAssociatedCandidates(committedText);
        currentPage_ = 0;
        selectedIndex_ = 0;
        UpdateCurrentPage();
        if (allCandidates_.empty()) return hr;
        detachedSuggestionActive_ = true;
        detachedSuggestionContext_ = context;
        detachedSuggestionContext_->AddRef();
        return ShowDetachedSuggestions(context, cookie);
    }

    HRESULT CommitCandidate(ITfContext* context, TfEditCookie cookie, const std::wstring& candidate, wchar_t trailing) {
        const size_t separator = candidate.find(static_cast<wchar_t>(0x1F));
        const std::wstring committed = separator == std::wstring::npos ? candidate : candidate.substr(0, separator);
        if (separator == std::wstring::npos && configuration_.adaptiveCandidateRanking) RecordCandidateSelection(committed);
        return detachedSuggestionActive_ ? CommitDetachedSuggestion(context, cookie, committed, trailing) : FinishWithSuggestions(context, cookie, committed, trailing);
    }

    void ClearComposition() {
        if (composition_) { composition_->Release(); composition_ = nullptr; }
        ClearDetachedSuggestions();
        if (compositionContext_) { compositionContext_->Release(); compositionContext_ = nullptr; }
        typed_.clear(); cursor_ = 0; allCandidates_.clear(); candidates_.clear(); currentPage_ = 0; selectedIndex_ = 0; HideOverlay(); candidateWindow_.Hide(); translationWindow_.Hide();
    }

    long refs_ = 1;
    ITfThreadMgr* threadManager_ = nullptr;
    TfClientId clientId_ = TF_CLIENTID_NULL;
    ITfComposition* composition_ = nullptr;
    ITfContext* compositionContext_ = nullptr;
    ITfContext* detachedSuggestionContext_ = nullptr;
    std::wstring typed_;
    size_t cursor_ = 0;
    std::vector<std::wstring> allCandidates_;
    std::vector<std::wstring> candidates_;
    size_t currentPage_ = 0;
    size_t selectedIndex_ = 0;
    std::wstring lastCommittedText_;
    bool emojiMode_ = false;
    bool translationEnabled_ = false;
    bool detachedSuggestionActive_ = false;
    bool isFocused_ = true;
    bool keepCandidateWindowPosition_ = false;
    bool overlayActive_ = false;
    std::uint64_t overlayStateId_ = 0;
    HWND overlayActionWindow_ = nullptr;
    RuntimeConfiguration configuration_{};
    std::unique_ptr<enput::OverlayClient> overlayClient_;
    CandidateWindow candidateWindow_;
    TranslationWindow translationWindow_;
};

class KeyEditSession final : public ITfEditSession {
public:
    KeyEditSession(TextService* service, ITfContext* context, WPARAM key, bool passThrough, wchar_t printableCharacter) : service_(service), context_(context), key_(key), passThrough_(passThrough), printableCharacter_(printableCharacter) { service_->AddRef(); context_->AddRef(); }
    ~KeyEditSession() { context_->Release(); service_->Release(); }
    STDMETHODIMP QueryInterface(REFIID iid, void** result) override { if (!result) return E_INVALIDARG; *result = nullptr; if (iid != IID_IUnknown && iid != IID_ITfEditSession) return E_NOINTERFACE; *result = static_cast<ITfEditSession*>(this); AddRef(); return S_OK; }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override { const auto refs = InterlockedDecrement(&refs_); if (!refs) delete this; return refs; }
    STDMETHODIMP DoEditSession(TfEditCookie cookie) override { return passThrough_ ? service_->CommitForPassthrough(context_, cookie) : printableCharacter_ ? service_->CommitWithCharacter(context_, cookie, printableCharacter_) : service_->ApplyKey(context_, cookie, key_); }
private: long refs_ = 1; TextService* service_; ITfContext* context_; WPARAM key_; bool passThrough_; wchar_t printableCharacter_;
};

class CandidateClickEditSession final : public ITfEditSession {
public:
    CandidateClickEditSession(TextService* service, ITfContext* context, std::wstring candidate, int action) : service_(service), context_(context), candidate_(std::move(candidate)), action_(action) { service_->AddRef(); context_->AddRef(); }
    ~CandidateClickEditSession() { context_->Release(); service_->Release(); }
    STDMETHODIMP QueryInterface(REFIID iid, void** result) override { if (!result) return E_INVALIDARG; *result = nullptr; if (iid != IID_IUnknown && iid != IID_ITfEditSession) return E_NOINTERFACE; *result = static_cast<ITfEditSession*>(this); AddRef(); return S_OK; }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override { const auto refs = InterlockedDecrement(&refs_); if (!refs) delete this; return refs; }
    STDMETHODIMP DoEditSession(TfEditCookie cookie) override { return service_->ApplyCandidateWindowAction(context_, cookie, candidate_, action_); }
private:
    long refs_ = 1;
    TextService* service_;
    ITfContext* context_;
    std::wstring candidate_;
    int action_;
};

class OverlayRefreshEditSession final : public ITfEditSession {
public:
    OverlayRefreshEditSession(TextService* service, ITfContext* context) : service_(service), context_(context) { service_->AddRef(); context_->AddRef(); }
    ~OverlayRefreshEditSession() { context_->Release(); service_->Release(); }
    STDMETHODIMP QueryInterface(REFIID iid, void** result) override { if (!result) return E_INVALIDARG; *result = nullptr; if (iid != IID_IUnknown && iid != IID_ITfEditSession) return E_NOINTERFACE; *result = static_cast<ITfEditSession*>(this); AddRef(); return S_OK; }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override { const auto refs = InterlockedDecrement(&refs_); if (!refs) delete this; return refs; }
    STDMETHODIMP DoEditSession(TfEditCookie cookie) override {
        if (!service_->isFocused_ || service_->candidates_.empty() || (context_ != service_->compositionContext_ && context_ != service_->detachedSuggestionContext_)) return S_OK;
        return service_->RefreshCandidates(context_, cookie);
    }
private:
    long refs_ = 1;
    TextService* service_;
    ITfContext* context_;
};

void TextService::HandleCandidateWindowAction(int action) {
    ITfContext* context = composition_ ? compositionContext_ : detachedSuggestionContext_;
    if (!context) return;
    const std::wstring candidate = action >= 0 && action < static_cast<int>(candidates_.size()) ? candidates_[action] : std::wstring{};
    auto* session = new (std::nothrow) CandidateClickEditSession(this, context, candidate, action);
    if (!session) return;
    HRESULT sessionResult{};
    context->RequestEditSession(clientId_, session, TF_ES_ASYNC | TF_ES_READWRITE, &sessionResult);
    session->Release();
}

void TextService::RequestOverlayRefresh() {
    ITfContext* context = composition_ ? compositionContext_ : detachedSuggestionContext_;
    if (!context || candidates_.empty()) return;
    auto* session = new (std::nothrow) OverlayRefreshEditSession(this, context);
    if (!session) return;
    HRESULT sessionResult{};
    context->RequestEditSession(clientId_, session, TF_ES_ASYNC | TF_ES_READWRITE, &sessionResult);
    session->Release();
}

STDMETHODIMP TextService::OnKeyDown(ITfContext* context, WPARAM key, LPARAM keyData, BOOL* eaten) {
    if (!eaten) return E_INVALIDARG;
    *eaten = FALSE;
    if (!ShouldHandleKey(key)) return S_OK;
    const bool shouldPassThrough = ShouldPassThrough(key);
    const wchar_t printableCharacter = shouldPassThrough ? PrintableCharacter(key, keyData) : L'\0';
    const bool passThrough = shouldPassThrough && !printableCharacter;
    auto* session = new (std::nothrow) KeyEditSession(this, context, key, passThrough, printableCharacter);
    if (!session) return E_OUTOFMEMORY;
    HRESULT sessionResult{}; const HRESULT hr = context->RequestEditSession(clientId_, session, TF_ES_SYNC | TF_ES_READWRITE, &sessionResult);
    session->Release();
    *eaten = !passThrough && SUCCEEDED(hr) && SUCCEEDED(sessionResult);
    return hr;
}

class ClassFactory final : public IClassFactory {
public:
    STDMETHODIMP QueryInterface(REFIID iid, void** result) override { if (!result) return E_INVALIDARG; *result = nullptr; if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE; *result = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override { const auto refs = InterlockedDecrement(&refs_); if (!refs) delete this; return refs; }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** result) override { if (outer) return CLASS_E_NOAGGREGATION; auto* service = new (std::nothrow) TextService(); if (!service) return E_OUTOFMEMORY; HRESULT hr = service->QueryInterface(iid, result); service->Release(); return hr; }
    STDMETHODIMP LockServer(BOOL lock) override { lock ? InterlockedIncrement(&g_lockCount) : InterlockedDecrement(&g_lockCount); return S_OK; }
private: long refs_ = 1;
};

std::wstring GuidText(REFGUID guid) { wchar_t text[39]{}; StringFromGUID2(guid, text, ARRAYSIZE(text)); return text; }
std::wstring ClassKey() { return L"Software\\Classes\\CLSID\\" + GuidText(kTextServiceClsid); }
HRESULT InstalledDllPath(std::wstring* path) {
    wchar_t programFiles[MAX_PATH]{};
    if (!GetEnvironmentVariableW(L"ProgramFiles", programFiles, ARRAYSIZE(programFiles))) return HRESULT_FROM_WIN32(GetLastError());
    const std::wstring directory = std::wstring(programFiles) + L"\\Enput Method";
    if (!CreateDirectoryW(directory.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS) return HRESULT_FROM_WIN32(GetLastError());
    wchar_t source[MAX_PATH]{};
    if (!GetModuleFileNameW(g_module, source, ARRAYSIZE(source))) return HRESULT_FROM_WIN32(GetLastError());
    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    if (!GetFileAttributesExW(source, GetFileExInfoStandard, &attributes)) return HRESULT_FROM_WIN32(GetLastError());
    // TSF DLLs remain mapped while a profile is active. Each produced binary gets
    // a stable, unique deployment name; re-installing the same binary can reuse it.
    wchar_t filename[96]{};
    swprintf_s(filename, L"\\EnputMethod.Tsf.%08lX%08lX.%08lX%08lX.dll",
               attributes.ftLastWriteTime.dwHighDateTime, attributes.ftLastWriteTime.dwLowDateTime,
               attributes.nFileSizeHigh, attributes.nFileSizeLow);
    *path = directory + filename;
    return S_OK;
}

bool FilesEqual(const std::wstring& left, const std::wstring& right) {
    return ReadUtf8File(left) == ReadUtf8File(right);
}

HRESULT RegisterComServer() {
    wchar_t source[MAX_PATH]{}; if (!GetModuleFileNameW(g_module, source, ARRAYSIZE(source))) return HRESULT_FROM_WIN32(GetLastError());
    std::wstring path; HRESULT hr = InstalledDllPath(&path); if (FAILED(hr)) return hr;
    if (_wcsicmp(source, path.c_str()) != 0 && !CopyFileW(source, path.c_str(), FALSE)) {
        const DWORD error = GetLastError();
        if ((error != ERROR_FILE_EXISTS && error != ERROR_SHARING_VIOLATION) || !FilesEqual(source, path)) return HRESULT_FROM_WIN32(error);
    }
    // Older builds registered the service per-user. HKCR gives that stale entry precedence over this machine-wide entry.
    RegDeleteTreeW(HKEY_CURRENT_USER, ClassKey().c_str());
    HKEY key{}; const auto classKey = ClassKey(); LONG status = RegCreateKeyExW(HKEY_LOCAL_MACHINE, classKey.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr);
    if (status != ERROR_SUCCESS) return HRESULT_FROM_WIN32(status);
    const wchar_t* name = L"Enput Method English Input"; RegSetValueExW(key, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(name), static_cast<DWORD>((wcslen(name) + 1) * sizeof(wchar_t))); RegCloseKey(key);
    const auto inprocKey = classKey + L"\\InprocServer32"; status = RegCreateKeyExW(HKEY_LOCAL_MACHINE, inprocKey.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr);
    if (status != ERROR_SUCCESS) return HRESULT_FROM_WIN32(status);
    RegSetValueExW(key, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(path.c_str()), static_cast<DWORD>((path.size() + 1) * sizeof(wchar_t)));
    const wchar_t* apartment = L"Apartment"; RegSetValueExW(key, L"ThreadingModel", 0, REG_SZ, reinterpret_cast<const BYTE*>(apartment), static_cast<DWORD>((wcslen(apartment) + 1) * sizeof(wchar_t))); RegCloseKey(key); return S_OK;
}
HRESULT RegisterProfile() {
    ITfInputProcessorProfiles* profiles{}; HRESULT hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&profiles));
    if (FAILED(hr)) return hr;
    std::wstring path; HRESULT pathHr = InstalledDllPath(&path); if (FAILED(pathHr)) { profiles->Release(); return pathHr; } const wchar_t* description = L"Enput Method - English";
    hr = profiles->Register(kTextServiceClsid);
    if (hr == TF_E_ALREADY_EXISTS) hr = S_OK;
    if (FAILED(hr)) { profiles->Release(); return hr; }
    profiles->RemoveLanguageProfile(kTextServiceClsid, kLegacyEnglishUs, kProfileGuid);
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, L"Software\\Microsoft\\CTF\\TIP\\{9C8945D5-01DF-48F4-A8DB-57E8B6A1EB10}\\LanguageProfile\\0x00000409");
    hr = profiles->AddLanguageProfile(kTextServiceClsid, kChineseSimplified, kProfileGuid, description, static_cast<ULONG>(wcslen(description)), path.c_str(), static_cast<ULONG>(path.size()), 0);
    if (hr == TF_E_ALREADY_EXISTS) hr = S_OK;
    if (SUCCEEDED(hr)) hr = profiles->EnableLanguageProfile(kTextServiceClsid, kChineseSimplified, kProfileGuid, TRUE);
    profiles->Release(); if (FAILED(hr)) return hr;
    ITfCategoryMgr* categories{}; hr = CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&categories));
    if (FAILED(hr)) return hr;
    hr = categories->RegisterCategory(kTextServiceClsid, GUID_TFCAT_TIP_KEYBOARD, kTextServiceClsid);
    if (SUCCEEDED(hr)) hr = categories->RegisterCategory(kTextServiceClsid, GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT, kTextServiceClsid);
    categories->Release();
    return hr;
}
HRESULT RemoveProfile() {
    ITfInputProcessorProfiles* profiles{}; HRESULT hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&profiles));
    if (SUCCEEDED(hr)) { profiles->RemoveLanguageProfile(kTextServiceClsid, kChineseSimplified, kProfileGuid); profiles->RemoveLanguageProfile(kTextServiceClsid, kLegacyEnglishUs, kProfileGuid); profiles->Unregister(kTextServiceClsid); profiles->Release(); }
    ITfCategoryMgr* categories{}; if (SUCCEEDED(CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&categories)))) { categories->UnregisterCategory(kTextServiceClsid, GUID_TFCAT_TIP_KEYBOARD, kTextServiceClsid); categories->UnregisterCategory(kTextServiceClsid, GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT, kTextServiceClsid); categories->Release(); }
    RegDeleteTreeW(HKEY_CURRENT_USER, ClassKey().c_str());
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, ClassKey().c_str()); std::wstring path; if (SUCCEEDED(InstalledDllPath(&path))) DeleteFileW(path.c_str()); return S_OK;
}
}

extern "C" HRESULT WINAPI InstallEnglishInputMethod() { HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED); const bool uninitialize = SUCCEEDED(hr); if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) return hr; hr = RegisterComServer(); if (SUCCEEDED(hr)) hr = RegisterProfile(); if (uninitialize) CoUninitialize(); return hr; }
extern "C" HRESULT WINAPI UninstallEnglishInputMethod() { HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED); const bool uninitialize = SUCCEEDED(hr); if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) return hr; hr = RemoveProfile(); if (uninitialize) CoUninitialize(); return hr; }
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) { g_module = module; DisableThreadLibraryCalls(module); } return TRUE; }
STDAPI DllCanUnloadNow() { return (g_objectCount == 0 && g_lockCount == 0) ? S_OK : S_FALSE; }
STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) { if (clsid != kTextServiceClsid) return CLASS_E_CLASSNOTAVAILABLE; auto* factory = new (std::nothrow) ClassFactory(); if (!factory) return E_OUTOFMEMORY; HRESULT hr = factory->QueryInterface(iid, result); factory->Release(); return hr; }
