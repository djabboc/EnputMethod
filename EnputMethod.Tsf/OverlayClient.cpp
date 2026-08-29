#include "OverlayClient.h"

#include "JsonObjectReader.h"
#include "OverlayDiagnostics.h"

#include <windows.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <deque>
#include <mutex>
#include <thread>
#include <utility>

namespace enput {
namespace {

constexpr wchar_t kPipePath[] = L"\\\\.\\pipe\\EnputMethod.Overlay.v1";
std::atomic<unsigned long> g_nextClientOrdinal = 0;
constexpr wchar_t kOverlayUpdateMutexName[] = L"Local\\EnputMethod.Overlay.Updating.v1";

bool IsOverlayUpdateInProgress() {
    HANDLE mutex = OpenMutexW(SYNCHRONIZE, FALSE, kOverlayUpdateMutexName);
    if (!mutex) return false;
    CloseHandle(mutex);
    return true;
}


std::string MakeClientId() {
    return "host-" + std::to_string(GetCurrentProcessId()) + "-" +
        std::to_string(GetTickCount64()) + "-" + std::to_string(++g_nextClientOrdinal);
}

const enput::json::Value* Member(const enput::json::Value& value, const char* key, enput::json::Value::Type type) {
    const enput::json::Value* member = enput::json::ObjectValue(value, key);
    return member && member->type == type ? member : nullptr;
}

} // namespace

class OverlayClient::Impl final {
public:
    Impl(std::wstring overlayExecutable, EventCallback eventCallback)
        : overlayExecutable_(std::move(overlayExecutable)), eventCallback_(std::move(eventCallback)), clientId_(MakeClientId()) {
        stopEvent_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    }

    ~Impl() {
        Stop();
        if (stopEvent_) CloseHandle(stopEvent_);
    }

    void Start() {
        if (!stopEvent_ || worker_.joinable()) return;
        ResetEvent(stopEvent_);
        WriteOverlayDiagnostic("client.start", clientId_);
        worker_ = std::thread([this] { Run(); });
    }

    void Stop() {
        if (!worker_.joinable()) return;
        SetEvent(stopEvent_);
        worker_.join();
        connected_.store(false);
        WriteOverlayDiagnostic("client.stop", clientId_);
    }

    bool Publish(std::string message) {
        return PublishBatch({ std::move(message) });
    }

    bool PublishBatch(std::vector<std::string> messages) {
        if (!connected_.load() || messages.empty() || std::any_of(messages.begin(), messages.end(), [](const std::string& message) { return message.empty(); })) {
            WriteOverlayDiagnostic("publish.skipped", connected_.load() ? "invalid-message" : "not-connected");
            return false;
        }
        std::scoped_lock lock(queueLock_);
        queuedMessages_.clear();
        for (std::string& message : messages) queuedMessages_.push_back(std::move(message));
        return true;
    }

    bool IsConnected() const { return connected_.load(); }
    const std::string& ClientId() const { return clientId_; }

