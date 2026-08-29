#pragma once

#include <cstddef>
#include <windows.h>

namespace enput {

constexpr int CandidateIndex(WPARAM key) noexcept {
    if (key >= '1' && key <= '9') return static_cast<int>(key - '1');
    if (key >= VK_NUMPAD1 && key <= VK_NUMPAD9) return static_cast<int>(key - VK_NUMPAD1);
    return -1;
}

constexpr bool TryGetCandidateIndex(WPARAM key, std::size_t candidateCount, std::size_t* index) noexcept {
    const int candidateIndex = CandidateIndex(key);
    if (candidateIndex < 0 || static_cast<std::size_t>(candidateIndex) >= candidateCount) return false;
    if (index) *index = static_cast<std::size_t>(candidateIndex);
    return true;
}

} // namespace enput
