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

constexpr bool IsCompositionCursorAtEnd(std::size_t cursor, std::size_t length) noexcept {
    return cursor == length;
}

constexpr bool ShouldInsertIntoComposition(std::size_t cursor, std::size_t length) noexcept {
    return cursor < length;
}

constexpr bool ShouldSelectCandidate(WPARAM key, bool shiftDown, bool bypassSelection, bool cursorAtEnd, std::size_t candidateCount, std::size_t* index = nullptr) noexcept {
    // Candidate selection is an end-of-composition convenience. Editing within
    // composition must preserve every printable key as text.
    if (!cursorAtEnd || bypassSelection) return false;
    // Shifted top-row digits produce punctuation such as '&' and must remain text input.
    if (shiftDown && key >= '0' && key <= '9') return false;
    return TryGetCandidateIndex(key, candidateCount, index);
}

} // namespace enput
