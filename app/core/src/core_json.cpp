/*
 * JSON string plumbing for internal payloads.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "core_json.h"

#include <cstdlib>
#include <cstdint>

namespace archoera {
namespace {

void append_utf8(std::string& out, uint32_t code_point) {
  if (code_point <= 0x7F) {
    out.push_back(static_cast<char>(code_point));
  } else if (code_point <= 0x7FF) {
    out.push_back(static_cast<char>(0xC0 | (code_point >> 6)));
    out.push_back(static_cast<char>(0x80 | (code_point & 0x3F)));
  } else if (code_point <= 0xFFFF) {
    out.push_back(static_cast<char>(0xE0 | (code_point >> 12)));
    out.push_back(static_cast<char>(0x80 | ((code_point >> 6) & 0x3F)));
    out.push_back(static_cast<char>(0x80 | (code_point & 0x3F)));
  } else {
    out.push_back(static_cast<char>(0xF0 | (code_point >> 18)));
    out.push_back(static_cast<char>(0x80 | ((code_point >> 12) & 0x3F)));
    out.push_back(static_cast<char>(0x80 | ((code_point >> 6) & 0x3F)));
    out.push_back(static_cast<char>(0x80 | (code_point & 0x3F)));
  }
}

int hex_value(char ch) {
  if (ch >= '0' && ch <= '9') return ch - '0';
  if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
  if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
  return -1;
}

/* Skips whitespace and reports whether `position` still points at a char. */
bool skip_spaces(std::string_view json, size_t* position) {
  while (*position < json.size() && (json[*position] == ' ' ||
                                     json[*position] == '\t' ||
                                     json[*position] == '\r' ||
                                     json[*position] == '\n')) {
    ++(*position);
  }
  return *position < json.size();
}

/*
 * Finds the end of the `"key"` member heading, i.e. the position right after
 * the closing quote. Returns false when the key is absent.
 */
bool jump_after_key(std::string_view json,
                    std::string_view key,
                    size_t* cursor) {
  if (key.empty()) {
    return false;
  }
  const std::string needle = std::string("\"") + std::string(key) + "\"";
  const size_t key_at = json.find(needle);
  if (key_at == std::string_view::npos) {
    return false;
  }
  *cursor = key_at + needle.size();
  return true;
}

/*
 * Parses the four hex digits of a `\u` escape; `position` points past the
 * `\u`. Returns false at a short or non-hex escape.
 */
bool parse_unicode_escape(std::string_view json,
                          size_t position,
                          uint32_t* unit) {
  if (position + 4 > json.size()) {
    return false;
  }
  uint32_t value = 0;
  for (int index = 0; index < 4; ++index) {
    const int digit = hex_value(json[position + index]);
    if (digit < 0) {
      return false;
    }
    value = (value << 4) | static_cast<uint32_t>(digit);
  }
  *unit = value;
  return true;
}

}  // namespace

void append_json_escaped(std::string& out, std::string_view text) {
  for (const char ch : text) {
    switch (ch) {
      case '\\':
        out.append("\\\\");
        break;
      case '"':
        out.append("\\\"");
        break;
      case '\n':
        out.append("\\n");
        break;
      case '\r':
        out.append("\\r");
        break;
      case '\t':
        out.append("\\t");
        break;
      default:
        out.push_back(ch);
        break;
    }
  }
}

bool extract_json_string(std::string_view json,
                         std::string_view key,
                         std::string* out) {
  if (out == nullptr) {
    return false;
  }
  size_t cursor = 0;
  if (!jump_after_key(json, key, &cursor)) {
    return false;
  }
  if (!skip_spaces(json, &cursor) || json[cursor] != ':') {
    return false;
  }
  ++cursor;
  if (!skip_spaces(json, &cursor) || json[cursor] != '"') {
    return false;
  }
  ++cursor;

  std::string value;
  while (cursor < json.size()) {
    const char ch = json[cursor++];
    if (ch == '"') {
      *out = std::move(value);
      return true;
    }
    if (ch != '\\') {
      value.push_back(ch);
      continue;
    }
    if (cursor >= json.size()) {
      return false;
    }
    const char escaped = json[cursor++];
    switch (escaped) {
      case '"':
      case '\\':
      case '/':
        value.push_back(escaped);
        break;
      case 'b':
        value.push_back('\b');
        break;
      case 'f':
        value.push_back('\f');
        break;
      case 'n':
        value.push_back('\n');
        break;
      case 'r':
        value.push_back('\r');
        break;
      case 't':
        value.push_back('\t');
        break;
      case 'u': {
        uint32_t unit = 0;
        if (!parse_unicode_escape(json, cursor, &unit)) {
          return false;
        }
        cursor += 4;
        if (unit >= 0xD800 && unit <= 0xDBFF) {
          // High surrogate: a following \uDC00-\uDFFF low half must complete
          // the pair. Anything else is malformed and cannot produce
          // well-formed UTF-8, so the whole member fails to decode.
          if (cursor + 6 > json.size() || json[cursor] != '\\' ||
              json[cursor + 1] != 'u') {
            return false;
          }
          uint32_t low = 0;
          if (!parse_unicode_escape(json, cursor + 2, &low) || low < 0xDC00 ||
              low > 0xDFFF) {
            return false;
          }
          cursor += 6;
          const uint32_t code_point =
              0x10000 + ((unit - 0xD800) << 10) + (low - 0xDC00);
          append_utf8(value, code_point);
          break;
        }
        if (unit >= 0xDC00 && unit <= 0xDFFF) {
          // A lone low surrogate is a parse failure; encoding one would emit
          // invalid UTF-8 towards every consumer of the string.
          return false;
        }
        append_utf8(value, unit);
        break;
      }
      default:
        return false;
    }
  }
  return false;
}

bool extract_json_int(std::string_view json,
                      std::string_view key,
                      long long* out) {
  if (out == nullptr) {
    return false;
  }
  size_t cursor = 0;
  if (!jump_after_key(json, key, &cursor)) {
    return false;
  }
  if (!skip_spaces(json, &cursor) || json[cursor] != ':') {
    return false;
  }
  ++cursor;
  if (!skip_spaces(json, &cursor)) {
    return false;
  }

  // Copy the numeric token so strtoll never reads past the view.
  std::string token;
  if (json[cursor] == '+' || json[cursor] == '-') {
    token.push_back(json[cursor++]);
  }
  while (cursor < json.size() && json[cursor] >= '0' && json[cursor] <= '9') {
    token.push_back(json[cursor++]);
  }

  const char* begin = token.c_str();
  char* end = nullptr;
  const long long value = std::strtoll(begin, &end, 10);
  if (end == begin) {
    return false;
  }
  *out = value;
  return true;
}

}  // namespace archoera
