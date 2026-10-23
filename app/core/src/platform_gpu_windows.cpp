/*
 * Display adapters on Windows via EnumDisplayDevicesW.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * The adapter string is the only vendor signal available without WMI/DXGI, so
 * vendor and dedicated/integrated are inferred from it. That is enough to
 * decide whether the high-performance GPU preference is worth setting.
 */

#include "platform_gpu.h"

#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif

#include <windows.h>

#include <algorithm>
#include <cctype>
#include <string>

namespace archoera {
namespace {

std::string narrow(const wchar_t* text) {
  if (text == nullptr || *text == L'\0') return {};
  const int length = WideCharToMultiByte(
      CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
  if (length <= 0) return {};
  std::string result(static_cast<size_t>(length), '\0');
  WideCharToMultiByte(CP_UTF8, 0, text, -1, result.data(), length, nullptr,
                      nullptr);
  if (!result.empty() && result.back() == '\0') result.pop_back();
  return result;
}

std::string upper(std::string value) {
  std::transform(value.begin(), value.end(), value.begin(),
                 [](unsigned char c) { return static_cast<char>(std::toupper(c)); });
  return value;
}

std::string vendor_from_name(const std::string& name) {
  const std::string label = upper(name);
  if (label.find("NVIDIA") != std::string::npos) return "nvidia";
  if (label.find("AMD") != std::string::npos ||
      label.find("RADEON") != std::string::npos) {
    return "amd";
  }
  if (label.find("INTEL") != std::string::npos) return "intel";
  if (label.find("APPLE") != std::string::npos) return "apple";
  if (label.find("QUALCOMM") != std::string::npos) return "arm";
  return "unknown";
}

bool is_discrete(const std::string& vendor, const std::string& name) {
  const std::string label = upper(name);
  if (vendor == "nvidia") return true;
  if (vendor == "amd") {
    // "AMD Radeon(TM) Graphics" is an APU; RX / Radeon Pro / FirePro are cards.
    return label.find(" RX ") != std::string::npos ||
           label.find("RADEON PRO") != std::string::npos ||
           label.find("FIREPRO") != std::string::npos;
  }
  if (vendor == "intel") return label.find("ARC") != std::string::npos;
  return false;
}

GpuStatus probe() {
  GpuStatus status;
  status.platform = "windows";

  bool have_discrete = false;
  bool have_integrated = false;
  for (DWORD index = 0;; ++index) {
    DISPLAY_DEVICEW device{};
    device.cb = sizeof(device);
    if (EnumDisplayDevicesW(nullptr, index, &device, 0) == FALSE) break;
    const DWORD flags = device.StateFlags;
    if ((flags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
    if ((flags & DISPLAY_DEVICE_MIRRORING_DRIVER) != 0) continue;

    const std::string name = narrow(device.DeviceString);
    if (name.empty()) continue;

    GpuDevice gpu;
    gpu.vendor = vendor_from_name(name);
    gpu.name = name;
    gpu.discrete = is_discrete(gpu.vendor, name);
    gpu.driver_loaded = true;
    if (status.primary_vendor.empty()) status.primary_vendor = gpu.vendor;
    have_discrete = have_discrete || gpu.discrete;
    have_integrated = have_integrated || !gpu.discrete;
    status.gpus.push_back(std::move(gpu));
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
