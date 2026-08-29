#pragma once

#include <string>

namespace enput {

inline std::wstring NormalizeEscapedLineBreaks(std::wstring text) {
    std::wstring normalized;
    normalized.reserve(text.size());
    for (size_t index = 0; index < text.size(); ++index) {
        if (text[index] != L'\\' || index + 1 >= text.size()) {
            normalized += text[index];
            continue;
        }
        if (text[index + 1] == L'n') {
            normalized += L'\n';
            ++index;
            continue;
        }
        if (text[index + 1] == L'r') {
            normalized += L'\n';
            ++index;
            if (index + 2 < text.size() && text[index + 1] == L'\\' && text[index + 2] == L'n') index += 2;
            continue;
        }
        normalized += text[index];
    }
    return normalized;
}

} // namespace enput
