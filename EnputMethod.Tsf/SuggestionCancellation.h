#pragma once

#include <algorithm>
#include <vector>
#include <windows.h>

namespace enput {

enum class CandidateCancellationAction {
    DismissDetachedSuggestion,
    ExitEmptyEmojiMode,
    FinishComposition,
};

inline bool HasConfiguredShortcut(const std::vector<WPARAM>& keys, WPARAM key) {
    return std::any_of(keys.begin(), keys.end(), [key](WPARAM configuredKey) {
        // Win32 can report a sided Shift key when the user configured generic Shift.
        if (configuredKey == VK_SHIFT) return key == VK_SHIFT || key == VK_LSHIFT || key == VK_RSHIFT;
        return configuredKey == key;
    });
}

inline CandidateCancellationAction ResolveCandidateCancellationAction(
    bool detachedSuggestionActive,
    bool emojiMode,
    bool typedEmpty) {
    if (detachedSuggestionActive) return CandidateCancellationAction::DismissDetachedSuggestion;
    if (emojiMode && typedEmpty) return CandidateCancellationAction::ExitEmptyEmojiMode;
    return CandidateCancellationAction::FinishComposition;
}

} // namespace enput
