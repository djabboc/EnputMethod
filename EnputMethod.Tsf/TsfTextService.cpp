#include <windows.h>
#include <textstor.h>
#include <msctf.h>
#include <algorithm>
#include <array>
#include <cwctype>
#include <fstream>
#include <iterator>
#include <new>
#include <string>
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

int ConfiguredCandidateCount() {
    const std::wstring directory = UserDataDirectory();
    if (directory.empty()) return 4;
    const std::string json = ReadUtf8File(directory + L"\\conf.json");
    const size_t key = json.find("\"candidateCount\"");
    if (key == std::string::npos) return 4;
    const size_t colon = json.find(':', key);
    if (colon == std::string::npos) return 4;
    int value = 0;
    for (size_t index = colon + 1; index < json.size(); ++index) {
        const char character = json[index];
        if (character >= '0' && character <= '9') value = value * 10 + (character - '0');
        else if (value != 0) break;
        else if (character != ' ' && character != '\t' && character != '\r' && character != '\n') return 4;
    }
    return std::clamp(value, 1, 9);
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

std::vector<std::wstring> LoadDictionary() {
    const std::wstring directory = UserDataDirectory();
    const std::string contents = directory.empty() ? std::string{} : ReadUtf8File(directory + L"\\dictionary.txt");
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
    return words.empty() ? DefaultDictionary() : words;
}

class KeyEditSession;

class CandidateWindow final {
public:
    ~CandidateWindow() { if (window_) DestroyWindow(window_); }

    void Show(ITfContext* context, TfEditCookie cookie, ITfRange* range, const std::vector<std::wstring>& candidates) {
        candidates_ = candidates;
        if (candidates_.empty()) { Hide(); return; }
        if (!EnsureWindow()) return;

        ITfContextView* view{};
        RECT textRect{};
        BOOL clipped{};
        if (FAILED(context->GetActiveView(&view))) return;
        const HRESULT hr = view->GetTextExt(cookie, range, &textRect, &clipped);
        view->Release();
        if (FAILED(hr)) return;

        const int height = kVerticalPadding * 2 + static_cast<int>(candidates_.size()) * kRowHeight;
        SetWindowPos(window_, HWND_TOPMOST, textRect.left, textRect.bottom + 2, kWidth, height,
                     SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(window_, nullptr, TRUE);
    }

    void Hide() {
        candidates_.clear();
        if (window_) ShowWindow(window_, SW_HIDE);
    }

private:
    static constexpr wchar_t kClassName[] = L"EnputMethodCandidateWindow";
    static constexpr int kWidth = 208;
    static constexpr int kRowHeight = 26;
    static constexpr int kVerticalPadding = 6;

    bool EnsureWindow() {
        if (window_) return true;
        WNDCLASSEXW windowClass{ sizeof(windowClass) };
        windowClass.lpfnWndProc = WindowProc;
        windowClass.hInstance = g_module;
        windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        windowClass.lpszClassName = kClassName;
        RegisterClassExW(&windowClass);
        window_ = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
                                  kClassName, L"", WS_POPUP | WS_BORDER,
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
            FillRect(dc, &client, reinterpret_cast<HBRUSH>(COLOR_WINDOW + 1));
            SetBkMode(dc, TRANSPARENT);
            SetTextColor(dc, GetSysColor(COLOR_WINDOWTEXT));
            for (size_t index = 0; index < self->candidates_.size(); ++index) {
                RECT row{ 12, kVerticalPadding + static_cast<int>(index) * kRowHeight, kWidth - 12,
                          kVerticalPadding + static_cast<int>(index + 1) * kRowHeight };
                const std::wstring label = std::to_wstring(index + 1) + L".  " + self->candidates_[index];
                DrawTextW(dc, label.c_str(), static_cast<int>(label.size()), &row, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
            }
            EndPaint(window, &paint);
            return 0;
        }
        if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
        return DefWindowProcW(window, message, 0, parameter);
    }

    HWND window_ = nullptr;
    std::vector<std::wstring> candidates_;
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
        if (key >= 'A' && key <= 'Z') {
            const bool uppercase = (GetKeyState(VK_SHIFT) < 0) ^ ((GetKeyState(VK_CAPITAL) & 1) != 0);
            typed_ += static_cast<wchar_t>(uppercase ? key : key + (L'a' - L'A'));
            candidates_ = FindCandidates(typed_, ConfiguredCandidateCount());
            return UpdateComposition(context, cookie);
        }
        if (key == VK_BACK && !typed_.empty()) {
            typed_.pop_back();
            candidates_ = FindCandidates(typed_, ConfiguredCandidateCount());
            return typed_.empty() ? FinishComposition(cookie, L"", L'\0') : UpdateComposition(context, cookie);
        }
        const int candidateIndex = CandidateIndex(key);
        if (candidateIndex >= 0 && candidateIndex < static_cast<int>(candidates_.size())) return FinishComposition(cookie, candidates_[candidateIndex], L'\0');
        if (key == VK_TAB && !candidates_.empty()) return FinishComposition(cookie, candidates_.front(), L'\0');
        if (key == VK_SPACE) return FinishComposition(cookie, typed_, L' ');
        if (key == VK_RETURN) return FinishComposition(cookie, typed_, L'\r');
        if (key == VK_ESCAPE) return FinishComposition(cookie, typed_, L'\0');
        return S_FALSE;
    }

private:
    friend class KeyEditSession;

    bool ShouldHandleKey(WPARAM key) const {
        if (GetKeyState(VK_CONTROL) < 0 || GetKeyState(VK_MENU) < 0) return false;
        if (key >= 'A' && key <= 'Z') return true;
        return !typed_.empty() && (key == VK_BACK || key == VK_TAB || key == VK_SPACE || key == VK_RETURN || key == VK_ESCAPE ||
                                  (CandidateIndex(key) >= 0 && CandidateIndex(key) < static_cast<int>(candidates_.size())));
    }

    static int CandidateIndex(WPARAM key) {
        if (key >= '1' && key <= '9') return static_cast<int>(key - '1');
        if (key >= VK_NUMPAD1 && key <= VK_NUMPAD9) return static_cast<int>(key - VK_NUMPAD1);
        return -1;
    }

    static std::vector<std::wstring> FindCandidates(const std::wstring& typed, int maximum) {
        if (typed.empty()) return {};
        std::wstring lower = typed;
        std::transform(lower.begin(), lower.end(), lower.begin(), towlower);
        std::vector<std::wstring> matches;
        for (const std::wstring& word : LoadDictionary()) {
            std::wstring candidate = word;
            std::transform(candidate.begin(), candidate.end(), candidate.begin(), towlower);
            if (candidate.starts_with(lower) && candidate.size() > typed.size()) {
                matches.push_back(word);
                if (matches.size() == static_cast<size_t>(maximum)) break;
            }
        }
        return matches;
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
                caret->Collapse(cookie, TF_ANCHOR_END);
                TF_SELECTION selection{ caret, { TF_AE_NONE, FALSE } };
                hr = context->SetSelection(cookie, 1, &selection);
                caret->Release();
            }
        }
        if (SUCCEEDED(hr)) candidateWindow_.Show(context, cookie, range, candidates_);
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
        typed_.clear(); candidates_.clear(); candidateWindow_.Hide();
        if (compositionContext_) { compositionContext_->Release(); compositionContext_ = nullptr; }
        if (SUCCEEDED(hr)) hr = composition->EndComposition(cookie);
        composition->Release();
        return hr;
    }

    void ClearComposition() {
        if (composition_) { composition_->Release(); composition_ = nullptr; }
        if (compositionContext_) { compositionContext_->Release(); compositionContext_ = nullptr; }
        typed_.clear(); candidates_.clear(); candidateWindow_.Hide();
    }

    long refs_ = 1;
    ITfThreadMgr* threadManager_ = nullptr;
    TfClientId clientId_ = TF_CLIENTID_NULL;
    ITfComposition* composition_ = nullptr;
    ITfContext* compositionContext_ = nullptr;
    std::wstring typed_;
    std::vector<std::wstring> candidates_;
    CandidateWindow candidateWindow_;
};

