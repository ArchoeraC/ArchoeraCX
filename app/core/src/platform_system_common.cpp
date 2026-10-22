/*
 * Shared validation for native system actions.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_system.h"

#include <cctype>

namespace archoera {
namespace {

bool has_http_scheme(const std::string& url) {
  auto starts_with = [&url](const char* prefix, size_t length) {
    if (url.size() < length) return false;
    for (size_t index = 0; index < length; ++index) {
      const auto actual =
          static_cast<unsigned char>(url[index]);
      const auto expected =
          static_cast<unsigned char>(prefix[index]);
      if (std::tolower(actual) != std::tolower(expected)) {
        return false;
      }
    }
    return true;
  };
  return starts_with("https://", 8) || starts_with("http://", 7);
}

}  // namespace

bool is_safe_external_url(const std::string& url) {
  // Authentication URLs are well under this; the cap keeps a malformed
  // payload from reaching ShellExecute/portal calls.
  if (url.empty() || url.size() > 8192) {
    return false;
  }
  if (!has_http_scheme(url)) {
    return false;
  }
  for (const unsigned char ch : url) {
    if (ch < 0x20 || ch == 0x7f) {
      return false;
    }
  }
  return true;
}

}  // namespace archoera
