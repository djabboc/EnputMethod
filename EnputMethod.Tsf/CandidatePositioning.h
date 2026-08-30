#pragma once

#include <algorithm>
#include <windows.h>

namespace enput {

struct CandidateWindowPlacement {
    int x;
    int y;
    bool placedAbove;
};

inline CandidateWindowPlacement PlaceCandidateWindow(
    const RECT& compositionBounds,
    const SIZE& candidateSize,
    const RECT& workArea,
    int gap = 2) {
    const int workLeft = static_cast<int>(workArea.left);
    const int workTop = static_cast<int>(workArea.top);
    const int workRight = static_cast<int>(workArea.right);
    const int workBottom = static_cast<int>(workArea.bottom);
    const int compositionLeft = static_cast<int>(compositionBounds.left);
    const int compositionTop = static_cast<int>(compositionBounds.top);
    const int compositionBottom = static_cast<int>(compositionBounds.bottom);
    const int candidateWidth = static_cast<int>(candidateSize.cx);
    const int candidateHeight = static_cast<int>(candidateSize.cy);
    const int maximumX = (std::max)(workLeft, workRight - candidateWidth);
    const int x = std::clamp(compositionLeft, workLeft, maximumX);
    const int below = compositionBottom + gap;
    const int above = compositionTop - gap - candidateHeight;
    const bool belowFits = below + candidateHeight <= workBottom;
    const bool aboveFits = above >= workTop;
    if (belowFits) return { x, below, false };
    if (aboveFits) return { x, above, true };

    const int aboveSpace = (std::max)(0, compositionTop - gap - workTop);
    const int belowSpace = (std::max)(0, workBottom - compositionBottom - gap);
    const bool placedAbove = aboveSpace >= belowSpace;
    int y = placedAbove
        ? (std::max)(workTop, above)
        : (std::min)(workBottom - candidateHeight, below);
    if (workBottom - workTop >= candidateHeight) {
        y = std::clamp(y, workTop, workBottom - candidateHeight);
    }
    return { x, y, placedAbove };
}

} // namespace enput
