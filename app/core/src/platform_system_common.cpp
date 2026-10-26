/*
 * Shared validation for native system actions.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_system.h"

#include <cctype>
#include <filesystem>
#include <system_error>

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

bool is_absolute_path(const std::string& path) {
#if defined(_WIN32)
  // Drive-qualified ("C:\dir", "C:/dir") or UNC ("\\server\share").
  if (path.size() >= 3 &&
      std::isalpha(static_cast<unsigned char>(path[0])) != 0 &&
      path[1] == ':' && (path[2] == '\\' || path[2] == '/')) {
    return true;
  }
  return path.size() >= 2 && path[0] == '\\' && path[1] == '\\';
#else
  return !path.empty() && path[0] == '/';
#endif
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

bool is_safe_absolute_path(const std::string& path) {
  // 4096 is the usual PATH_MAX; anything longer cannot name a real directory
  // on the platforms we support.
  if (path.empty() || path.size() > 4096) {
    return false;
  }
  for (const unsigned char ch : path) {
    if (ch < 0x20 || ch == 0x7f) {
      return false;
    }
  }
  return is_absolute_path(path);
}

bool is_safe_directory_path(const std::string& path) {
  if (!is_safe_absolute_path(path)) {
    return false;
  }

  // Directories only: handing a file to the platform "open" verb would launch
  // an arbitrary application, so files and missing paths are rejected here
  // instead of at the backend.
  // The ABI carries UTF-8; going through u8string keeps non-ASCII Windows
  // paths from being decoded with the process ANSI code page.
  const std::filesystem::path fs_path(
      std::u8string(path.begin(), path.end()));
  std::error_code code;
  const bool directory = std::filesystem::is_directory(fs_path, code);
  return directory && !code;
}

}  // namespace archoera
