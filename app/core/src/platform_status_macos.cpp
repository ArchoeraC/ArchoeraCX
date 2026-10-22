/*
 * macOS machine load via Mach and sysctl.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Only C APIs are used here (no AppKit), so this file compiles as plain C++.
 */

#include "platform_status.h"

#include <mach/mach.h>
#include <mach/mach_host.h>
#include <sys/mount.h>
#include <sys/sysctl.h>

#include <cstring>
#include <string>
#include <thread>

namespace archoera {
namespace {

std::string sysctl_string(const char* name) {
  size_t size = 0;
  if (sysctlbyname(name, nullptr, &size, nullptr, 0) != 0 || size == 0) {
    return {};
  }
  std::string value(size, '\0');
  if (sysctlbyname(name, value.data(), &size, nullptr, 0) != 0) {
    return {};
  }
  while (!value.empty() && value.back() == '\0') {
    value.pop_back();
  }
  return value;
}

long long sysctl_int64(const char* name) {
  long long value = 0;
  size_t size = sizeof(value);
  if (sysctlbyname(name, &value, &size, nullptr, 0) != 0) return 0;
  return value;
}

std::string read_os_name() {
  const std::string product = sysctl_string("kern.osproductversion");
  const std::string build = sysctl_string("kern.osversion");
  if (product.empty()) return "macOS";
  if (build.empty()) return "macOS " + product;
  return "macOS " + product + " (" + build + ")";
}

}  // namespace

CpuTimes read_cpu_times() {
  host_cpu_load_info_data_t info{};
  mach_msg_type_number_t count = HOST_CPU_LOAD_INFO_COUNT;
  if (host_statistics(mach_host_self(), HOST_CPU_LOAD_INFO,
                      reinterpret_cast<host_info_t>(&info), &count) !=
      KERN_SUCCESS) {
    return {};
  }

  CpuTimes result;
  result.valid = true;
  result.idle = info.cpu_ticks[CPU_STATE_IDLE];
  for (unsigned int index = 0; index < CPU_STATE_MAX; ++index) {
    result.total += info.cpu_ticks[index];
  }
  return result;
}

SystemStatus sample_system_status(const std::string& disk_path) {
  SystemStatus status;
  status.platform = platform_name();
  status.os = read_os_name();

  const unsigned int cores = std::thread::hardware_concurrency();
  status.cores = cores > 0 ? static_cast<int>(cores) : 1;

  double load = 0.0;
  if (getloadavg(&load, 1) == 1) {
    status.load_known = true;
    status.load1 = load;
  }

  const long long total_bytes = sysctl_int64("hw.memsize");
  vm_size_t page_size = 0;
  vm_statistics64_data_t vm{};
  mach_msg_type_number_t vm_count = HOST_VM_INFO64_COUNT;
  const bool have_vm =
      host_page_size(mach_host_self(), &page_size) == KERN_SUCCESS &&
      host_statistics64(mach_host_self(), HOST_VM_INFO64,
                        reinterpret_cast<host_info64_t>(&vm),
                        &vm_count) == KERN_SUCCESS;
  if (total_bytes > 0 && have_vm && page_size > 0) {
    const long long available_pages =
        static_cast<long long>(vm.free_count) +
        static_cast<long long>(vm.inactive_count) +
        static_cast<long long>(vm.speculative_count);
    long long available_bytes =
        available_pages * static_cast<long long>(page_size);
    if (available_bytes > total_bytes) available_bytes = total_bytes;
    status.memory_known = true;
    status.memory_total_mb = total_bytes / 1024 / 1024;
    status.memory_available_mb = available_bytes / 1024 / 1024;
  }

  struct statfs info {};
  const std::string target = disk_path.empty() ? "/" : disk_path;
  if (statfs(target.c_str(), &info) == 0 && info.f_bsize > 0) {
    status.disk_known = true;
    status.disk_path = target;
    status.disk_total_bytes =
        static_cast<long long>(info.f_blocks) * info.f_bsize;
    status.disk_free_bytes =
        static_cast<long long>(info.f_bavail) * info.f_bsize;
  }

  mach_task_basic_info_data_t task{};
  mach_msg_type_number_t task_count = MACH_TASK_BASIC_INFO_COUNT;
  if (task_info(mach_task_self(), MACH_TASK_BASIC_INFO,
                reinterpret_cast<task_info_t>(&task), &task_count) ==
      KERN_SUCCESS) {
    status.process_known = true;
    status.process_working_set_mb =
        static_cast<long long>(task.resident_size / 1024 / 1024);
  }

  struct timeval boot{};
  size_t boot_size = sizeof(boot);
  int mib[2] = {CTL_KERN, KERN_BOOTTIME};
  if (sysctl(mib, 2, &boot, &boot_size, nullptr, 0) == 0 && boot.tv_sec > 0) {
    struct timeval now {};
    gettimeofday(&now, nullptr);
    double seconds = static_cast<double>(now.tv_sec - boot.tv_sec) +
                     static_cast<double>(now.tv_usec - boot.tv_usec) / 1e6;
    if (seconds >= 0.0) {
      status.uptime_known = true;
      status.system_uptime_seconds = seconds;
    }
  }

  return status;
}

}  // namespace archoera
