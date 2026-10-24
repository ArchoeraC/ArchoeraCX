/*
 * Task bookkeeping shared by every core-owned worker.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "core_tasks.h"

#include "core_json.h"
#include "core_result.h"

#include <chrono>
#include <string>

namespace archoera {
namespace {

/* system.pingAsync is a deliberately small probe task used to exercise the
 * wake path end to end. It is not business logic: it only moves through the
 * task state machine and emits observable progress. */
constexpr const char* kPingAsyncMethod = "system.pingAsync";
constexpr const char* kPingAsyncProgressJson = "{\"phase\":\"probe\"}";
constexpr int64_t kPingAsyncTotal = 3;
constexpr auto kPingAsyncStepDelay = std::chrono::milliseconds(25);

struct PingAsyncPayload {};

}  // namespace

void settle_task_locked(archoera_core& core,
                        TaskRecord& task,
                        const archoera_task_state state,
                        std::string result,
                        const int32_t event_code,
                        const char* message) {
  if (is_terminal(task.state)) {
    return;
  }

  const char* phase = nullptr;
  const char* note = nullptr;
  switch (state) {
    case ARCHOERA_TASK_COMPLETED:
      phase = "completed";
      note = message != nullptr ? message : "task completed";
      break;
    case ARCHOERA_TASK_FAILED:
      phase = "failed";
      note = message != nullptr ? message : "task failed";
      break;
    case ARCHOERA_TASK_CANCELLED:
      phase = "cancelled";
      note = message != nullptr ? message : "task cancelled";
      break;
    default:
      phase = message != nullptr ? message : "settled";
      note = phase;
      break;
  }

  EventRecord event;
  event.type = ARCHOERA_EV_TASK_STATE;
  event.task_id = task.id;
  event.state = state;
  event.code = event_code;
  task.result = std::move(result);
  task.state = state;
  task.settled_at = std::chrono::steady_clock::now();
  event.json = make_state_json(task.method, phase, note);
  enqueue_event_locked(core, std::move(event));
}

void settle_task_terminal(archoera_core* core,
                          const uint64_t task_id,
                          const archoera_task_state state,
                          std::string result,
                          const int32_t event_code,
                          const char* message) noexcept {
  try {
    WakeTarget wake;
    bool notify = false;

    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        return;
      }

      auto it = core->tasks.find(task_id);
      if (it == core->tasks.end()) {
        return;
      }

      if (is_terminal(it->second.state)) {
        return;
      }

      settle_task_locked(*core, it->second, state, std::move(result),
                         event_code, message);
      wake = wake_target_locked(*core);
      notify = true;
    }

    if (notify) {
      notify_wake(wake);
    }
  } catch (...) {
    // If even the failure event cannot be allocated, at least the task is no
    // longer RUNNING; shutdown will still join this worker.
  }
}

void fail_async_task(archoera_core* core, const uint64_t task_id) noexcept {
  settle_task_terminal(core, task_id, ARCHOERA_TASK_FAILED, std::string(), 0,
                       "internal worker error");
}

bool async_task_should_stop_locked(const archoera_core& core,
                                   const uint64_t task_id) {
  auto it = core.tasks.find(task_id);
  if (it == core.tasks.end()) {
    return true;
  }
  return it->second.cancel_requested;
}

void publish_domain_event(archoera_core* core, std::string json) noexcept {
  try {
    WakeTarget wake;
    bool notify = false;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        return;
      }

      EventRecord event;
      event.type = ARCHOERA_EV_DOMAIN;
      event.task_id = 0;
      event.json = std::move(json);
      enqueue_event_locked(*core, std::move(event));
      wake = wake_target_locked(*core);
      notify = true;
    }
    if (notify) {
      notify_wake(wake);
    }
  } catch (...) {
    // Domain notifications are cosmetic; a failure must not escape.
  }
}

