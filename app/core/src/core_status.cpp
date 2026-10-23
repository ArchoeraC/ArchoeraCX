/*
 * Machine load sampling and the status watch (system.status*).
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "core_status.h"

#include "core_internal.h"
#include "core_json.h"
#include "core_result.h"
#include "core_tasks.h"
#include "platform_status.h"
#include "platform_system.h"

#include <algorithm>
#include <cstdio>
#include <string>
#include <thread>
#include <utility>

namespace archoera {
namespace {

void append_number_field(std::string& json,
                         const char* name,
                         const double value) {
  char buffer[64];
  std::snprintf(buffer, sizeof(buffer), "%.1f", value);
  json.append("\"");
  json.append(name);
  json.append("\":");
  json.append(buffer);
}

void append_int_field(std::string& json, const char* name, const long long value) {
  json.append("\"");
  json.append(name);
  json.append("\":");
  json.append(std::to_string(value));
}

std::string make_status_result(const SystemStatus& status,
                               const bool cpu_known,
                               const double cpu_percent,
                               const double launcher_uptime) {
  std::string json = "{\"ok\":true,\"data\":{";
  append_status_fields(json, status, cpu_known, cpu_percent, launcher_uptime);
  json.append("},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

// Publishes one sample as a `system.status` domain event. Never throws and
// never calls wake under the core lock.
void publish_status(archoera_core* core,
                    const SystemStatus& status,
                    const bool cpu_known,
                    const double cpu_percent) noexcept {
  try {
    std::string json = "{\"schemaVersion\":1,\"kind\":\"system.status\",";
    append_status_fields(json, status, cpu_known, cpu_percent,
                         launcher_uptime_seconds(*core));
    json.append("}");
    publish_domain_event(core, std::move(json));
  } catch (...) {
    // A missed sample must never escape the sampler thread.
  }
}

struct StatusCallPayload {
  std::string disk_path;
};

// One-shot sample worker. Like the watcher it derives CPU usage from two
// counter reads (~140 ms apart) and settles the task with the full payload.
void status_worker(archoera_core* core,
                   const uint64_t task_id,
                   StatusCallPayload* payload) noexcept {
  std::unique_ptr<StatusCallPayload> held(payload);
  try {
    const auto first = read_cpu_times();
    std::this_thread::sleep_for(std::chrono::milliseconds(140));
    const auto second = read_cpu_times();
    double percent = 0.0;
    const bool cpu_known = cpu_usage_percent(first, second, &percent);
    const std::string result = make_status_result(
        sample_system_status(held->disk_path), cpu_known, percent,
        launcher_uptime_seconds(*core));

    WakeTarget wake;
    bool notify = false;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        return;
      }

      auto it = core->tasks.find(task_id);
      if (it == core->tasks.end() || is_terminal(it->second.state)) {
        return;
      }

      TaskRecord& task = it->second;
      if (task.cancel_requested) {
        settle_task_locked(*core, task, ARCHOERA_TASK_CANCELLED, std::string(),
                           0, nullptr);
      } else {
        settle_task_locked(*core, task, ARCHOERA_TASK_COMPLETED, std::move(result),
                           0, nullptr);
      }

      wake = wake_target_locked(*core);
      notify = true;
    }
    if (notify) {
      notify_wake(wake);
    }
  } catch (...) {
    fail_async_task(core, task_id);
  }
}

void status_watch_loop(archoera_core* core, StatusWatch* watch) noexcept {
  try {
    // First window: a short pair of samples so the first push already carries
    // CPU usage instead of a gap.
    const auto first = read_cpu_times();
    {
      std::unique_lock<std::mutex> lock(watch->mutex);
      if (watch->cv.wait_for(lock, std::chrono::milliseconds(120),
                             [watch] { return watch->stopping; })) {
        return;
      }
    }

    auto previous = read_cpu_times();
    double percent = 0.0;
    const bool cpu_known = cpu_usage_percent(first, previous, &percent);
    publish_status(core, sample_system_status(watch->disk_path),
                   cpu_known, percent);

    std::unique_lock<std::mutex> lock(watch->mutex);
    while (!watch->stopping) {
      if (watch->cv.wait_for(lock, watch->interval,
                             [watch] { return watch->stopping; })) {
        break;
      }
      const std::string path = watch->disk_path;
      lock.unlock();

      const auto current = read_cpu_times();
      double value = 0.0;
      const bool known = cpu_usage_percent(previous, current, &value);
      previous = current;
      publish_status(core, sample_system_status(path), known, value);

      lock.lock();
    }
  } catch (...) {
    // Stop pushing; Dart keeps the last sample and the manual refresh.
  }
}

}  // namespace

double launcher_uptime_seconds(const archoera_core& core) {
  const auto elapsed = std::chrono::steady_clock::now() - core.started_at;
  return std::chrono::duration<double>(elapsed).count();
}

void append_status_fields(std::string& json,
                          const SystemStatus& status,
                          const bool cpu_known,
                          const double cpu_percent,
                          const double launcher_uptime_seconds_value) {
  json.append("\"platform\":\"");
  append_json_escaped(json, status.platform);
  json.append("\",\"os\":\"");
  append_json_escaped(json, status.os);
  json.append("\",");
  append_int_field(json, "cores", status.cores > 0 ? status.cores : 1);
  json.append(",");

  json.append("\"cpu\":{");
  if (cpu_known) {
    append_number_field(json, "usagePercent", cpu_percent);
  } else {
    json.append("\"usagePercent\":null");
  }
  json.append(",");
  if (status.load_known) {
    append_number_field(json, "loadAverage1m", status.load1);
  } else {
    json.append("\"loadAverage1m\":null");
  }
  json.append("},");

  json.append("\"memory\":{");
  if (status.memory_known && status.memory_total_mb > 0) {
    const long long used = std::max<long long>(
        0, status.memory_total_mb - status.memory_available_mb);
    append_int_field(json, "totalMb", status.memory_total_mb);
    json.append(",");
    append_int_field(json, "availableMb", status.memory_available_mb);
    json.append(",");
    append_int_field(json, "usedMb", used);
    json.append(",");
    append_number_field(
        json, "usagePercent",
        static_cast<double>(used) * 100.0 /
            static_cast<double>(status.memory_total_mb));
  } else {
    json.append(
        "\"totalMb\":null,\"availableMb\":null,\"usedMb\":null,"
        "\"usagePercent\":null");
  }
  json.append("},");

  json.append("\"disk\":{");
  if (status.disk_known && status.disk_total_bytes > 0) {
    json.append("\"path\":\"");
    append_json_escaped(json, status.disk_path);
    json.append("\",");
    append_int_field(json, "totalBytes", status.disk_total_bytes);
    json.append(",");
    append_int_field(json, "freeBytes", status.disk_free_bytes);
    json.append(",");
    const long long used =
        std::max<long long>(0, status.disk_total_bytes - status.disk_free_bytes);
    append_number_field(
        json, "usagePercent",
        static_cast<double>(used) * 100.0 /
            static_cast<double>(status.disk_total_bytes));
  } else {
    json.append(
        "\"path\":null,\"totalBytes\":null,\"freeBytes\":null,"
        "\"usagePercent\":null");
  }
  json.append("},");
  json.append("\"process\":{");
  if (status.process_known) {
    append_int_field(json, "workingSetMb", status.process_working_set_mb);
  } else {
    json.append("\"workingSetMb\":null");
  }
  json.append(",\"managedMb\":null,");
  append_number_field(json, "uptimeSeconds", launcher_uptime_seconds_value);
  json.append("},");

  json.append("\"system\":{");
  if (status.uptime_known) {
    append_number_field(json, "uptimeSeconds", status.system_uptime_seconds);
  } else {
    json.append("\"uptimeSeconds\":null");
  }
  json.append("}");
}

bool start_status_watch(archoera_core& core,
                        const std::chrono::milliseconds interval,
                        std::string disk_path) noexcept {
  try {
    std::lock_guard<std::mutex> lock(core.mutex);
    if (core.shutting_down.load(std::memory_order_acquire)) {
      return false;
    }

    if (core.status_watch != nullptr) {
      std::lock_guard<std::mutex> watch_lock(core.status_watch->mutex);
      core.status_watch->interval = interval;
      core.status_watch->disk_path = std::move(disk_path);
      core.status_watch->cv.notify_all();
      return true;
    }

    auto watch = std::make_unique<StatusWatch>();
    watch->interval = interval;
    watch->disk_path = std::move(disk_path);
    watch->thread = std::thread(status_watch_loop, &core, watch.get());
    core.status_watch = std::move(watch);
    return true;
  } catch (...) {
    return false;
  }
}

void stop_status_watch(archoera_core& core) noexcept {
  try {
    std::thread worker;
    {
      std::lock_guard<std::mutex> lock(core.mutex);
      if (core.status_watch == nullptr) {
        return;
      }
      {
        std::lock_guard<std::mutex> watch_lock(core.status_watch->mutex);
        core.status_watch->stopping = true;
      }
      core.status_watch->cv.notify_all();

      // Another thread already moved the thread out and is joining it; leave
      // the object alone so its sampler cannot observe a freed watch.
      if (!core.status_watch->thread.joinable()) {
        return;
      }
      worker = std::move(core.status_watch->thread);
    }

    // Join without holding core.mutex: the loop publishes through it.
    if (worker.joinable()) {
      worker.join();
    }

    std::lock_guard<std::mutex> lock(core.mutex);
    core.status_watch.reset();
  } catch (...) {
    // Shutdown must stay idempotent and exception-free.
  }
}

// ---------------------------------------------------------------------------
// Method handlers
// ---------------------------------------------------------------------------

archoera_status handle_system_status(archoera_core& core,
                                     const MethodRoute& route,
                                     const std::string_view params,
                                     std::string&,
                                     uint64_t* out_task_id,
                                     archoera_error* err) {
  std::string path;
  extract_json_string(params, "path", &path);
  if (!path.empty() && !is_safe_directory_path(path)) {
    set_error(err, ARCHOERA_E_INVALID, kMsgStatusPathInvalid);
    return ARCHOERA_E_INVALID;
  }

  TaskRecord record;
  record.method = std::string(route.name);
  return spawn_async_task<StatusCallPayload>(
      &core, std::move(record), &status_worker,
      StatusCallPayload{std::move(path)}, out_task_id, err);
}

archoera_status handle_system_status_watch(archoera_core& core,
                                           const MethodRoute&,
                                           const std::string_view params,
                                           std::string& result,
                                           uint64_t*,
                                           archoera_error* err) {
  long long interval = 5000;
  extract_json_int(params, "intervalMs", &interval);
  if (interval < 1000) interval = 1000;
  if (interval > 60000) interval = 60000;

  std::string path;
  extract_json_string(params, "path", &path);
  if (!path.empty() && !is_safe_directory_path(path)) {
    set_error(err, ARCHOERA_E_INVALID, kMsgStatusPathInvalid);
    return ARCHOERA_E_INVALID;
  }

  if (!start_status_watch(core, std::chrono::milliseconds(interval),
                          std::move(path))) {
    set_error(err, ARCHOERA_E_INVALID, kMsgShuttingDown);
    return ARCHOERA_E_INVALID;
  }

  result = "{\"ok\":true,\"data\":{\"watching\":true,"
           "\"intervalMs\":" +
           std::to_string(interval) +
           "},\"meta\":{\"schemaVersion\":1}}";
  return ARCHOERA_OK;
}

archoera_status handle_system_status_unwatch(archoera_core& core,
                                             const MethodRoute&,
                                             std::string_view,
                                             std::string& result,
                                             uint64_t*,
                                             archoera_error*) {
  stop_status_watch(core);
  result =
      "{\"ok\":true,\"data\":{\"watching\":false},"
      "\"meta\":{\"schemaVersion\":1}}";
  return ARCHOERA_OK;
}

}  // namespace archoera
