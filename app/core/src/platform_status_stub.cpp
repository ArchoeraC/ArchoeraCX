/*
 * Machine load fallback for platforms without a native backend.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Every metric stays unknown so the UI hides the rows instead of showing
 * fabricated values.
 */

#include "platform_status.h"

#include <thread>

namespace archoera {

CpuTimes read_cpu_times() {
  return {};
}

SystemStatus sample_system_status(const std::string& disk_path) {
  SystemStatus status;
  status.platform = platform_name();
  const unsigned int cores = std::thread::hardware_concurrency();
  status.cores = cores > 0 ? static_cast<int>(cores) : 1;
  status.disk_path = disk_path;
  return status;
}

}  // namespace archoera
