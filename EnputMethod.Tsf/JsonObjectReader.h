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
            case 'u':
                if (position_ + 4 > text_.size()) return false;
                {
                    unsigned int codePoint = 0;
                    for (int index = 0; index < 4; ++index) {
                        const char hex = text_[position_++];
                        const int digit = hex >= '0' && hex <= '9' ? hex - '0' : hex >= 'a' && hex <= 'f' ? hex - 'a' + 10 : hex >= 'A' && hex <= 'F' ? hex - 'A' + 10 : -1;
                        if (digit < 0) return false;
                        codePoint = (codePoint << 4) | static_cast<unsigned int>(digit);
                    }
                    if (codePoint < 0x80) value->push_back(static_cast<char>(codePoint));
                    else if (codePoint < 0x800) { value->push_back(static_cast<char>(0xC0 | (codePoint >> 6))); value->push_back(static_cast<char>(0x80 | (codePoint & 0x3F))); }
                    else { value->push_back(static_cast<char>(0xE0 | (codePoint >> 12))); value->push_back(static_cast<char>(0x80 | ((codePoint >> 6) & 0x3F))); value->push_back(static_cast<char>(0x80 | (codePoint & 0x3F))); }
                }
                break;
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

struct Value {
    enum class Type { Null, String, Number, Boolean, Array, Object };
    Type type = Type::Null;
    std::string string;
    double number = 0;
    bool boolean = false;
    std::vector<Value> array;
    std::unordered_map<std::string, Value> object;
};

class DocumentReader final {
public:
    explicit DocumentReader(const std::string& text) : text_(text) {}

    bool Read(Value* value) {
        if (!value) return false;
        if (text_.size() >= 3 && static_cast<unsigned char>(text_[0]) == 0xEF &&
            static_cast<unsigned char>(text_[1]) == 0xBB && static_cast<unsigned char>(text_[2]) == 0xBF) position_ = 3;
        SkipWhitespace();
        return ReadValue(value) && AtEnd();
    }

private:
    bool ReadValue(Value* value) {
        SkipWhitespace();
        if (position_ >= text_.size()) return false;
        const char character = text_[position_];
        if (character == '{') return ReadObject(value);
        if (character == '[') return ReadArray(value);
        if (character == '"') { value->type = Value::Type::String; return ReadString(&value->string); }
        if (text_.compare(position_, 4, "true") == 0) { position_ += 4; value->type = Value::Type::Boolean; value->boolean = true; return true; }
        if (text_.compare(position_, 5, "false") == 0) { position_ += 5; value->type = Value::Type::Boolean; value->boolean = false; return true; }
        if (text_.compare(position_, 4, "null") == 0) { position_ += 4; value->type = Value::Type::Null; return true; }
        const char* start = text_.c_str() + position_;
        char* end = nullptr;
        const double number = std::strtod(start, &end);
        if (end == start) return false;
        position_ += static_cast<size_t>(end - start);
        value->type = Value::Type::Number;
        value->number = number;
        return true;
    }

    bool ReadObject(Value* value) {
        if (!Consume('{')) return false;
        value->type = Value::Type::Object;
        value->object.clear();
        SkipWhitespace();
        if (Consume('}')) return true;
        while (true) {
            std::string key;
            if (!ReadString(&key)) return false;
            SkipWhitespace();
            if (!Consume(':')) return false;
            Value member;
            if (!ReadValue(&member)) return false;
            value->object[std::move(key)] = std::move(member);
            SkipWhitespace();
            if (Consume('}')) return true;
            if (!Consume(',')) return false;
            SkipWhitespace();
        }
    }

    bool ReadArray(Value* value) {
        if (!Consume('[')) return false;
        value->type = Value::Type::Array;
        value->array.clear();
        SkipWhitespace();
        if (Consume(']')) return true;
        while (true) {
            Value item;
            if (!ReadValue(&item)) return false;
            value->array.push_back(std::move(item));
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
            if (character != '\\') { value->push_back(character); continue; }
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
            case 'u':
                if (position_ + 4 > text_.size()) return false;
                {
                    unsigned int codePoint = 0;
                    for (int index = 0; index < 4; ++index) {
                        const char hex = text_[position_++];
                        const int digit = hex >= '0' && hex <= '9' ? hex - '0' : hex >= 'a' && hex <= 'f' ? hex - 'a' + 10 : hex >= 'A' && hex <= 'F' ? hex - 'A' + 10 : -1;
                        if (digit < 0) return false;
                        codePoint = (codePoint << 4) | static_cast<unsigned int>(digit);
                    }
                    if (codePoint < 0x80) value->push_back(static_cast<char>(codePoint));
                    else if (codePoint < 0x800) { value->push_back(static_cast<char>(0xC0 | (codePoint >> 6))); value->push_back(static_cast<char>(0x80 | (codePoint & 0x3F))); }
                    else { value->push_back(static_cast<char>(0xE0 | (codePoint >> 12))); value->push_back(static_cast<char>(0x80 | ((codePoint >> 6) & 0x3F))); value->push_back(static_cast<char>(0x80 | (codePoint & 0x3F))); }
                }
                break;
            default: return false;
            }
        }
        return false;
    }

    bool Consume(char character) { if (position_ >= text_.size() || text_[position_] != character) return false; ++position_; return true; }
    void SkipWhitespace() { while (position_ < text_.size() && std::isspace(static_cast<unsigned char>(text_[position_]))) ++position_; }
    bool AtEnd() { SkipWhitespace(); return position_ == text_.size(); }

    const std::string& text_;
    size_t position_ = 0;
};

inline bool ReadDocument(const std::string& text, Value* value) { return DocumentReader(text).Read(value); }

inline const Value* ObjectValue(const Value& object, const char* key) {
    if (object.type != Value::Type::Object) return nullptr;
    const auto value = object.object.find(key);
    return value == object.object.end() ? nullptr : &value->second;
}

} // namespace enput::json
