#include <windows.h>
#include <textstor.h>
#include <msctf.h>
#include "JsonObjectReader.h"
#include <algorithm>
#include <cmath>
#include <cwctype>
#include <fstream>
#include <functional>
#include <iterator>
#include <new>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

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
    return theme;
}

RuntimeConfiguration LoadRuntimeConfiguration() {
    RuntimeConfiguration configuration;
    enput::json::Object object;
    if (enput::json::ReadObject(ReadUtf8File(ConfigurationPath()), &object)) {
        configuration.candidateCount = std::clamp(static_cast<int>(std::lround(enput::json::NumberOr(object, "candidateCount", configuration.candidateCount))), 1, 9);
        configuration.horizontal = enput::json::StringOr(object, "layout", "vertical") == "horizontal";
        configuration.appendSpaceAfterSelection = enput::json::BooleanOr(object, "appendSpaceAfterSelection", configuration.appendSpaceAfterSelection);
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
    const enput::json::Value* values = enput::json::ReadDocument(contents, &document) ? enput::json::ObjectValue(document, "entries") : nullptr;
    if (values && values->type == enput::json::Value::Type::Array) {
        for (const enput::json::Value& value : values->array) {
            const enput::json::Value* emoji = enput::json::ObjectValue(value, "emoji");
            if (!emoji || emoji->type != enput::json::Value::Type::String) continue;
            EmojiEntry entry;
            entry.emoji = Utf8ToWide(emoji->string);
            entry.keywords = JsonStrings(enput::json::ObjectValue(value, "keywords"));
            if (!entry.emoji.empty() && !entry.keywords.empty()) entries.push_back(std::move(entry));
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

const std::vector<TranslationEntry>& LoadTranslationDictionary() {
    static std::vector<TranslationEntry> entries;
    static bool loaded = false;
    if (loaded) return entries;
    loaded = true;
    enput::json::Value document;
    const enput::json::Value* values = enput::json::ReadDocument(ReadUtf8File(TranslationDictionaryPath()), &document) ? enput::json::ObjectValue(document, "entries") : nullptr;
    if (!values || values->type != enput::json::Value::Type::Array) return entries;
    for (const enput::json::Value& value : values->array) {
        const enput::json::Value* text = enput::json::ObjectValue(value, "text");
        if (!text || text->type != enput::json::Value::Type::String) continue;
        TranslationEntry entry;
        entry.text = Utf8ToWide(text->string);
        entry.partsOfSpeech = JsonStrings(enput::json::ObjectValue(value, "partOfSpeech"));
        entry.source = Utf8ToWide(enput::json::ObjectValue(value, "source") && enput::json::ObjectValue(value, "source")->type == enput::json::Value::Type::String ? enput::json::ObjectValue(value, "source")->string : "");
        const enput::json::Value* translations = enput::json::ObjectValue(value, "translations");
        if (translations && translations->type == enput::json::Value::Type::Object) {
            for (const auto& [language, meanings] : translations->object) entry.translations.emplace_back(Utf8ToWide(language), JsonStrings(&meanings));
        }
        const enput::json::Value* examples = enput::json::ObjectValue(value, "examples");
        if (examples && examples->type == enput::json::Value::Type::Array && !examples->array.empty()) {
            const enput::json::Value* example = enput::json::ObjectValue(examples->array.front(), "text");
            if (example && example->type == enput::json::Value::Type::String) entry.example = Utf8ToWide(example->string);
        }
        if (!entry.text.empty()) entries.push_back(std::move(entry));
    }
    return entries;
}

const TranslationEntry* FindTranslation(const std::wstring& text) {
    const std::wstring lower = Lowercase(text);
    const std::vector<TranslationEntry>& entries = LoadTranslationDictionary();
    const auto entry = std::find_if(entries.begin(), entries.end(), [&lower](const TranslationEntry& value) { return Lowercase(value.text) == lower; });
    return entry == entries.end() ? nullptr : &*entry;
}

class KeyEditSession;
class CandidateClickEditSession;

class CandidateWindow final {
public:
    ~CandidateWindow() {
        if (font_) DeleteObject(font_);
        if (window_) DestroyWindow(window_);
    }

    void Show(ITfContext* context, TfEditCookie cookie, ITfRange* range, const std::vector<std::wstring>& candidates, const RuntimeConfiguration& configuration, size_t page, size_t pageCount, size_t selectedIndex, bool capsLock, std::wstring modeMarker, std::function<void(int)> actionCallback) {
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
        const bool sizeChanged = size.cx != size_.cx || size.cy != size_.cy;
        const bool positionChanged = positionX != positionX_ || positionY != positionY_;
        if (sizeChanged) {
            const int diameter = (std::max)(1, configuration_.theme.cornerRadius * 2);
            SetWindowRgn(window_, CreateRoundRectRgn(0, 0, size.cx, size.cy, diameter, diameter), TRUE);
            size_ = size;
        }
        if (!IsWindowVisible(window_) || sizeChanged || positionChanged) {
            SetWindowPos(window_, HWND_TOPMOST, positionX, positionY, size.cx, size.cy,
                         SWP_NOACTIVATE | SWP_SHOWWINDOW);
            positionX_ = positionX;
            positionY_ = positionY;
        }
        InvalidateRect(window_, nullptr, FALSE);
    }

    void Hide() {
        candidates_.clear();
        actionCallback_ = {};
        if (window_) ShowWindow(window_, SW_HIDE);
    }

private:
    static constexpr wchar_t kClassName[] = L"EnputMethodCandidateWindow";

    void ConfigureWindow() {
        if (font_) DeleteObject(font_);
        HDC dc = GetDC(window_);
        const int dpi = GetDeviceCaps(dc, LOGPIXELSY);
        ReleaseDC(window_, dc);
        font_ = CreateFontW(-MulDiv(configuration_.fontSize, dpi, 72), 0, 0, 0,
                            FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                            CLEARTYPE_QUALITY, DEFAULT_PITCH | FF_DONTCARE, configuration_.fontFamily.c_str());
        SetLayeredWindowAttributes(window_, 0, configuration_.opacity, LWA_ALPHA);
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
        if (previous) SelectObject(dc, previous);
        ReleaseDC(window_, dc);
        const int footerHeight = (pageCount_ > 1 || capsLock_ || !modeMarker_.empty()) ? rowHeight : 0;
        const int height = (configuration_.horizontal ? rowHeight + padding * 2 : rowHeight * static_cast<int>(candidates_.size()) + padding * 2) + footerHeight;
        return { width + configuration_.theme.shadowSize, height + configuration_.theme.shadowSize };
    }

    SIZE LabelSize(HDC dc, size_t index) const {
        const std::wstring label = std::to_wstring(index + 1) + L".  " + candidates_[index];
        SIZE size{};
        GetTextExtentPoint32W(dc, label.c_str(), static_cast<int>(label.size()), &size);
        return size;
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
        if (pageCount_ > 1 && point.y >= rowsBottom && point.y < surfaceBottom - padding) return point.x < surfaceRight / 2 ? -1 : -2;
        return -3;
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
            HDC dc = BeginPaint(window, &paint);
            RECT client{};
            GetClientRect(window, &client);
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
                if (index == self->selectedIndex_) {
                    HBRUSH selected = CreateSolidBrush(self->configuration_.theme.selectedBackground);
                    FillRect(dc, &row, selected);
                    DeleteObject(selected);
                    if (self->configuration_.theme.selectedBorderWidth > 0) {
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
                SetTextColor(dc, index == self->selectedIndex_ ? self->configuration_.theme.selectedForeground : self->configuration_.theme.foreground);
                const std::wstring label = std::to_wstring(index + 1) + L".  " + self->candidates_[index];
                DrawTextW(dc, label.c_str(), static_cast<int>(label.size()), &row, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
            }
            if (self->pageCount_ > 1 || self->capsLock_ || !self->modeMarker_.empty()) {
                const std::wstring pageText = L"Page " + std::to_wstring(self->page_ + 1) + L"/" + std::to_wstring(self->pageCount_);
                RECT pageRow{ padding, surface.bottom - rowHeight - padding, surface.right - padding, surface.bottom - padding };
                if (self->capsLock_ || !self->modeMarker_.empty()) {
                    std::wstring capsText = self->capsLock_ ? L"CAPS" : L"";
                    if (!capsText.empty() && !self->modeMarker_.empty()) capsText += L" ";
                    capsText += self->modeMarker_;
                    SetTextColor(dc, self->configuration_.theme.selectedForeground);
                    DrawTextW(dc, capsText.c_str(), static_cast<int>(capsText.size()), &pageRow, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                }
                if (self->pageCount_ > 1) {
                    const std::wstring previousText = L"<";
                    const std::wstring nextText = L">";
                    SetTextColor(dc, self->configuration_.theme.foreground);
                    DrawTextW(dc, previousText.c_str(), static_cast<int>(previousText.size()), &pageRow, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
                    DrawTextW(dc, nextText.c_str(), static_cast<int>(nextText.size()), &pageRow, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
                    RECT pageNumber{ pageRow.left + rowHeight, pageRow.top, pageRow.right - rowHeight, pageRow.bottom };
                    DrawTextW(dc, pageText.c_str(), static_cast<int>(pageText.size()), &pageNumber, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                }
            }
            if (previousFont) SelectObject(dc, previousFont);
            EndPaint(window, &paint);
            return 0;
        }
        if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
        if (message == WM_LBUTTONUP && self && self->actionCallback_) {
            const POINT point{ static_cast<short>(LOWORD(parameter)), static_cast<short>(HIWORD(parameter)) };
            const int action = self->HitTestAction(point);
            if (action != -3) self->actionCallback_(action);
            return 0;
        }
        return DefWindowProcW(window, message, 0, parameter);
    }

    HWND window_ = nullptr;
    HFONT font_ = nullptr;
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
};

class TranslationWindow final {
public:
    ~TranslationWindow() { if (font_) DeleteObject(font_); if (window_) DestroyWindow(window_); }

    void Show(ITfContext* context, TfEditCookie cookie, ITfRange* range, const TranslationEntry* entry, const RuntimeConfiguration& configuration) {
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
        constexpr int width = 380;
        const int height = (std::min)(420, configuration_.theme.padding * 2 + configuration_.theme.rowHeight * static_cast<int>(lines_.size()) * 2);
        int left = textRect.right + 12;
        int top = textRect.bottom + 2;
        if (configuration_.avoidScreenEdges) {
            MONITORINFO info{ sizeof(info) };
            const HMONITOR monitor = MonitorFromRect(&textRect, MONITOR_DEFAULTTONEAREST);
            if (monitor && GetMonitorInfoW(monitor, &info)) {
                left = std::clamp(left, static_cast<int>(info.rcWork.left), static_cast<int>(info.rcWork.right) - width);
                top = std::clamp(top, static_cast<int>(info.rcWork.top), static_cast<int>(info.rcWork.bottom) - height);
            }
        }
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
            HBRUSH background = CreateSolidBrush(self->configuration_.theme.background); HPEN border = CreatePen(PS_SOLID, self->configuration_.theme.borderWidth, self->configuration_.theme.border);
            HGDIOBJ oldBrush = SelectObject(dc, background); HGDIOBJ oldPen = SelectObject(dc, border);
            RoundRect(dc, 0, 0, client.right, client.bottom, self->configuration_.theme.cornerRadius * 2, self->configuration_.theme.cornerRadius * 2);
            SelectObject(dc, oldBrush); SelectObject(dc, oldPen); DeleteObject(background); DeleteObject(border);
            SetBkMode(dc, TRANSPARENT); HGDIOBJ oldFont = self->font_ ? SelectObject(dc, self->font_) : nullptr;
            RECT text{ self->configuration_.theme.padding, self->configuration_.theme.padding, client.right - self->configuration_.theme.padding, client.bottom - self->configuration_.theme.padding };
            for (size_t index = 0; index < self->lines_.size() && text.top < text.bottom; ++index) {
                SetTextColor(dc, index == 0 ? self->configuration_.theme.selectedForeground : self->configuration_.theme.foreground);
                RECT line = text; DrawTextW(dc, self->lines_[index].c_str(), static_cast<int>(self->lines_[index].size()), &line, DT_LEFT | DT_WORDBREAK | DT_CALCRECT);
                DrawTextW(dc, self->lines_[index].c_str(), static_cast<int>(self->lines_[index].size()), &text, DT_LEFT | DT_WORDBREAK);
                text.top += (std::max)(self->configuration_.theme.rowHeight, static_cast<int>(line.bottom - line.top)) + 2;
            }
            if (oldFont) SelectObject(dc, oldFont); EndPaint(window, &paint); return 0;
        }
        if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
        return DefWindowProcW(window, message, 0, parameter);
    }

    HWND window_ = nullptr; HFONT font_ = nullptr; RuntimeConfiguration configuration_{}; std::vector<std::wstring> lines_;
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

    STDMETHODIMP OnSetFocus(BOOL) override { return S_OK; }
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
            if (composition_) return UpdateComposition(context, cookie);
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
        const int candidateIndex = CandidateIndex(key);
        const wchar_t selectionTrailing = configuration_.appendSpaceAfterSelection ? L' ' : L'\0';
        if (candidateIndex >= 0 && candidateIndex < static_cast<int>(candidates_.size())) return FinishWithSuggestions(context, cookie, candidates_[candidateIndex], selectionTrailing);
        if (HasShortcut(configuration_.shortcuts.selectCurrent, key) && selectedIndex_ < candidates_.size()) return FinishWithSuggestions(context, cookie, candidates_[selectedIndex_], selectionTrailing);
        if (key == VK_SPACE && IsSuggestionActive()) return FinishComposition(cookie, L"", L' ');
        if (key == VK_SPACE) return FinishWithSuggestions(context, cookie, typed_, L' ');
        if (key == VK_RETURN) return FinishComposition(cookie, typed_, L'\r');
        if (key == VK_ESCAPE) return FinishComposition(cookie, typed_, L'\0');
        return S_FALSE;
    }

    HRESULT CommitForPassthrough(TfEditCookie cookie) {
        return FinishComposition(cookie, typed_, L'\0');
    }

    HRESULT CommitWithCharacter(TfEditCookie cookie, wchar_t character) {
        return FinishComposition(cookie, typed_ + character, L'\0');
    }

private:
    friend class KeyEditSession;
    friend class CandidateClickEditSession;

    void HandleCandidateWindowAction(int action);

    HRESULT ApplyCandidateWindowAction(ITfContext* context, TfEditCookie cookie, int action) {
        if (action >= 0 && action < static_cast<int>(candidates_.size())) {
            const wchar_t trailing = configuration_.appendSpaceAfterSelection ? L' ' : L'\0';
            return FinishWithSuggestions(context, cookie, candidates_[action], trailing);
        }
        if (action == -1) return MovePage(context, cookie, -1);
        if (action == -2) return MovePage(context, cookie, 1);
        return S_FALSE;
    }

    bool ShouldHandleKey(WPARAM key) const {
        if (HasShortcut(configuration_.shortcuts.toggleTranslationWindow, key)) return true;
        if (HasShortcut(configuration_.shortcuts.toggleEmojiMode, key)) return true;
        if (emojiMode_ && key == VK_ESCAPE && typed_.empty()) return true;
        if (IsSuggestionActive()) return !IsModifierKey(key);
        if (!typed_.empty()) return !IsModifierKey(key);
        if (GetKeyState(VK_CONTROL) < 0 || GetKeyState(VK_MENU) < 0) return false;
        return key >= 'A' && key <= 'Z';
    }

    bool ShouldPassThrough(WPARAM key) const {
        if (typed_.empty()) {
            if (!IsSuggestionActive()) return false;
            const bool hasModifier = GetKeyState(VK_CONTROL) < 0 || GetKeyState(VK_MENU) < 0;
            if (!hasModifier && key >= 'A' && key <= 'Z') return false;
            if (HasShortcut(configuration_.shortcuts.toggleTranslationWindow, key) || key == VK_SPACE || HasShortcut(configuration_.shortcuts.previousPage, key) || HasShortcut(configuration_.shortcuts.nextPage, key)) return false;
            if (HasShortcut(configuration_.shortcuts.selectPrevious, key) || HasShortcut(configuration_.shortcuts.selectNext, key)) return false;
            if (CandidateIndex(key) >= 0 && CandidateIndex(key) < static_cast<int>(candidates_.size())) return false;
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
        if (CandidateIndex(key) >= 0 && CandidateIndex(key) < static_cast<int>(candidates_.size())) return false;
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

    static int CandidateIndex(WPARAM key) {
        if (key >= '1' && key <= '9') return static_cast<int>(key - '1');
        if (key >= VK_NUMPAD1 && key <= VK_NUMPAD9) return static_cast<int>(key - VK_NUMPAD1);
        return -1;
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
        std::wstring display = candidate;
        const bool capsLock = (GetKeyState(VK_CAPITAL) & 1) != 0;
        if (capsLock || (configuration.preserveCase && IsAllUpper(typed))) {
            ToUpperInPlace(&display);
        } else if (configuration.preserveCase && IsTitleCase(typed)) {
            ToLowerInPlace(&display);
            if (!display.empty()) display.front() = static_cast<wchar_t>(towupper(display.front()));
        } else if (configuration.preserveCase && !typed.empty() && std::all_of(typed.begin(), typed.end(), [](wchar_t character) { return !iswalpha(character) || iswlower(character); })) {
            ToLowerInPlace(&display);
        }
        return display;
    }

    static std::vector<std::wstring> FindCandidates(const std::wstring& typed) {
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
        matches.reserve(exactMatches.size() + prefixMatches.size());
        matches.insert(matches.end(), exactMatches.begin(), exactMatches.end());
        matches.insert(matches.end(), prefixMatches.begin(), prefixMatches.end());
        return matches;
    }

    static std::vector<std::wstring> FindAssociatedCandidates(const std::wstring& committedText) {
        const std::wstring lower = Lowercase(committedText);
        std::vector<std::wstring> matches;
        std::unordered_set<std::wstring> keys;
        const auto appendUnique = [&matches, &keys](const std::wstring& candidate) {
            const std::wstring lowerCandidate = Lowercase(candidate);
            if (keys.insert(lowerCandidate).second) matches.push_back(candidate);
        };
        for (const SuggestionEntry& entry : LoadSuggestionDictionary()) {
            if (Lowercase(entry.text) != lower) continue;
            for (const std::wstring& candidate : entry.next) appendUnique(candidate);
            for (const std::wstring& candidate : entry.phrases) appendUnique(candidate);
        }
        if (matches.empty()) {
            static const std::vector<std::wstring> fallback{ L"the", L"to", L"and", L"a", L"is", L"of", L"for", L"in", L"that" };
            matches = fallback;
        }
        return matches;
    }

    static std::vector<std::wstring> FindEmojiCandidates(const std::wstring& typed) {
        const std::wstring lower = Lowercase(typed);
        std::vector<std::wstring> matches;
        for (const EmojiEntry& entry : LoadEmojiDictionary()) {
            const bool matchesKeyword = std::any_of(entry.keywords.begin(), entry.keywords.end(), [&lower](const std::wstring& keyword) { return Lowercase(keyword).starts_with(lower); });
            if (!matchesKeyword || std::find(matches.begin(), matches.end(), entry.emoji) != matches.end()) continue;
            matches.push_back(entry.emoji);
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
        return UpdateComposition(context, cookie);
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
        return changed ? UpdateComposition(context, cookie) : S_OK;
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
        if (SUCCEEDED(hr)) {
            candidateWindow_.Show(context, cookie, range, candidates_, configuration_, currentPage_, PageCount(), selectedIndex_, (GetKeyState(VK_CAPITAL) & 1) != 0, emojiMode_ ? L"EMOJI" : L"", [this](int action) { HandleCandidateWindowAction(action); });
            translationWindow_.Show(context, cookie, range, translationEnabled_ && selectedIndex_ < candidates_.size() ? FindTranslation(candidates_[selectedIndex_]) : nullptr, configuration_);
        }
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
        typed_.clear(); cursor_ = 0; allCandidates_.clear(); candidates_.clear(); currentPage_ = 0; selectedIndex_ = 0; candidateWindow_.Hide(); translationWindow_.Hide();
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
        return UpdateComposition(context, cookie);
    }

    bool IsSuggestionActive() const {
        return composition_ && typed_.empty() && !candidates_.empty();
    }

    void ClearComposition() {
        if (composition_) { composition_->Release(); composition_ = nullptr; }
        if (compositionContext_) { compositionContext_->Release(); compositionContext_ = nullptr; }
        typed_.clear(); cursor_ = 0; allCandidates_.clear(); candidates_.clear(); currentPage_ = 0; selectedIndex_ = 0; candidateWindow_.Hide(); translationWindow_.Hide();
    }

    long refs_ = 1;
    ITfThreadMgr* threadManager_ = nullptr;
    TfClientId clientId_ = TF_CLIENTID_NULL;
    ITfComposition* composition_ = nullptr;
    ITfContext* compositionContext_ = nullptr;
    std::wstring typed_;
    size_t cursor_ = 0;
    std::vector<std::wstring> allCandidates_;
    std::vector<std::wstring> candidates_;
    size_t currentPage_ = 0;
    size_t selectedIndex_ = 0;
    std::wstring lastCommittedText_;
    bool emojiMode_ = false;
    bool translationEnabled_ = false;
    RuntimeConfiguration configuration_{};
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
    STDMETHODIMP DoEditSession(TfEditCookie cookie) override { return passThrough_ ? service_->CommitForPassthrough(cookie) : printableCharacter_ ? service_->CommitWithCharacter(cookie, printableCharacter_) : service_->ApplyKey(context_, cookie, key_); }
private: long refs_ = 1; TextService* service_; ITfContext* context_; WPARAM key_; bool passThrough_; wchar_t printableCharacter_;
};

class CandidateClickEditSession final : public ITfEditSession {
public:
    CandidateClickEditSession(TextService* service, ITfContext* context, int action) : service_(service), context_(context), action_(action) { service_->AddRef(); context_->AddRef(); }
    ~CandidateClickEditSession() { context_->Release(); service_->Release(); }
    STDMETHODIMP QueryInterface(REFIID iid, void** result) override { if (!result) return E_INVALIDARG; *result = nullptr; if (iid != IID_IUnknown && iid != IID_ITfEditSession) return E_NOINTERFACE; *result = static_cast<ITfEditSession*>(this); AddRef(); return S_OK; }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override { const auto refs = InterlockedDecrement(&refs_); if (!refs) delete this; return refs; }
    STDMETHODIMP DoEditSession(TfEditCookie cookie) override { return service_->ApplyCandidateWindowAction(context_, cookie, action_); }
private:
    long refs_ = 1;
    TextService* service_;
    ITfContext* context_;
    int action_;
};

void TextService::HandleCandidateWindowAction(int action) {
    if (!compositionContext_ || !composition_) return;
    auto* session = new (std::nothrow) CandidateClickEditSession(this, compositionContext_, action);
    if (!session) return;
    HRESULT sessionResult{};
    compositionContext_->RequestEditSession(clientId_, session, TF_ES_ASYNC | TF_ES_READWRITE, &sessionResult);
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
        if (error != ERROR_FILE_EXISTS || !FilesEqual(source, path)) return HRESULT_FROM_WIN32(error);
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
    if (FAILED(hr)) { profiles->Release(); return hr; }
    profiles->RemoveLanguageProfile(kTextServiceClsid, kLegacyEnglishUs, kProfileGuid);
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, L"Software\\Microsoft\\CTF\\TIP\\{9C8945D5-01DF-48F4-A8DB-57E8B6A1EB10}\\LanguageProfile\\0x00000409");
    hr = profiles->AddLanguageProfile(kTextServiceClsid, kChineseSimplified, kProfileGuid, description, static_cast<ULONG>(wcslen(description)), path.c_str(), static_cast<ULONG>(path.size()), 0);
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
