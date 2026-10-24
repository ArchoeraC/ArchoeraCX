/*
 * Task bookkeeping shared by every core-owned worker.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Terminal transitions for async tasks, domain-event publication and the
 * single start_* engine all live here. The engine replaces the duplicated
 * per-method start_* registration blocks: callers hand in a TaskRecord
 * (method / params / module fields), a worker entry and its payload, and the
 * engine handles the shutting-down gate, id allocation, rollback on thread
 * creation failure and the error plumbing.
 */

#pragma once

#include "core_internal.h"

#include <type_traits>
#include <utility>

namespace archoera {

/*
 * Terminal transition primitive. Requires core.mutex to be held and the task
 * to be a live, non-terminal record; an already-terminal record is a silent
 * no-op so callers can gate with a single branch like the original blocks.
 * `message` feeds the event json; completing/cancelling callers may leave it
 * null to use the fixed phrasing. States map to phases:
 *   COMPLETED / FAILED / CANCELLED -> "completed" / "failed" / "cancelled".
 */
void settle_task_locked(archoera_core& core,
                        TaskRecord& task,
                        archoera_task_state state,
                        std::string result,
                        int32_t event_code,
                        const char* message);

/*
 * Unlocked settle: takes the lock, re-checks the shutting-down gate and the
 * task, applies the terminal transition and wakes Dart outside the lock.
 * Never throws and is the single entry point for worker error paths.
 */
void settle_task_terminal(archoera_core* core,
                          uint64_t task_id,
                          archoera_task_state state,
                          std::string result,
                          int32_t event_code,
                          const char* message) noexcept;

/* Worker fast-fail: move an in-flight task to FAILED without a result. */
void fail_async_task(archoera_core* core, uint64_t task_id) noexcept;

/* Requires core.mutex to be held. A released/missing task is a stop signal
 * for its worker because there is no longer anywhere to publish progress or
 * state. */
bool async_task_should_stop_locked(const archoera_core& core,
                                   uint64_t task_id);

/* Publishes a `schemaVersion`+`kind` domain event without a task id. The
 * payload is fully built; this only performs the enqueued-event mechanics. */
void publish_domain_event(archoera_core* core, std::string json) noexcept;

/* Registers a synchronous call's COMPLETED task and event, then wakes Dart.
 * Returns the ABI status; on failure `err` carries the message. */
archoera_status complete_sync_call(archoera_core* core,
                                   std::string_view method,
                                   std::string result,
                                   uint64_t* out_task_id,
                                   archoera_error* err);

/* system.pingAsync probe worker: emits three progress events on a core-owned
 * thread and settles through the shared state machine. */
archoera_status handle_system_ping_async(archoera_core& core,
                                         const MethodRoute& route,
                                         std::string_view params,
                                         std::string& result,
                                         uint64_t* out_task_id,
                                         archoera_error* err);

/* Uniform worker entry type handed to std::thread. The heap payload was
 * created by the engine and is owned (and freed) by the worker. */
template <typename Payload>
using TaskWorkerEntry = void (*)(archoera_core* core,
                                 uint64_t task_id,
                                 Payload* payload);

// std::thread entry. Marks the slot done only after the real entry point has
// returned, so the slot is safe to join from the submitting thread.
template <typename Payload>
void run_task_worker(archoera_core* core,
                     uint64_t task_id,
                     Payload* payload,
                     TaskWorkerEntry<Payload> entry,
                     std::atomic<bool>* done) noexcept {
  try {
    entry(core, task_id, payload);
  } catch (...) {
    // A worker entry point must never unwind out of the thread.
  }
  done->store(true, std::memory_order_release);
}

// Requires core.mutex. Joins finished workers (except the caller) so threads
// do not accumulate for the whole process lifetime. `done` is stored after the
// entry point returned, so the worker no longer touches core state here.
inline void reap_finished_workers_locked(archoera_core& core) {
  const std::thread::id self = std::this_thread::get_id();
  auto it = core.workers.begin();
  while (it != core.workers.end()) {
    WorkerSlot& slot = **it;
    if (slot.done.load(std::memory_order_acquire) &&
        slot.thread.joinable() && slot.thread.get_id() != self) {
      slot.thread.join();
      it = core.workers.erase(it);
    } else {
      ++it;
    }
  }
}

/*
 * The single async start engine. Registers `record` (the id/state/async
 * flags are filled in here), spawns `thread_entry` with the payload moved to
 * the heap, and rolls both back when thread creation fails. On success
 * *out_task_id carries the id and `err` is cleared.
 */
template <typename Payload>
archoera_status spawn_async_task(
    archoera_core* core,
    TaskRecord record,
    TaskWorkerEntry<Payload> thread_entry,
    Payload payload,
    uint64_t* out_task_id,
    archoera_error* err) {
  static_assert(std::is_nothrow_move_constructible_v<Payload>);

  try {
    Payload* heap_payload = new Payload(std::move(payload));
    uint64_t task_id = 0;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        delete heap_payload;
        set_error(err, ARCHOERA_E_INVALID, kMsgShuttingDown);
        return ARCHOERA_E_INVALID;
      }

      reap_finished_workers_locked(*core);

      task_id = core->next_task_id++;
      record.id = task_id;
      record.state = ARCHOERA_TASK_RUNNING;
      record.async = true;

      auto inserted = core->tasks.emplace(task_id, std::move(record));
      try {
        // Allocate the slot before starting the thread: a failing push_back
        // must never leave an untracked thread behind.
        core->workers.push_back(std::make_unique<WorkerSlot>());
      } catch (...) {
        core->tasks.erase(inserted.first);
        delete heap_payload;
        throw;
      }

      WorkerSlot& slot = *core->workers.back();
      try {
        slot.thread = std::thread(&run_task_worker<Payload>, core, task_id,
                                  heap_payload, thread_entry, &slot.done);
      } catch (...) {
        core->tasks.erase(inserted.first);
        core->workers.pop_back();
        delete heap_payload;
        throw;
      }
    }

    *out_task_id = task_id;
    clear_error(err);
    return ARCHOERA_OK;
  } catch (const std::bad_alloc&) {
    set_error(err, ARCHOERA_E_INTERNAL, kMsgOutOfMemory);
    return ARCHOERA_E_INTERNAL;
  } catch (...) {
    set_error(err, ARCHOERA_E_INTERNAL, kMsgInternal);
    return ARCHOERA_E_INTERNAL;
  }
}

}  // namespace archoera
