/*
 * Display adapter probe for launch optimization.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Dart never reads sysfs, the registry or Win32 adapter APIs. The core reports
 * facts (vendor, dedicated or integrated, driver bound) and the launch module
 * decides what to do with them.
 */

#pragma once

#include <string>
#include <vector>

namespace archoera {

struct GpuDevice {
  /// "nvidia", "amd", "intel", "apple", "arm", "virtio", "microsoft" or "unknown".
  std::string vendor;
  /// Human-readable label; empty when the platform exposes none.
  std::string name;
  /// True for a dedicated card, false for an integrated GPU.
  bool discrete = false;
  /// False when no kernel driver is bound (e.g. a card left on nouveau).
  bool driver_loaded = true;
};

struct GpuStatus {
  std::string platform;
  std::vector<GpuDevice> gpus;
  /// Vendor of the boot/default adapter; empty when unknown.
  std::string primary_vendor;
  /// True when a dedicated and an integrated adapter coexist.
  bool hybrid = false;
};

/// Probes display adapters once per process and caches the result. Never
/// throws; unsupported platforms return an empty list.
const GpuStatus& sample_gpu_status();

}  // namespace archoera