    void Run() {
        HANDLE pipe = INVALID_HANDLE_VALUE;
        bool wasConnected = false;
        ULONGLONG lastLaunch = 0;
        std::string input;

        while (WaitForSingleObject(stopEvent_, 0) == WAIT_TIMEOUT) {
            if (pipe == INVALID_HANDLE_VALUE) {
                if (IsOverlayUpdateInProgress()) {
                    WaitForSingleObject(stopEvent_, 250);
                    continue;
                }

                pipe = CreateFileW(kPipePath, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
                if (pipe == INVALID_HANDLE_VALUE) {
                    const ULONGLONG now = GetTickCount64();
                    if (!overlayExecutable_.empty() && now - lastLaunch >= 3000) {
                        LaunchOverlay();
                        lastLaunch = now;
                    }
                    WaitForSingleObject(stopEvent_, 25);
                    continue;
                }
                DWORD mode = PIPE_READMODE_BYTE;
                SetNamedPipeHandleState(pipe, &mode, nullptr, nullptr);
                input.clear();
            }

            bool pipeFailed = false;
            ReadMessages(pipe, &input, &pipeFailed, &wasConnected);
            if (!pipeFailed && connected_.load()) WriteQueuedMessages(pipe, &pipeFailed);
            if (pipeFailed) {
                CloseHandle(pipe);
                pipe = INVALID_HANDLE_VALUE;
                if (connected_.exchange(false) || wasConnected) {
                    wasConnected = false;
                    WriteOverlayDiagnostic("client.disconnected", clientId_);
                    eventCallback_(OverlayEvent{ OverlayEventType::Disconnected });
                }
                continue;
            }
            WaitForSingleObject(stopEvent_, 16);
        }

        if (pipe != INVALID_HANDLE_VALUE) CloseHandle(pipe);
    }

    void LaunchOverlay() const {
        if (GetFileAttributesW(overlayExecutable_.c_str()) == INVALID_FILE_ATTRIBUTES) return;
        std::wstring commandLine = L"\"" + overlayExecutable_ + L"\"";
        STARTUPINFOW startupInfo{ sizeof(startupInfo) };
        PROCESS_INFORMATION processInfo{};
        if (CreateProcessW(nullptr, commandLine.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startupInfo, &processInfo)) {
            WriteOverlayDiagnostic("overlay.launch", "started");
            CloseHandle(processInfo.hThread);
            CloseHandle(processInfo.hProcess);
        }
    }

    void ReadMessages(HANDLE pipe, std::string* input, bool* pipeFailed, bool* wasConnected) {
        DWORD available = 0;
        if (!PeekNamedPipe(pipe, nullptr, 0, nullptr, &available, nullptr)) {
            *pipeFailed = true;
            return;
        }
        if (!available) return;
        std::string buffer(available, '\0');
        DWORD read = 0;
        if (!ReadFile(pipe, buffer.data(), available, &read, nullptr)) {
            *pipeFailed = true;
            return;
        }
        input->append(buffer.data(), read);
        size_t newline = input->find('\n');
        while (newline != std::string::npos) {
            std::string line = input->substr(0, newline);
            input->erase(0, newline + 1);
            HandleLine(line, wasConnected);
            newline = input->find('\n');
        }
    }

    void HandleLine(const std::string& line, bool* wasConnected) {
        enput::json::Value message;
        if (!enput::json::ReadDocument(line, &message) || message.type != enput::json::Value::Type::Object) return;
        const enput::json::Value* type = Member(message, "type", enput::json::Value::Type::String);
        if (!type) return;
        if (type->string == "ready") {
            const bool wasAlreadyConnected = connected_.exchange(true);
            *wasConnected = true;
            if (!wasAlreadyConnected) {
                WriteOverlayDiagnostic("client.connected", clientId_);
                eventCallback_(OverlayEvent{ OverlayEventType::Connected });
            }
            return;
        }
        if (type->string != "selectCandidate" && type->string != "previousPage" && type->string != "nextPage" && type->string != "dismiss") return;
        const enput::json::Value* clientId = Member(message, "clientId", enput::json::Value::Type::String);
        const enput::json::Value* stateId = Member(message, "stateId", enput::json::Value::Type::Number);
        if (!clientId || !stateId || clientId->string != clientId_ || stateId->number <= 0) return;
        OverlayEvent event{ OverlayEventType::Action, type->string, clientId->string, static_cast<std::uint64_t>(stateId->number) };
        if (type->string == "selectCandidate") {
            const enput::json::Value* candidateIndex = Member(message, "candidateIndex", enput::json::Value::Type::Number);
            if (!candidateIndex || candidateIndex->number < 0 || candidateIndex->number > 1000) return;
            event.candidateIndex = static_cast<int>(candidateIndex->number);
        }
        WriteOverlayDiagnostic("event.received", type->string + " state=" + std::to_string(event.stateId));
        eventCallback_(std::move(event));
    }

    void WriteQueuedMessages(HANDLE pipe, bool* pipeFailed) {
        std::deque<std::string> messages;
        {
            std::scoped_lock lock(queueLock_);
            if (queuedMessages_.empty()) return;
            messages = std::move(queuedMessages_);
            queuedMessages_.clear();
        }
        for (std::string& message : messages) {
            message.push_back('\n');
            DWORD written = 0;
            if (!WriteFile(pipe, message.data(), static_cast<DWORD>(message.size()), &written, nullptr) || written != message.size()) {
                *pipeFailed = true;
                return;
            }
        }
    }

    std::wstring overlayExecutable_;
    EventCallback eventCallback_;
    std::string clientId_;
    HANDLE stopEvent_ = nullptr;
    std::thread worker_;
    std::atomic<bool> connected_ = false;
    std::mutex queueLock_;
    std::deque<std::string> queuedMessages_;
};

OverlayClient::OverlayClient(std::wstring overlayExecutable, EventCallback eventCallback)
    : impl_(std::make_unique<Impl>(std::move(overlayExecutable), std::move(eventCallback))) {}

OverlayClient::~OverlayClient() = default;
void OverlayClient::Start() { impl_->Start(); }
void OverlayClient::Stop() { impl_->Stop(); }
bool OverlayClient::IsConnected() const { return impl_->IsConnected(); }
const std::string& OverlayClient::ClientId() const { return impl_->ClientId(); }
bool OverlayClient::Publish(std::string message) { return impl_->Publish(std::move(message)); }
bool OverlayClient::PublishBatch(std::vector<std::string> messages) { return impl_->PublishBatch(std::move(messages)); }

} // namespace enput
