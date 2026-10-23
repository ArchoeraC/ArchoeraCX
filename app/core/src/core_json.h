/*
 * JSON string plumbing for internal payloads.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Two responsibilities: writing escaped members into hand-built JSON, and the
 * minimal string/int extraction used by the few core methods that take scalar
 * parameters. Payloads come from Dart's jsonEncode, so extraction only needs
 * string escapes plus correct surrogate-pair handling.
 */

#pragma once

#include <string>
#include <string_view>

namespace archoera {

/* Escapes `text` for safe inclusion inside a JSON string member. */
void append_json_escaped(std::string& out, std::string_view text);

/*
 * Decodes the string member `key` into `out` (append mode: existing content
 * is replaced). Returns false for a missing key, malformed escapes or a broken
 * surrogate pair; single lone surrogates are rejected as malformed.
 */
bool extract_json_string(std::string_view json,
                         std::string_view key,
                         std::string* out);

/* Decodes the integer member `key`; false when missing or not an integer. */
bool extract_json_int(std::string_view json,
                      std::string_view key,
                      long long* out);

}  // namespace archoera
