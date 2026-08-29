#pragma once

#include <cwctype>
#include <string>

namespace enput {

inline bool IsIgnoredMatchSeparator(wchar_t character) {
    return character == L'_' || character == L'-' || iswspace(character);
}

inline bool IsOrderedSubsequence(const std::wstring& query, const std::wstring& candidate, bool ignoreSeparators = false) {
    if (query.empty()) return false;
    size_t queryIndex = 0;
    for (wchar_t character : candidate) {
        if (ignoreSeparators && IsIgnoredMatchSeparator(character)) continue;
        if (queryIndex < query.size() && towlower(character) == towlower(query[queryIndex])) ++queryIndex;
    }
    return queryIndex == query.size();
}

inline std::wstring LikeOrderedSubsequencePattern(const std::wstring& query) {
    std::wstring pattern = L"%";
    for (wchar_t character : query) {
        if (character == L'%' || character == L'_' || character == L'\\') pattern += L'\\';
        pattern += character;
        pattern += L'%';
    }
    return pattern;
}

} // namespace enput
