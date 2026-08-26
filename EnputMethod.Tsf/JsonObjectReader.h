#pragma once

#include <cctype>
#include <cstdlib>
#include <string>
#include <unordered_map>
#include <vector>

namespace enput::json {

struct Object {
    std::unordered_map<std::string, std::string> strings;
    std::unordered_map<std::string, double> numbers;
    std::unordered_map<std::string, bool> booleans;
    std::unordered_map<std::string, std::vector<std::string>> stringArrays;
};

class ObjectReader final {
public:
    explicit ObjectReader(const std::string& text) : text_(text) {}

    bool Read(Object* object) {
        if (!object) return false;
        if (text_.size() >= 3 && static_cast<unsigned char>(text_[0]) == 0xEF &&
            static_cast<unsigned char>(text_[1]) == 0xBB && static_cast<unsigned char>(text_[2]) == 0xBF) {
            position_ = 3;
        }
        SkipWhitespace();
        if (!Consume('{')) return false;
        SkipWhitespace();
        if (Consume('}')) return AtEnd();
        while (true) {
            std::string key;
            if (!ReadString(&key)) return false;
            SkipWhitespace();
            if (!Consume(':')) return false;
            SkipWhitespace();
            if (!ReadValue(key, object)) return false;
            SkipWhitespace();
            if (Consume('}')) return AtEnd();
            if (!Consume(',')) return false;
            SkipWhitespace();
        }
    }

private:
    bool ReadValue(const std::string& key, Object* object) {
        if (position_ >= text_.size()) return false;
        if (text_[position_] == '"') {
            std::string value;
            if (!ReadString(&value)) return false;
            object->strings[key] = std::move(value);
            return true;
        }
        if (text_[position_] == '[') {
            std::vector<std::string> values;
            if (!ReadStringArray(&values)) return false;
            object->stringArrays[key] = std::move(values);
            return true;
        }
        if (text_.compare(position_, 4, "true") == 0) {
            position_ += 4;
            object->booleans[key] = true;
            return true;
        }
        if (text_.compare(position_, 5, "false") == 0) {
            position_ += 5;
            object->booleans[key] = false;
            return true;
        }
        const char* start = text_.c_str() + position_;
        char* end = nullptr;
        const double value = std::strtod(start, &end);
        if (end == start) return false;
        position_ += static_cast<size_t>(end - start);
        object->numbers[key] = value;
        return true;
    }

    bool ReadStringArray(std::vector<std::string>* values) {
        if (!values || !Consume('[')) return false;
        SkipWhitespace();
        if (Consume(']')) return true;
        while (true) {
            std::string value;
            if (!ReadString(&value)) return false;
            values->push_back(std::move(value));
            SkipWhitespace();
            if (Consume(']')) return true;
            if (!Consume(',')) return false;
            SkipWhitespace();
        }
    }

    bool ReadString(std::string* value) {
        if (!value || !Consume('"')) return false;
        value->clear();
        while (position_ < text_.size()) {
            const char character = text_[position_++];
            if (character == '"') return true;
            if (character != '\\') {
                value->push_back(character);
                continue;
            }
            if (position_ >= text_.size()) return false;
            const char escaped = text_[position_++];
            switch (escaped) {
            case '"': value->push_back('"'); break;
            case '\\': value->push_back('\\'); break;
            case '/': value->push_back('/'); break;
            case 'b': value->push_back('\b'); break;
            case 'f': value->push_back('\f'); break;
            case 'n': value->push_back('\n'); break;
            case 'r': value->push_back('\r'); break;
            case 't': value->push_back('\t'); break;
            default: return false;
            }
        }
        return false;
    }

    bool Consume(char character) {
        if (position_ >= text_.size() || text_[position_] != character) return false;
        ++position_;
        return true;
    }

    void SkipWhitespace() {
        while (position_ < text_.size() && std::isspace(static_cast<unsigned char>(text_[position_]))) ++position_;
    }

    bool AtEnd() {
        SkipWhitespace();
        return position_ == text_.size();
    }

    const std::string& text_;
    size_t position_ = 0;
};

inline bool ReadObject(const std::string& text, Object* object) {
    return ObjectReader(text).Read(object);
}

inline std::string StringOr(const Object& object, const char* key, const std::string& fallback) {
    const auto value = object.strings.find(key);
    return value == object.strings.end() ? fallback : value->second;
}

inline double NumberOr(const Object& object, const char* key, double fallback) {
    const auto value = object.numbers.find(key);
    return value == object.numbers.end() ? fallback : value->second;
}

inline bool BooleanOr(const Object& object, const char* key, bool fallback) {
    const auto value = object.booleans.find(key);
    return value == object.booleans.end() ? fallback : value->second;
}

inline const std::vector<std::string>* StringArray(const Object& object, const char* key) {
    const auto value = object.stringArrays.find(key);
    return value == object.stringArrays.end() ? nullptr : &value->second;
}

} // namespace enput::json
