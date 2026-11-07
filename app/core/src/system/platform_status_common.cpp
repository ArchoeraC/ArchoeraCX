/*
 * Shared helpers for the machine load sampler.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_status.h"

#include <filesystem>
#include <string>
#include <system_error>

namespace archoera {
namespace {

std::string path_to_utf8(const std::filesystem::path& path) {
  const std::u8string value = path.u8string();
  return std::string(reinterpret_cast<const char*>(value.data()), value.size());
}

}  // namespace

const char* platform_name() {
#if defined(_WIN32)
  return "windows";
#elif defined(__APPLE__)
  return "macos";
#elif defined(__linux__)
  return "linux";
#else
  return "unknown";
#endif
}

std::string status_volume_path(const std::string& path) {
  if (path.empty()) return {};
  try {
    std::error_code code;
    std::filesystem::path candidate(std::u8string(path.begin(), path.end()));
    while (!candidate.empty()) {
      if (std::filesystem::is_directory(candidate, code) && !code) {
        return path_to_utf8(candidate);
      }
      code.clear();
      const std::filesystem::path parent = candidate.parent_path();
      if (parent.empty() || parent == candidate) {
        break;
      }
      candidate = parent;
    }
  } catch (...) {
    // Path conversion is platform-dependent; an unreadable hint only hides the
    // disk row.
  }
  return {};
}

bool cpu_usage_percent(const CpuTimes& previous,
                       const CpuTimes& current,
                       double* percent) {
  if (percent == nullptr || !previous.valid || !current.valid) {
    return false;
  }

  // Counters are monotonic; a reset (suspend/resume on some platforms) is not
  // a usage spike, so report "unknown" for that interval.
  if (current.total <= previous.total || current.idle < previous.idle) {
    return false;
  }

  const auto total_delta = current.total - previous.total;
  const auto idle_delta = current.idle - previous.idle;
  const auto busy_delta = total_delta > idle_delta ? total_delta - idle_delta : 0;
  double value = static_cast<double>(busy_delta) * 100.0 /
                 static_cast<double>(total_delta);
  if (value < 0.0) value = 0.0;
  if (value > 100.0) value = 100.0;
  *percent = value;
  return true;
}

}  // namespace archoera
