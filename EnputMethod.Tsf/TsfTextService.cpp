#include <windows.h>
#include <textstor.h>
#include <msctf.h>
#include <algorithm>
#include <array>
#include <cwctype>
#include <new>
#include <string>

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

class KeyEditSession;

class TextService final : public ITfTextInputProcessor, public ITfKeyEventSink, public ITfCompositionSink {
public:
    TextService() { InterlockedIncrement(&g_objectCount); }
    ~TextService() { ClearComposition(); Deactivate(); InterlockedDecrement(&g_objectCount); }

    STDMETHODIMP QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        if (iid == IID_IUnknown || iid == IID_ITfTextInputProcessor) *result = static_cast<ITfTextInputProcessor*>(this);
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
        ITfSource* source = nullptr;
        HRESULT hr = threadManager_->QueryInterface(IID_PPV_ARGS(&source));
        if (SUCCEEDED(hr)) { hr = source->AdviseSink(IID_ITfKeyEventSink, static_cast<ITfKeyEventSink*>(this), &keySinkCookie_); source->Release(); }
        return hr;
    }
    STDMETHODIMP Deactivate() override {
        if (threadManager_) {
            ITfSource* source = nullptr;
            if (keySinkCookie_ != TF_INVALID_COOKIE && SUCCEEDED(threadManager_->QueryInterface(IID_PPV_ARGS(&source)))) { source->UnadviseSink(keySinkCookie_); source->Release(); }
            keySinkCookie_ = TF_INVALID_COOKIE; threadManager_->Release(); threadManager_ = nullptr;
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
            suggestion_ = FindSuggestion(typed_);
            return UpdateComposition(context, cookie);
        }
        if (key == VK_BACK && !typed_.empty()) {
            typed_.pop_back();
            suggestion_ = FindSuggestion(typed_);
            return typed_.empty() ? FinishComposition(cookie, false, L'\0') : UpdateComposition(context, cookie);
        }
        if (key == VK_TAB) return FinishComposition(cookie, true, L'\0');
        if (key == VK_SPACE) return FinishComposition(cookie, false, L' ');
        if (key == VK_RETURN) return FinishComposition(cookie, false, L'\r');
        if (key == VK_ESCAPE) return FinishComposition(cookie, false, L'\0');
        return S_FALSE;
    }

