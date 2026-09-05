#pragma once

#include <windows.h>

namespace enput {

constexpr bool IsFunctionKey(WPARAM key) noexcept {
    return key >= VK_F1 && key <= VK_F24;
}

// Function keys conventionally belong to the active application. A configured
// Enput action may claim one only while the user is already editing with Enput.
constexpr bool ShouldClaimFunctionKey(WPARAM key, bool hasInputContext, bool isConfiguredInputAction) noexcept {
    return !IsFunctionKey(key) || (hasInputContext && isConfiguredInputAction);
}

} // namespace enput
