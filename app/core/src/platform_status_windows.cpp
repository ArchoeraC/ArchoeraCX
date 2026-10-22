/*
 * Windows machine load via Win32 APIs.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * GetSystemTimes gives cumulative CPU counters (the caller derives usage from
 * two samples), GlobalMemoryStatusEx covers memory, GetDiskFreeSpaceEx covers
 * the disk and K32GetProcessMemoryInfo the launcher working set.
 */

#include "platform_status.h"

#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif

#include <windows.h>
#include <psapi.h>

#include <string>
#include <thread>

namespace archoera {
namespace {

std::wstring widen(const std::string& text) {
  if (text.empty()) return {};
  const int length = MultiByteToWideChar(CP_UTF8, 0, text.data(),
                                         static_cast<int>(text.size()),
                                         nullptr, 0);
  if (length <= 0) return {};
  std::wstring result(static_cast<size_t>(length), L'\0');
  MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
                      result.data(), length);
  return result;
}

unsigned long long to_ticks(const FILETIME& time) {
  ULARGE_INTEGER value{};
  value.LowPart = time.dwLowDateTime;
  value.HighPart = time.dwHighDateTime;
  return value.QuadPart;
}

struct OsVersionInfo {
  ULONG size;
  ULONG major;
  ULONG minor;
  ULONG build;
  ULONG platform_id;
  WCHAR service_pack[128];
};

std::string read_os_name() {
  // GetVersionEx is manifest-dependent; RtlGetVersion always reports the
  // real version. Declaring the struct locally avoids pulling in winternl.h.
  using RtlGetVersionFn = LONG(WINAPI*)(OsVersionInfo*);
  const HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
  if (ntdll != nullptr) {
    const auto fn = reinterpret_cast<RtlGetVersionFn>(
        GetProcAddress(ntdll, "RtlGetVersion"));
    if (fn != nullptr) {
      OsVersionInfo info{};
      info.size = sizeof(info);
      if (fn(&info) == 0) {
        return "Windows " + std::to_string(info.major) + "." +
               std::to_string(info.minor) + " (build " +
               std::to_string(info.build) + ")";
      }
    }
  }
  return "Windows";
}

}  // namespace

CpuTimes read_cpu_times() {
  FILETIME idle_time{};
  FILETIME kernel_time{};
  FILETIME user_time{};
  if (!GetSystemTimes(&idle_time, &kernel_time, &user_time)) return {};

  CpuTimes result;
  result.valid = true;
  result.idle = to_ticks(idle_time);
  // Kernel time already includes idle time.
  result.total = to_ticks(kernel_time) + to_ticks(user_time);
  return result;
}

SystemStatus sample_system_status(const std::string& disk_path) {
  SystemStatus status;
  status.platform = platform_name();
  status.os = read_os_name();

  const DWORD cores = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
  status.cores = cores > 0 ? static_cast<int>(cores) : 1;

  MEMORYSTATUSEX memory{};
  memory.dwLength = sizeof(memory);
  if (GlobalMemoryStatusEx(&memory)) {
    status.memory_known = true;
    status.memory_total_mb =
        static_cast<long long>(memory.ullTotalPhys / 1024 / 1024);
    status.memory_available_mb =
        static_cast<long long>(memory.ullAvailPhys / 1024 / 1024);
  }

  const std::wstring wide_path =
      widen(disk_path.empty() ? std::string(".") : disk_path);
  ULARGE_INTEGER free_available{};
  ULARGE_INTEGER total{};
  ULARGE_INTEGER total_free{};
  if (!wide_path.empty() &&
      GetDiskFreeSpaceExW(wide_path.c_str(), &free_available, &total,
                          &total_free)) {
    status.disk_known = true;
    status.disk_path = disk_path.empty() ? "." : disk_path;
    status.disk_total_bytes = static_cast<long long>(total.QuadPart);
    status.disk_free_bytes = static_cast<long long>(free_available.QuadPart);
  }

  PROCESS_MEMORY_COUNTERS counters{};
  if (K32GetProcessMemoryInfo(GetCurrentProcess(), &counters,
                              sizeof(counters))) {
    status.process_known = true;
    status.process_working_set_mb =
        static_cast<long long>(counters.WorkingSetSize / 1024 / 1024);
  }

  status.uptime_known = true;
  status.system_uptime_seconds =
      static_cast<double>(GetTickCount64()) / 1000.0;

  return status;
}

}  // namespace archoera
