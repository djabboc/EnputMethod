#pragma once

#include <algorithm>
#include <array>
#include <string>
#include <string_view>
#include <utility>

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

// ECDICT stores a small amount of template markup and inconsistent POS prefixes.
// Normalize only known presentation syntax; dictionary punctuation remains untouched.
inline std::wstring NormalizeDictionaryText(std::wstring text) {
    text = NormalizeEscapedLineBreaks(std::move(text));

    constexpr std::wstring_view kAlternativeMarker = L"{{or}}";
    size_t marker = 0;
    while ((marker = text.find(kAlternativeMarker, marker)) != std::wstring::npos) {
        text.replace(marker, kAlternativeMarker.size(), L"or");
        marker += 2;
    }

    constexpr std::array<std::wstring_view, 11> kPartOfSpeechPrefixes{
        L"n", L"v", L"vt", L"vi", L"adj", L"adv", L"prep", L"pron", L"conj", L"int", L"art"
    };
    std::wstring normalized;
    normalized.reserve(text.size() + 8);
    for (size_t index = 0; index < text.size();) {
        const bool lineStart = index == 0 || text[index - 1] == L'\n';
        if (lineStart) {
            const size_t tokenEnd = text.find_first_of(L" \t\n", index);
            const size_t tokenLength = (tokenEnd == std::wstring::npos ? text.size() : tokenEnd) - index;
            const std::wstring_view token(text.data() + index, tokenLength);
            const bool knownPrefix = std::find(kPartOfSpeechPrefixes.begin(), kPartOfSpeechPrefixes.end(), token) != kPartOfSpeechPrefixes.end();
            if (knownPrefix && tokenEnd != std::wstring::npos && text[tokenEnd] != L'\n') {
                normalized.append(token);
                normalized += L'.';
                index = tokenEnd;
                continue;
            }
        }
        normalized += text[index++];
    }
    return normalized;
}

} // namespace enput
