/*
 * ArchoeraCX native core ABI v1.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * This translation unit owns the C boundary declared in
 * contracts/abi/archoera_abi.h: only lifecycle, poll/cancel/result plumbing
 * and the routing-table dispatch. Method handlers live in the core_*.cpp
 * files and are registered in method_registry.cpp.
 */

#include "core_internal.h"
#include "core_status.h"
#include "core_tasks.h"

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <deque>
#include <iterator>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_set>
#include <utility>
#include <vector>

namespace {

constexpr int32_t kMaxPollBatch = 64;

// Shutdown is documented as idempotent. Tracking live handles lets a repeated
// shutdown (or a shutdown of an already-freed handle) become a no-op instead of
// a double delete. Callers must still not use a handle after shutdown.
std::mutex g_live_mutex;
std::unordered_set<archoera_core*> g_live_cores;

}  // namespace

extern "C" {

ARCHOERA_API uint32_t archoera_abi_version(void) {
  return ARCHOERA_ABI_VERSION;
}

ARCHOERA_API const char* archoera_build_info(void) {
  return archoera::kBuildInfoJson;
}

ARCHOERA_API archoera_status archoera_init(const char* config_json,
                                           archoera_core** out,
                                           archoera_error* err) {
  if (out == nullptr) {
    archoera::set_error(err, ARCHOERA_E_INVALID, archoera::kMsgNullOutput,
                        "archoera_init requires a non-null out handle");
    return ARCHOERA_E_INVALID;
  }
  *out = nullptr;

  try {
    std::unique_ptr<archoera_core> core(new archoera_core());
    if (config_json != nullptr) {
      core->config_json = config_json;
    }

    // A missing or incompatible business module is not fatal in this slice:
    // module-backed methods then report E_UNSUPPORTED. The host is always
    // constructed so shutdown has a single code path.
    core->module_registry = std::make_unique<archoera::ModuleRegistry>();
    core->module_registry->load();

    {
      std::lock_guard<std::mutex> lock(g_live_mutex);
      g_live_cores.insert(core.get());
    }

    archoera::clear_error(err);
    *out = core.release();
    return ARCHOERA_OK;
  } catch (const std::bad_alloc&) {
    archoera::set_error(err, ARCHOERA_E_INTERNAL, archoera::kMsgOutOfMemory);
    return ARCHOERA_E_INTERNAL;
  } catch (...) {
    archoera::set_error(err, ARCHOERA_E_INTERNAL, archoera::kMsgInternal);
    return ARCHOERA_E_INTERNAL;
  }
}

ARCHOERA_API void archoera_shutdown(archoera_core* core) {
  if (core == nullptr) {
    return;
  }

  try {
    {
      std::lock_guard<std::mutex> lock(g_live_mutex);
      auto it = g_live_cores.find(core);
      if (it == g_live_cores.end()) {
        return;
      }
      g_live_cores.erase(it);
    }

    // Stop all workers before touching the core again. The flag is set under
    // core.mutex (the same lock a worker takes before publishing anything),
    // then the condition variable wakes workers that are pacing. Threads are
    // joined, never detached, so the core outlives every worker access.
    std::vector<std::unique_ptr<archoera::WorkerSlot>> workers;
    std::vector<std::pair<std::string, uint64_t>> module_tasks;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      core->shutting_down.store(true, std::memory_order_release);
      for (auto& entry : core->tasks) {
        archoera::TaskRecord& task = entry.second;
        if (task.async && !archoera::is_terminal(task.state)) {
          task.cancel_requested = true;
          if (task.module_backed) {
            module_tasks.emplace_back(task.module, task.id);
          }
        }
      }
      workers.swap(core->workers);
    }
    core->worker_cv.notify_all();

    // Ask the module to settle in-flight calls. done callbacks that arrive
    // here observe shutting_down and only release their context.
    if (core->module_registry != nullptr) {
      for (const auto& [module, task_id] : module_tasks) {
        core->module_registry->cancel(module, task_id);
      }
    }

    for (auto& slot : workers) {
      if (slot != nullptr && slot->thread.joinable()) {
        slot->thread.join();
      }
    }

    // The appearance watcher may call publish_appearance from an OS thread;
    // stop() joins it before the core and its event queue are destroyed.
    if (core->appearance != nullptr) {
      core->appearance->stop();
    }

    // The status sampler pushes domain events; join it before the queue dies.
    archoera::stop_status_watch(*core);

    // Module contract: shutdown blocks until callbacks stop, so no callback
    // can touch this core after this call. Only then may the core die.
    if (core->module_registry != nullptr) {
      core->module_registry->shutdown();
    }
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      core->module_calls.clear();
    }

    delete core;
  } catch (...) {
    // Never let C++ exceptions escape the ABI.
  }
}

ARCHOERA_API archoera_status archoera_set_wakeup(archoera_core* core,
                                                 archoera_wake_fn fn,
                                                 void* user) {
  if (core == nullptr) {
    return ARCHOERA_E_INVALID;
  }

  try {
    std::lock_guard<std::mutex> lock(core->mutex);
    core->wake_fn = fn;
    core->wake_user = user;
    return ARCHOERA_OK;
  } catch (...) {
    return ARCHOERA_E_INTERNAL;
  }
}

