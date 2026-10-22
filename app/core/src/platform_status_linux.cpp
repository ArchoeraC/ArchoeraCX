/*
 * Linux machine load via /proc and statvfs.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_status.h"

#include <sys/statvfs.h>
#include <unistd.h>

#include <cctype>
#include <cstdio>
#include <fstream>
#include <sstream>
#include <string>
#include <thread>

namespace archoera {
namespace {

std::string read_first_line(const char* path) {
  std::ifstream stream(path);
  std::string line;
  if (!std::getline(stream, line)) return {};
  return line;
}

std::string read_os_name() {
  std::ifstream stream("/etc/os-release");
  std::string line;
  while (std::getline(stream, line)) {
    if (line.rfind("PRETTY_NAME=", 0) != 0) continue;
    std::string value = line.substr(12);
    if (value.size() >= 2 && value.front() == '"' && value.back() == '"') {
      value = value.substr(1, value.size() - 2);
    }
    return value;
  }
  return read_first_line("/etc/issue");
}

bool read_memory(long long* total_mb, long long* available_mb) {
  if (total_mb == nullptr || available_mb == nullptr) return false;
  std::ifstream stream("/proc/meminfo");
  std::string key;
  long long value = 0;
  std::string unit;
  bool have_total = false;
  bool have_available = false;
  while (stream >> key >> value) {
    std::getline(stream, unit);
    if (key == "MemTotal:") {
      *total_mb = value / 1024;
      have_total = true;
    } else if (key == "MemAvailable:") {
      *available_mb = value / 1024;
      have_available = true;
    }
  }
  return have_total && have_available && *total_mb > 0;
}

bool read_load_average(double* load1) {
  if (load1 == nullptr) return false;
  const std::string line = read_first_line("/proc/loadavg");
  if (line.empty()) return false;
  std::istringstream stream(line);
  return static_cast<bool>(stream >> *load1);
}

bool read_uptime(double* seconds) {
  if (seconds == nullptr) return false;
  const std::string line = read_first_line("/proc/uptime");
  if (line.empty()) return false;
  std::istringstream stream(line);
  return static_cast<bool>(stream >> *seconds);
}

bool read_process_working_set(long long* working_set_mb) {
  if (working_set_mb == nullptr) return false;
  std::ifstream stream("/proc/self/status");
  std::string key;
  while (stream >> key) {
    if (key == "VmRSS:") {
      long long kb = 0;
      stream >> kb;
      *working_set_mb = kb / 1024;
      return *working_set_mb > 0;
    }
    std::string rest;
    std::getline(stream, rest);
  }
  return false;
}

bool read_disk(const std::string& path,
               long long* total_bytes,
               long long* free_bytes) {
  if (total_bytes == nullptr || free_bytes == nullptr) return false;
  const std::string target = path.empty() ? "/" : path;
  struct statvfs info {};
  if (statvfs(target.c_str(), &info) != 0) return false;
  const auto block_size = static_cast<long long>(info.f_frsize);
  if (block_size <= 0) return false;
  *total_bytes = static_cast<long long>(info.f_blocks) * block_size;
  *free_bytes = static_cast<long long>(info.f_bavail) * block_size;
  return *total_bytes > 0;
}

}  // namespace

CpuTimes read_cpu_times() {
  std::ifstream stream("/proc/stat");
  std::string line;
  while (std::getline(stream, line)) {
    if (line.rfind("cpu ", 0) != 0) continue;
    std::istringstream values(line.substr(4));
    unsigned long long ticks[8] = {0, 0, 0, 0, 0, 0, 0, 0};
    int count = 0;
    while (count < 8 && (values >> ticks[count])) {
      ++count;
    }
    if (count < 4) return {};

    CpuTimes result;
    result.valid = true;
    // user nice system idle iowait irq softirq steal
    result.idle = ticks[3] + (count > 4 ? ticks[4] : 0);
    for (int index = 0; index < count; ++index) {
      result.total += ticks[index];
    }
    return result;
  }
  return {};
}

SystemStatus sample_system_status(const std::string& disk_path) {
  SystemStatus status;
  status.platform = platform_name();
  status.os = read_os_name();

  const unsigned int cores = std::thread::hardware_concurrency();
  status.cores = cores > 0 ? static_cast<int>(cores) : 1;

  double load = 0.0;
  if (read_load_average(&load)) {
    status.load_known = true;
    status.load1 = load;
  }

  long long total_mb = 0;
  long long available_mb = 0;
  if (read_memory(&total_mb, &available_mb)) {
    status.memory_known = true;
    status.memory_total_mb = total_mb;
    status.memory_available_mb = available_mb;
  }

  long long total_bytes = 0;
  long long free_bytes = 0;
  if (read_disk(disk_path, &total_bytes, &free_bytes)) {
    status.disk_known = true;
    status.disk_path = disk_path.empty() ? "/" : disk_path;
    status.disk_total_bytes = total_bytes;
    status.disk_free_bytes = free_bytes;
  }

  long long working_set_mb = 0;
  if (read_process_working_set(&working_set_mb)) {
    status.process_known = true;
    status.process_working_set_mb = working_set_mb;
  }

  double uptime = 0.0;
  if (read_uptime(&uptime)) {
    status.uptime_known = true;
    status.system_uptime_seconds = uptime;
  }

  return status;
}

}  // namespace archoera
