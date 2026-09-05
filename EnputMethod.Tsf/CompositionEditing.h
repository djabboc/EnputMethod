#pragma once

#include <cstddef>
#include <string>

namespace enput {

inline bool InsertCompositionCharacter(std::wstring* typed, std::size_t* cursor, wchar_t character) {
    if (!typed || !cursor || *cursor > typed->size()) return false;
    typed->insert(*cursor, 1, character);
    ++*cursor;
    return true;
}

} // namespace enput