ARCHOERA_API int32_t archoera_poll(archoera_core* core,
                                   archoera_event* out,
                                   const int32_t max) {
  if (core == nullptr || out == nullptr || max <= 0) {
    return -1;
  }

  // Events taken out of the shared queue for this poll. If building the
  // payload storage fails, the undelivered events go back at the front of the
  // queue in their original order.
  std::deque<archoera::EventRecord> batch;
  std::deque<std::string> payloads;

  try {
    int32_t count = 0;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      archoera::purge_expired_tasks_locked(*core);

      // Any pointers returned by the previous poll become invalid here.
      core->poll_payloads.clear();

      const int32_t limit = std::min<int32_t>(max, kMaxPollBatch);
      while (count < limit && !core->events.empty()) {
        batch.push_back(std::move(core->events.front()));
        core->events.pop_front();
        ++count;
      }
    }

    // Moving the JSON into the payload deque is the only allocating step left;
    // do it without the lock so a slow allocator cannot stall producers.
    for (archoera::EventRecord& event : batch) {
      payloads.push_back(std::move(event.json));
    }

    {
      std::lock_guard<std::mutex> lock(core->mutex);
      core->poll_payloads.swap(payloads);
      // `payloads` now owns the previous poll's strings; it is destroyed after
      // the lock is released.

      for (int32_t index = 0; index < count; ++index) {
        const archoera::EventRecord& event = batch[static_cast<size_t>(index)];
        const std::string& payload =
            core->poll_payloads[static_cast<size_t>(index)];

        archoera_event& dst = out[index];
        dst.type = event.type;
        dst.state = event.state;
        dst.code = event.code;
        dst.flags = event.flags;
        dst.seq = event.seq;
        dst.task_id = event.task_id;
        dst.done = event.done;
        dst.total = event.total;
        dst.bytes_per_sec = event.bytes_per_sec;
        dst.json = payload.empty() ? nullptr : payload.c_str();
      }
    }

    return count;
  } catch (...) {
    // Put the payloads we already moved back into their events, then restore
    // the batch at the front of the queue. Sequence numbers are assigned at
    // enqueue time, so a failed poll never consumes one.
    std::size_t index = 0;
    for (std::string& payload : payloads) {
      if (index < batch.size()) {
        batch[index].json = std::move(payload);
      }
      ++index;
    }

    try {
      std::lock_guard<std::mutex> lock(core->mutex);
      core->events.insert(core->events.begin(),
                          std::make_move_iterator(batch.begin()),
                          std::make_move_iterator(batch.end()));
    } catch (...) {
      // Restoring allocates too and can fail under the same memory pressure.
      // There is no safe recovery left; drop the batch rather than leave the
      // queue partially reordered.
    }
    return -1;
  }
}

ARCHOERA_API archoera_status archoera_call(archoera_core* core,
                                           const char* method,
                                           const char* params_json,
                                           uint64_t* out_task_id,
                                           archoera_error* err) {
  if (out_task_id != nullptr) {
    *out_task_id = 0;
  }

  if (core == nullptr) {
    archoera::set_error(err, ARCHOERA_E_INVALID, archoera::kMsgNullCore);
    return ARCHOERA_E_INVALID;
  }
  if (method == nullptr || method[0] == '\0') {
    archoera::set_error(err, ARCHOERA_E_INVALID, archoera::kMsgInvalidMethod);
    return ARCHOERA_E_INVALID;
  }
  if (out_task_id == nullptr) {
    archoera::set_error(err, ARCHOERA_E_INVALID, archoera::kMsgNullOutput,
                        "archoera_call requires a non-null task id output");
    return ARCHOERA_E_INVALID;
  }

  const char* raw_params = params_json != nullptr ? params_json : "{}";

  try {
    // Opportunistic TTL sweep: a caller that never releases a terminal task
    // must not grow the task map for the process lifetime.
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      archoera::purge_expired_tasks_locked(*core);
    }

    const std::string method_name(method);
    const archoera::MethodRoute* route =
        archoera::find_method_route(method_name);
    if (route == nullptr) {
      archoera::set_error(err, ARCHOERA_E_UNSUPPORTED,
                          archoera::kMsgUnknownMethod);
      return ARCHOERA_E_UNSUPPORTED;
    }

    // One table lookup, one dispatch call: the row carries its handler.
    // Sync handlers fill `result` (completed below); async handlers start
    // their task and write *out_task_id.
    std::string result;
    const archoera_status status =
        route->handler(*core, *route, raw_params, result, out_task_id, err);
    if (status != ARCHOERA_OK) {
      return status;
    }
    if (*out_task_id != 0) {
      return ARCHOERA_OK;
    }

    return archoera::complete_sync_call(core, route->name, std::move(result),
                                        out_task_id, err);
  } catch (const std::bad_alloc&) {
    archoera::set_error(err, ARCHOERA_E_INTERNAL, archoera::kMsgOutOfMemory);
    return ARCHOERA_E_INTERNAL;
  } catch (...) {
    archoera::set_error(err, ARCHOERA_E_INTERNAL, archoera::kMsgInternal);
    return ARCHOERA_E_INTERNAL;
  }
}

