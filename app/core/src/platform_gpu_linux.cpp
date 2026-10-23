/*
 * Display adapters on Linux via /sys/class/drm.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_gpu.h"

#include <algorithm>
#include <cctype>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>

namespace archoera {
namespace {

namespace fs = std::filesystem;

std::string trim(std::string value) {
  while (!value.empty() &&
         std::isspace(static_cast<unsigned char>(value.back())) != 0) {
    value.pop_back();
  }
  size_t start = 0;
  while (start < value.size() &&
         std::isspace(static_cast<unsigned char>(value[start])) != 0) {
    ++start;
  }
  return value.substr(start);
}

std::string read_line(const fs::path& path) {
  std::ifstream stream(path);
  std::string line;
  if (!std::getline(stream, line)) return {};
  return trim(line);
}

/// "card0", "card1" … but not "card0-DP-1" or "renderD128".
bool is_card_name(const std::string& name) {
  if (name.size() < 5 || name.rfind("card", 0) != 0) return false;
  for (size_t index = 4; index < name.size(); ++index) {
    if (std::isdigit(static_cast<unsigned char>(name[index])) == 0) return false;
  }
  return true;
}

std::string vendor_from_pci(const std::string& id) {
  if (id == "0x10de") return "nvidia";
  if (id == "0x1002") return "amd";
  if (id == "0x8086") return "intel";
  if (id == "0x13b5") return "arm";
  if (id == "0x1af4") return "virtio";
  if (id == "0x1414") return "microsoft";
  return "unknown";
}

std::string uevent_value(const fs::path& uevent, const std::string& key) {
  std::ifstream stream(uevent);
  std::string line;
  const std::string prefix = key + "=";
  while (std::getline(stream, line)) {
    if (line.rfind(prefix, 0) == 0) return trim(line.substr(prefix.size()));
  }
  return {};
}

GpuStatus probe() {
  GpuStatus status;
  status.platform = "linux";

  std::error_code ec;
  const fs::path drm("/sys/class/drm");
  if (!fs::exists(drm, ec)) return status;

  std::vector<fs::path> cards;
  for (const auto& entry : fs::directory_iterator(drm, ec)) {
    if (!is_card_name(entry.path().filename().string())) continue;
    cards.push_back(entry.path());
  }
  std::sort(cards.begin(), cards.end());

  bool have_primary = false;
  bool have_discrete = false;
  bool have_integrated = false;

  for (const auto& card : cards) {
    const fs::path device = card / "device";
    const std::string vendor_id = read_line(device / "vendor");
    if (vendor_id.empty()) continue;

    GpuDevice gpu;
    gpu.vendor = vendor_from_pci(vendor_id);
    const std::string pci_id = uevent_value(device / "uevent", "PCI_ID");
    gpu.name = pci_id.empty() ? gpu.vendor : gpu.vendor + " (" + pci_id + ")";
    // A bound driver creates this symlink. NVIDIA also needs its module: the
    // offload environment variables do nothing under nouveau.
    gpu.driver_loaded = fs::exists(device / "driver", ec);
    if (gpu.vendor == "nvidia") {
      gpu.driver_loaded = fs::exists("/sys/module/nvidia", ec) || gpu.driver_loaded;
    }

    long long vram_bytes = 0;
    const std::string vram = read_line(device / "mem_info_vram_total");
    if (!vram.empty()) {
      try {
        vram_bytes = std::stoll(vram);
      } catch (...) {
        vram_bytes = 0;
      }
    }
    if (vram_bytes > 0) {
      gpu.discrete = vram_bytes >= 1024LL * 1024LL * 1024LL;
    } else {
      // No VRAM counter: NVIDIA and AMD cards are dedicated in practice,
      // Intel adapters are integrated unless the driver says otherwise.
      gpu.discrete = gpu.vendor == "nvidia" || gpu.vendor == "amd";
    }

    if (!have_primary && read_line(device / "boot_vga") == "1") {
      status.primary_vendor = gpu.vendor;
      have_primary = true;
    }

    have_discrete = have_discrete || gpu.discrete;
    have_integrated = have_integrated || !gpu.discrete;
    status.gpus.push_back(std::move(gpu));
  }

  if (!have_primary && !status.gpus.empty()) {
    status.primary_vendor = status.gpus.front().vendor;
  }
  status.hybrid = have_discrete && have_integrated;
  return status;
}

}  // namespace

const GpuStatus& sample_gpu_status() {
  static const GpuStatus status = probe();
  return status;
}

}  // namespace archoera