class KeyEditSession final : public ITfEditSession {
public:
    KeyEditSession(TextService* service, ITfContext* context, WPARAM key) : service_(service), context_(context), key_(key) { service_->AddRef(); context_->AddRef(); }
    ~KeyEditSession() { context_->Release(); service_->Release(); }
    STDMETHODIMP QueryInterface(REFIID iid, void** result) override { if (!result) return E_INVALIDARG; *result = nullptr; if (iid != IID_IUnknown && iid != IID_ITfEditSession) return E_NOINTERFACE; *result = static_cast<ITfEditSession*>(this); AddRef(); return S_OK; }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs_); }
    STDMETHODIMP_(ULONG) Release() override { const auto refs = InterlockedDecrement(&refs_); if (!refs) delete this; return refs; }
    STDMETHODIMP DoEditSession(TfEditCookie cookie) override { return service_->ApplyKey(context_, cookie, key_); }
private: long refs_ = 1; TextService* service_; ITfContext* context_; WPARAM key_;
};

STDMETHODIMP TextService::OnKeyDown(ITfContext* context, WPARAM key, LPARAM, BOOL* eaten) {
    if (!eaten) return E_INVALIDARG;
    *eaten = FALSE;
    if (!ShouldHandleKey(key)) return S_OK;
    auto* session = new (std::nothrow) KeyEditSession(this, context, key);
    if (!session) return E_OUTOFMEMORY;
    HRESULT sessionResult{}; const HRESULT hr = context->RequestEditSession(clientId_, session, TF_ES_SYNC | TF_ES_READWRITE, &sessionResult);
    session->Release();
    *eaten = SUCCEEDED(hr) && SUCCEEDED(sessionResult);
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
    // TSF DLLs stay mapped in applications while a profile is active. Use a new
    // deployment name for this update so an in-use prior version cannot block it.
    *path = directory + L"\\EnputMethod.Tsf.5.dll";
    return S_OK;
}
HRESULT RegisterComServer() {
    wchar_t source[MAX_PATH]{}; if (!GetModuleFileNameW(g_module, source, ARRAYSIZE(source))) return HRESULT_FROM_WIN32(GetLastError());
    std::wstring path; HRESULT hr = InstalledDllPath(&path); if (FAILED(hr)) return hr;
    if (_wcsicmp(source, path.c_str()) != 0 && !CopyFileW(source, path.c_str(), FALSE)) return HRESULT_FROM_WIN32(GetLastError());
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