ARCHOERA_API archoera_status archoera_cancel(archoera_core* core,
                                             const uint64_t task_id) {
  if (core == nullptr) {
    return ARCHOERA_E_INVALID;
  }
  if (task_id == 0) {
    return ARCHOERA_E_NOT_FOUND;
  }

  try {
    archoera::WakeTarget wake;
    bool notify_wake_now = false;
    bool notify_worker = false;
    bool module_task = false;
    std::string module;

    {
      std::lock_guard<std::mutex> lock(core->mutex);

      auto it = core->tasks.find(task_id);
      if (it == core->tasks.end()) {
        return ARCHOERA_E_NOT_FOUND;
      }

      archoera::TaskRecord& task = it->second;
      if (archoera::is_terminal(task.state)) {
        return ARCHOERA_OK;
      }

      if (task.async) {
        // Cooperative cancellation: the worker/module owns the terminal
        // transition and the CANCELLED event, so cancel never publishes a
        // state the owner might later contradict.
        module_task = task.module_backed;
        module = task.module;
        if (!task.cancel_requested) {
          task.cancel_requested = true;
          notify_worker = true;
        }
      } else {
        archoera::settle_task_locked(
            *core, task, ARCHOERA_TASK_CANCELLED, std::string(), 0, nullptr);

        wake = archoera::wake_target_locked(*core);
        notify_wake_now = true;
      }
    }

    if (notify_worker) {
      core->worker_cv.notify_all();
    }
    // Module calls must never run while core.mutex is held. The module host
    // serializes this with in-flight call/shutdown internally.
    if (module_task && core->module_registry != nullptr) {
      core->module_registry->cancel(module, task_id);
    }
    if (notify_wake_now) {
      archoera::notify_wake(wake);
    }
    return ARCHOERA_OK;
  } catch (...) {
    return ARCHOERA_E_INTERNAL;
  }
}

ARCHOERA_API archoera_status archoera_result(archoera_core* core,
                                             const uint64_t task_id,
                                             char* buf,
                                             const size_t buf_len,
                                             size_t* out_len,
                                             archoera_error* err) {
  if (out_len == nullptr) {
    archoera::set_error(err, ARCHOERA_E_INVALID, archoera::kMsgNullOutput,
                        "archoera_result requires a non-null length output");
    return ARCHOERA_E_INVALID;
  }
  *out_len = 0;

  if (core == nullptr) {
    archoera::set_error(err, ARCHOERA_E_INVALID, archoera::kMsgNullCore);
    return ARCHOERA_E_INVALID;
  }
  if (task_id == 0) {
    archoera::set_error(err, ARCHOERA_E_NOT_FOUND, archoera::kMsgTaskNotFound);
    return ARCHOERA_E_NOT_FOUND;
  }

  try {
    std::lock_guard<std::mutex> lock(core->mutex);

    auto it = core->tasks.find(task_id);
    if (it == core->tasks.end()) {
      archoera::set_error(err, ARCHOERA_E_NOT_FOUND,
                          archoera::kMsgTaskNotFound);
      return ARCHOERA_E_NOT_FOUND;
    }

    const archoera::TaskRecord& task = it->second;
    if (!archoera::is_terminal(task.state)) {
      archoera::set_error(err, ARCHOERA_E_BUSY, archoera::kMsgTaskBusy);
      return ARCHOERA_E_BUSY;
    }

    const size_t required = task.result.size() + 1;
    *out_len = required;

    // Two-phase read: the caller asks for the required size first.
    if (buf == nullptr || buf_len == 0) {
      archoera::clear_error(err);
      return ARCHOERA_OK;
    }

    if (buf_len < required) {
      archoera::set_error(err, ARCHOERA_E_BUFFER, archoera::kMsgBufferTooSmall);
      return ARCHOERA_E_BUFFER;
    }

    if (!task.result.empty()) {
      std::memcpy(buf, task.result.data(), task.result.size());
    }
    buf[task.result.size()] = '\0';

    archoera::clear_error(err);
    return ARCHOERA_OK;
  } catch (...) {
    archoera::set_error(err, ARCHOERA_E_INTERNAL, archoera::kMsgInternal);
    return ARCHOERA_E_INTERNAL;
  }
}

ARCHOERA_API void archoera_release(archoera_core* core,
                                   const uint64_t task_id) {
  if (core == nullptr) {
    return;
  }

  try {
    std::lock_guard<std::mutex> lock(core->mutex);
    archoera::purge_expired_tasks_locked(*core);

    const auto it = core->tasks.find(task_id);
    if (it == core->tasks.end()) {
      return;
    }

    // Only terminal tasks may be dropped. Releasing a running task would
    // detach it from cancel and from its module done callback; the core keeps
    // it alive and the TTL sweep reclaims it once it settles.
    if (!archoera::is_terminal(it->second.state)) {
      return;
    }

    core->tasks.erase(it);
  } catch (...) {
    // Never let C++ exceptions escape the ABI.
  }
}

}  // extern "C"
