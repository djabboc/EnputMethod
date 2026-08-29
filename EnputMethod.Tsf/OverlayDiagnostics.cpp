#include "OverlayDiagnostics.h"

#include <windows.h>

#include <algorithm>
#include <cstdio>
#include <string>

namespace enput {
namespace {

std::wstring DiagnosticPath() {
    wchar_t localAppData[MAX_PATH]{};
    if (!GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, ARRAYSIZE(localAppData))) return {};
    std::wstring directory = std::wstring(localAppData) + L"\\Enput Method";
    if (!CreateDirectoryW(directory.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS) return {};
    return directory + L"\\overlay-diagnostics.log";
}

} // namespace

void WriteOverlayDiagnostic(std::string_view event, std::string_view detail) {
    SYSTEMTIME now{};
    GetLocalTime(&now);
    char line[1024]{};
    const int length = std::snprintf(line, sizeof(line),
        "%04u-%02u-%02uT%02u:%02u:%02u.%03u+08:00 TSF pid=%lu %.*s %.*s\r\n",
        now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, now.wMilliseconds,
        GetCurrentProcessId(), static_cast<int>(event.size()), event.data(), static_cast<int>(detail.size()), detail.data());
    if (length <= 0) return;
    OutputDebugStringA(line);

    HANDLE mutex = CreateMutexW(nullptr, FALSE, L"Local\\EnputMethod.OverlayDiagnostics.v1");
    const DWORD wait = mutex ? WaitForSingleObject(mutex, 25) : WAIT_FAILED;
    if (!mutex || (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED)) {
        if (mutex) CloseHandle(mutex);
        return;
    }
    const std::wstring path = DiagnosticPath();
    if (!path.empty()) {
        HANDLE file = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file != INVALID_HANDLE_VALUE) {
            DWORD written{};
            WriteFile(file, line, static_cast<DWORD>((std::min)(length, static_cast<int>(sizeof(line) - 1))), &written, nullptr);
            CloseHandle(file);
        }
    }
    ReleaseMutex(mutex);
    CloseHandle(mutex);
}


} // namespace enput