private:
    friend class KeyEditSession;

    bool ShouldHandleKey(WPARAM key) const {
        if (GetKeyState(VK_CONTROL) < 0 || GetKeyState(VK_MENU) < 0) return false;
        if (key >= 'A' && key <= 'Z') return true;
        return !typed_.empty() && (key == VK_BACK || key == VK_TAB || key == VK_SPACE || key == VK_RETURN || key == VK_ESCAPE);
    }

    static std::wstring FindSuggestion(const std::wstring& typed) {
        if (typed.empty()) return {};
        std::wstring lower = typed;
        std::transform(lower.begin(), lower.end(), lower.begin(), towlower);
        static constexpr std::array<const wchar_t*, 34> words{
            L"about", L"above", L"after", L"again", L"always", L"because", L"before", L"between",
            L"business", L"different", L"English", L"example", L"first", L"following", L"function",
            L"great", L"hello", L"help", L"information", L"input", L"keyboard", L"language", L"method",
            L"people", L"please", L"project", L"prototype", L"really", L"service", L"simple", L"system",
            L"thank", L"through", L"where" };
        for (const wchar_t* word : words) {
            std::wstring candidate = word;
            std::transform(candidate.begin(), candidate.end(), candidate.begin(), towlower);
            if (candidate.starts_with(lower) && candidate.size() > typed.size()) return std::wstring(word + typed.size());
        }
        return {};
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
        const std::wstring display = typed_ + suggestion_;
        hr = range->SetText(cookie, 0, display.data(), static_cast<LONG>(display.size()));
        if (SUCCEEDED(hr)) {
            ITfRange* caret{}; hr = range->Clone(&caret);
            if (SUCCEEDED(hr)) {
                caret->Collapse(cookie, TF_ANCHOR_END);
                if (!suggestion_.empty()) caret->ShiftStart(cookie, -static_cast<LONG>(suggestion_.size()), nullptr, nullptr);
                TF_SELECTION selection{ caret, { TF_AE_NONE, !suggestion_.empty() } };
                hr = context->SetSelection(cookie, 1, &selection);
                caret->Release();
            }
        }
        range->Release();
        return hr;
    }

    HRESULT FinishComposition(TfEditCookie cookie, bool acceptSuggestion, wchar_t trailing) {
        if (!composition_) return S_FALSE;
        ITfRange* range{}; HRESULT hr = composition_->GetRange(&range);
        std::wstring finalText = typed_ + (acceptSuggestion ? suggestion_ : L"");
        if (trailing) finalText += trailing;
        if (SUCCEEDED(hr)) { hr = range->SetText(cookie, 0, finalText.data(), static_cast<LONG>(finalText.size())); range->Release(); }
        ITfComposition* composition = composition_; composition_ = nullptr;
        typed_.clear(); suggestion_.clear();
        if (compositionContext_) { compositionContext_->Release(); compositionContext_ = nullptr; }
        if (SUCCEEDED(hr)) hr = composition->EndComposition(cookie);
        composition->Release();
        return hr;
    }

    void ClearComposition() {
        if (composition_) { composition_->Release(); composition_ = nullptr; }
        if (compositionContext_) { compositionContext_->Release(); compositionContext_ = nullptr; }
        typed_.clear(); suggestion_.clear();
    }

    long refs_ = 1;
    ITfThreadMgr* threadManager_ = nullptr;
    TfClientId clientId_ = TF_CLIENTID_NULL;
    DWORD keySinkCookie_ = TF_INVALID_COOKIE;
    ITfComposition* composition_ = nullptr;
    ITfContext* compositionContext_ = nullptr;
    std::wstring typed_;
    std::wstring suggestion_;
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
    *path = directory + L"\\EnputMethod.Tsf.dll";
    return S_OK;
}
HRESULT RegisterComServer() {
    wchar_t source[MAX_PATH]{}; if (!GetModuleFileNameW(g_module, source, ARRAYSIZE(source))) return HRESULT_FROM_WIN32(GetLastError());
    std::wstring path; HRESULT hr = InstalledDllPath(&path); if (FAILED(hr)) return hr;
    if (_wcsicmp(source, path.c_str()) != 0 && !CopyFileW(source, path.c_str(), FALSE)) return HRESULT_FROM_WIN32(GetLastError());
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
    hr = categories->RegisterCategory(kTextServiceClsid, GUID_TFCAT_TIP_KEYBOARD, kTextServiceClsid); categories->Release();
    return hr;
}
HRESULT RemoveProfile() {
    ITfInputProcessorProfiles* profiles{}; HRESULT hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&profiles));
    if (SUCCEEDED(hr)) { profiles->RemoveLanguageProfile(kTextServiceClsid, kChineseSimplified, kProfileGuid); profiles->RemoveLanguageProfile(kTextServiceClsid, kLegacyEnglishUs, kProfileGuid); profiles->Unregister(kTextServiceClsid); profiles->Release(); }
    ITfCategoryMgr* categories{}; if (SUCCEEDED(CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&categories)))) { categories->UnregisterCategory(kTextServiceClsid, GUID_TFCAT_TIP_KEYBOARD, kTextServiceClsid); categories->Release(); }
    RegDeleteTreeW(HKEY_LOCAL_MACHINE, ClassKey().c_str()); std::wstring path; if (SUCCEEDED(InstalledDllPath(&path))) DeleteFileW(path.c_str()); return S_OK;
}
}

extern "C" HRESULT WINAPI InstallEnglishInputMethod() { HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED); const bool uninitialize = SUCCEEDED(hr); if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) return hr; hr = RegisterComServer(); if (SUCCEEDED(hr)) hr = RegisterProfile(); if (uninitialize) CoUninitialize(); return hr; }
extern "C" HRESULT WINAPI UninstallEnglishInputMethod() { HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED); const bool uninitialize = SUCCEEDED(hr); if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) return hr; hr = RemoveProfile(); if (uninitialize) CoUninitialize(); return hr; }
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) { g_module = module; DisableThreadLibraryCalls(module); } return TRUE; }
STDAPI DllCanUnloadNow() { return (g_objectCount == 0 && g_lockCount == 0) ? S_OK : S_FALSE; }
STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** result) { if (clsid != kTextServiceClsid) return CLASS_E_CLASSNOTAVAILABLE; auto* factory = new (std::nothrow) ClassFactory(); if (!factory) return E_OUTOFMEMORY; HRESULT hr = factory->QueryInterface(iid, result); factory->Release(); return hr; }
