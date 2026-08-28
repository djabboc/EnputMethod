#pragma once

#include <cstdint>
#include <functional>
#include <memory>
#include <string>

namespace enput {

enum class OverlayEventType {
    Action,
    Disconnected,
};

struct OverlayEvent {
    OverlayEventType type = OverlayEventType::Action;
    std::string action;
    std::string clientId;
    std::uint64_t stateId = 0;
    int candidateIndex = -1;
};

// Owns the asynchronous pipe connection. Its callback runs on the worker thread.
class OverlayClient final {
public:
    using EventCallback = std::function<void(OverlayEvent)>;

    OverlayClient(std::wstring overlayExecutable, EventCallback eventCallback);
    ~OverlayClient();

    OverlayClient(const OverlayClient&) = delete;
    OverlayClient& operator=(const OverlayClient&) = delete;

    void Start();
    void Stop();
    bool IsConnected() const;
    const std::string& ClientId() const;

    // Uses a bounded queue so paired candidate and translation updates stay ordered.
    bool Publish(std::string message);

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace enput
