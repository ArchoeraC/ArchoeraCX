/*
 * Machine load sampling (CPU / memory / disk / process / uptime).
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * The core owns this platform detail so Dart never reads /proc, the registry
 * or Mach APIs, and never polls. One-shot reads use sample_system_status()
 * plus two read_cpu_times() samples; the push watcher keeps the previous CPU
 * counters and reports the delta on every interval.
 */

#pragma once

#include <cstdint>
#include <string>

namespace archoera {

/// Cumulative CPU counters used to derive usage between two reads.
struct CpuTimes {
  bool valid = false;
  unsigned long long idle = 0;
  unsigned long long total = 0;
};

struct SystemStatus {
  std::string platform;
  std::string os;
  int cores = 0;

  bool load_known = false;
  double load1 = 0.0;

  bool memory_known = false;
  long long memory_total_mb = 0;
  long long memory_available_mb = 0;

  bool disk_known = false;
  std::string disk_path;
  long long disk_total_bytes = 0;
  long long disk_free_bytes = 0;

  bool process_known = false;
  long long process_working_set_mb = 0;

  bool uptime_known = false;
  double system_uptime_seconds = 0.0;
};

/// Platform label used in payloads: "linux", "windows" or "macos".
const char* platform_name();

/// Reads aggregate CPU counters. Returns an invalid result when the platform
/// does not expose them (or the read fails); callers then omit the percentage.
CpuTimes read_cpu_times();

/// Samples everything except CPU usage. Never throws; unknown metrics stay
/// unset so the UI can hide them instead of showing a fake zero.
SystemStatus sample_system_status(const std::string& disk_path);

/// CPU usage in [0, 100] between two samples, or false when it cannot be
/// derived (first sample, counter reset, zero elapsed ticks).
bool cpu_usage_percent(const CpuTimes& previous,
                       const CpuTimes& current,
                       double* percent);

}  // namespace archoera
