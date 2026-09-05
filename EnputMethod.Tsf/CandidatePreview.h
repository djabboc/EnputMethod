#pragma once

#include <cstddef>
#include <string>
#include <vector>

namespace enput {

inline const std::wstring& CompositionPreviewText(
    const std::wstring& typed,
    const std::vector<std::wstring>& candidates,
    std::size_t selectedIndex,
    bool enabled,
    bool active) {
    if (enabled && active && selectedIndex < candidates.size()) return candidates[selectedIndex];
    return typed;
}

// Turns a display-only candidate preview into the composition being edited.
inline bool PromoteCandidatePreview(
    std::wstring* typed,
    std::size_t* cursor,
    const std::vector<std::wstring>& candidates,
    std::size_t selectedIndex,
    bool active) {
    if (!typed || !cursor || !active || selectedIndex >= candidates.size()) return false;
    *typed = candidates[selectedIndex];
    *cursor = typed->size();
    return true;
}

} // namespace enput