archoera_status complete_sync_call(archoera_core* core,
                                   const std::string_view method,
                                   std::string result,
                                   uint64_t* out_task_id,
                                   archoera_error* err) {
  try {
    WakeTarget wake;
    uint64_t task_id = 0;

    {
      std::lock_guard<std::mutex> lock(core->mutex);

      task_id = core->next_task_id++;
      TaskRecord task;
      task.id = task_id;
      task.method = method;
      task.state = ARCHOERA_TASK_COMPLETED;
      task.settled_at = std::chrono::steady_clock::now();
      task.result = std::move(result);

      auto inserted = core->tasks.emplace(task_id, std::move(task));
      try {
        EventRecord event;
        event.type = ARCHOERA_EV_TASK_STATE;
        event.state = ARCHOERA_TASK_COMPLETED;
        event.task_id = task_id;
        event.json = make_state_json(task.method, "completed", "task completed");
        enqueue_event_locked(*core, std::move(event));
      } catch (...) {
        core->tasks.erase(inserted.first);
        throw;
      }

      wake = wake_target_locked(*core);
    }

    *out_task_id = task_id;
    clear_error(err);
    notify_wake(wake);
    return ARCHOERA_OK;
  } catch (const std::bad_alloc&) {
    set_error(err, ARCHOERA_E_INTERNAL, kMsgOutOfMemory);
    return ARCHOERA_E_INTERNAL;
  } catch (...) {
    set_error(err, ARCHOERA_E_INTERNAL, kMsgInternal);
    return ARCHOERA_E_INTERNAL;
  }
}

// ---------------------------------------------------------------------------
// system.pingAsync probe worker
// ---------------------------------------------------------------------------

// Worker entry point. Runs on a core-owned thread, emits progress while the
// caller polls, then performs the terminal transition. Events are always
// enqueued under core.mutex, but the wake callback is always invoked after
// that mutex has been released.
void ping_async_worker(archoera_core* core,
                       const uint64_t task_id,
                       PingAsyncPayload*) noexcept {
  try {
    for (int64_t step = 1; step <= kPingAsyncTotal; ++step) {
      WakeTarget wake;
      bool notify = false;
      bool cancelled = false;
      bool stop = false;

      {
        std::unique_lock<std::mutex> lock(core->mutex);

        // Pacing is observable (25 ms between events) but the wait is
        // condition-variable driven, so cancel/shutdown wake it immediately
        // instead of making the caller wait out a sleep.
        core->worker_cv.wait_for(lock, kPingAsyncStepDelay, [&] {
          return core->shutting_down.load(std::memory_order_acquire) ||
                 async_task_should_stop_locked(*core, task_id);
        });

        if (core->shutting_down.load(std::memory_order_acquire)) {
          stop = true;
        } else {
          auto it = core->tasks.find(task_id);
          if (it == core->tasks.end()) {
            stop = true;
          } else if (it->second.cancel_requested) {
            cancelled = true;
            if (!is_terminal(it->second.state)) {
              settle_task_locked(*core, it->second, ARCHOERA_TASK_CANCELLED,
                                 std::string(), 0, nullptr);
              wake = wake_target_locked(*core);
              notify = true;
            }
          } else {
            EventRecord event;
            event.type = ARCHOERA_EV_TASK_PROGRESS;
            event.state = 0;
            event.task_id = task_id;
            event.done = step;
            event.total = kPingAsyncTotal;
            event.json = kPingAsyncProgressJson;
            enqueue_event_locked(*core, std::move(event));
            wake = wake_target_locked(*core);
            notify = true;
          }
        }
      }

      if (notify) {
        notify_wake(wake);
      }
      if (cancelled || stop) {
        return;
      }
    }

    // All progress is emitted. Decide the terminal state under the same mutex
    // so a cancel that raced the final progress still wins. The wake callback
    // runs after the mutex is released: a C embedder may re-enter the core
    // from it, which would self-deadlock under the lock.
    WakeTarget wake;
    bool notify = false;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        return;
      }

      auto it = core->tasks.find(task_id);
      if (it == core->tasks.end()) {
        return;
      }

      TaskRecord& task = it->second;
      if (is_terminal(task.state)) {
        return;
      }

      if (task.cancel_requested) {
        settle_task_locked(*core, task, ARCHOERA_TASK_CANCELLED, std::string(),
                           0, nullptr);
      } else {
        settle_task_locked(*core, task, ARCHOERA_TASK_COMPLETED,
                           kPingAsyncResult, 0, nullptr);
      }

      wake = wake_target_locked(*core);
      notify = true;
    }

    if (notify) {
      notify_wake(wake);
    }
  } catch (...) {
    // Standard-library allocation failures must never escape a thread entry
    // point. Move the task to FAILED if possible so a caller waiting on it
    // does not see RUNNING forever; no exception may cross the C ABI.
    fail_async_task(core, task_id);
  }
}

archoera_status handle_system_ping_async(archoera_core& core,
                                         const MethodRoute& route,
                                         std::string_view,
                                         std::string&,
                                         uint64_t* out_task_id,
                                         archoera_error* err) {
  TaskRecord record;
  record.method = std::string(route.name);
  return spawn_async_task<PingAsyncPayload>(&core, std::move(record),
                                            &ping_async_worker,
                                            PingAsyncPayload{}, out_task_id,
                                            err);
}

}  // namespace archoera
