/*
 * Machine load sampling and the status watch (system.status*).
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * One-shot status runs on a core worker thread; the push watcher keeps a
 * single sampler thread waiting on its condition variable (no busy polling)
 * and publishes one domain event per interval. Handlers are registered in
 * method_registry.cpp.
 */

#pragma once

#include <chrono>
#include <condition_variable>
#include <mutex>
#include <string>
#include <thread>

#include "method_registry.h"
#include "platform_status.h"

struct archoera_core;

namespace archoera {

// Native sampler owned by system.status.watch. A single thread waits on a
// condition variable for the next interval (no busy polling) and pushes one
// domain event per tick.
struct StatusWatch {
  std::mutex mutex;
  std::condition_variable cv;
  std::thread thread;
  bool stopping = false;
  std::chrono::milliseconds interval{5000};
  std::string disk_path;
};

archoera_status handle_system_status(archoera_core& core,
                                     const MethodRoute& route,
                                     std::string_view params,
                                     std::string& result,
                                     uint64_t* out_task_id,
                                     archoera_error* err);
archoera_status handle_system_status_watch(archoera_core& core,
                                           const MethodRoute& route,
                                           std::string_view params,
                                           std::string& result,
                                           uint64_t* out_task_id,
                                           archoera_error* err);
archoera_status handle_system_status_unwatch(archoera_core& core,
                                             const MethodRoute& route,
                                             std::string_view params,
                                             std::string& result,
                                             uint64_t* out_task_id,
                                             archoera_error* err);

/* Watch lifecycle, called directly by archoera_shutdown. */
bool start_status_watch(archoera_core& core,
                        std::chrono::milliseconds interval,
                        std::string disk_path) noexcept;

void stop_status_watch(archoera_core& core) noexcept;

/* Builds the shared payload of the one-shot result and the pushed event. The
 * caller adds the `ok`/`data` or `kind` wrapper. */
void append_status_fields(std::string& json,
                          const SystemStatus& status,
                          bool cpu_known,
                          double cpu_percent,
                          double launcher_uptime_seconds);

/* Launcher process uptime row; steady clock never jumps. */
double launcher_uptime_seconds(const archoera_core& core);

}  // namespace archoera
