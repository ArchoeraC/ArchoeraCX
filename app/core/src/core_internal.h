/*
 * Shared internal header for the archoera_core sources.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Single point for everything the internal translation units share:
 *   - the CoreState definition behind the opaque ABI handle,
 *   - the version / build-type strings,
 *   - error message constants,
 *   - the task/event settle primitives (enqueue, wake, error plumbing).
 * Method handlers are declared by their domain headers and registered in
 * method_registry.cpp; the public ABI export surface lives only in
 * archoera_core.cpp.
 */

#pragma once

#include "archoera_abi.h"
#include "method_registry.h"
#include "module_registry.h"
#include "platform_appearance.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <memory>
#include <mutex>
#include <string>
#include <string_view>
#include <thread>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

namespace archoera {

#define ARCHOERA_CORE_VERSION "0.0.1+1"

inline constexpr std::string_view kArchoeraVersion = ARCHOERA_CORE_VERSION;

#if defined(NDEBUG)
#define ARCHOERA_CORE_BUILD_TYPE "Release"
#else
#define ARCHOERA_CORE_BUILD_TYPE "Debug"
#endif

// Static JSON. The pointer returned by archoera_build_info() stays valid for
// the whole process lifetime, as required by the ABI.
inline constexpr char kBuildInfoJson[] =
    "{\"name\":\"archoera_core\",\"version\":\"" ARCHOERA_CORE_VERSION
    "\",\"buildType\":\"" ARCHOERA_CORE_BUILD_TYPE
    "\",\"abiVersion\":1,\"modules\":[\"system\"]}";

// ARCHOERA_API is owned by the public ABI header: the MSVC export attribute
// must match on the declaration and the definition.

// ---------------------------------------------------------------------------
// Error message constants
// ---------------------------------------------------------------------------

constexpr const char* kMsgNullCore = "core handle is null";
constexpr const char* kMsgNullOutput = "output pointer is null";
constexpr const char* kMsgInvalidMethod = "method must not be null or empty";
constexpr const char* kMsgUnknownMethod = "unknown method";
constexpr const char* kMsgModuleUnavailable = "business module is not available";
constexpr const char* kMsgModuleRejected = "module rejected the call";
constexpr const char* kMsgTaskNotFound = "task not found or already released";
constexpr const char* kMsgTaskBusy = "task is not terminal";
constexpr const char* kMsgBufferTooSmall = "buffer is too small";
constexpr const char* kMsgOutOfMemory = "out of memory";
constexpr const char* kMsgInternal = "internal core error";
constexpr const char* kMsgShuttingDown = "core is shutting down";
constexpr const char* kMsgOpenUrlInvalid = "system.openUrl requires http(s)";
constexpr const char* kMsgOpenPathInvalid =
    "system.openPath requires an absolute existing directory";
constexpr const char* kMsgStatusPathInvalid =
    "system.status requires an absolute path";
constexpr const char* kMsgNotifyNeedsPayload =
    "system.notify requires a title or body";
constexpr const char* kMsgNotifyTooLarge =
    "system.notify payload is too large";

// ---------------------------------------------------------------------------
// State records
// ---------------------------------------------------------------------------

struct EventRecord {
  int32_t type = 0;
  int32_t state = 0;
  int32_t code = 0;
  uint32_t flags = 0;
  uint64_t seq = 0;
  uint64_t task_id = 0;
  int64_t done = 0;
  int64_t total = 0;
  int64_t bytes_per_sec = 0;
  std::string json;
};

struct TaskRecord {
  uint64_t id = 0;
  std::string method;
  std::string params;
  archoera_task_state state = ARCHOERA_TASK_QUEUED;
  std::string result;
  // True for tasks backed by a core-owned worker thread. Cancellation for these
  // is cooperative: cancel only sets this flag, the worker performs the
  // RUNNING -> CANCELLED transition and emits the terminal event.
  bool async = false;
  // True when the task is owned by a loaded native module. Cancellation is
  // forwarded to the module and the module's done callback settles the state.
  bool module_backed = false;
  std::string module;
  bool cancel_requested = false;
  // Set when the task reaches a terminal state. The result stays readable for
  // kTaskResultTtl after this point; afterwards the record is purged so a
  // forgotten archoera_release cannot grow the map for the process lifetime.
  std::chrono::steady_clock::time_point settled_at;
};

// One core-owned worker thread. `done` is set by the trampoline after the
// entry point has returned, so the submitting thread can join finished
// workers without waiting for shutdown.
struct WorkerSlot {
  std::thread thread;
  std::atomic<bool> done{false};
};

// Kept alive by archoera_core::module_calls until the module's done callback
// arrives. This makes a callback that fires after `call` returned safe even if
// the core worker has already exited.
struct ModuleCallContext {
  archoera_core* core = nullptr;
  uint64_t task_id = 0;
};

struct WakeTarget {
  archoera_wake_fn fn = nullptr;
  void* user = nullptr;
};

// Native sampler owned by system.status.watch. A single thread waits on a
// condition variable for the next interval (no busy polling) and pushes one
// domain event per tick. Defined by core_status.h.
struct StatusWatch;

}  // namespace archoera

// ---------------------------------------------------------------------------
// CoreState: the opaque handle from the public header. It must stay in the
// global namespace so it is the same type as the forward declaration in the
// ABI.
// ---------------------------------------------------------------------------
struct archoera_core {
  std::mutex mutex;
  std::unordered_map<uint64_t, archoera::TaskRecord> tasks;
  std::deque<archoera::EventRecord> events;

  // Backing storage for event.json pointers handed out by the most recent
  // poll. They stay valid until the next poll clears this deque.
  std::deque<std::string> poll_payloads;

  archoera_wake_fn wake_fn = nullptr;
  void* wake_user = nullptr;
  uint64_t next_task_id = 1;
  uint64_t next_event_seq = 1;
  std::string config_json;

  // Worker lifecycle. Finished workers are reaped when the next async task is
  // submitted; archoera_shutdown joins every remaining thread before the core
  // is deleted. They are never detached. shutting_down is set under mutex and
  // is the last gate a worker checks before touching the task/event state.
  // Slots are heap-allocated so the vector can grow without moving the
  // non-movable atomics/threads.
  std::vector<std::unique_ptr<archoera::WorkerSlot>> workers;
  std::condition_variable worker_cv;
  std::atomic<bool> shutting_down{false};

  // Native module host. Constructed by archoera_init; may be unloaded (no
  // entry point) when the module is missing. It is shut down after every
  // worker has been joined and before this core is deleted.
  std::unique_ptr<archoera::ModuleRegistry> module_registry;
  // In-flight module calls, keyed by task id and held until done arrives.
  std::unordered_map<uint64_t, std::shared_ptr<archoera::ModuleCallContext>>
      module_calls;

  // Lazily created on the first system.appearance call. The watcher owns an
  // OS notification thread and is stopped before this core is deleted.
  std::unique_ptr<archoera::PlatformAppearanceWatcher> appearance;
  bool appearance_started = false;

  // system.status.watch: one native sampler thread that pushes a domain event
  // every interval. Started on demand and joined before the core is deleted.
  std::unique_ptr<archoera::StatusWatch> status_watch;

  // Used for the launcher process uptime row; steady clock never jumps.
  std::chrono::steady_clock::time_point started_at =
      std::chrono::steady_clock::now();
};

namespace archoera {

// ---------------------------------------------------------------------------
// Error plumbing
// ---------------------------------------------------------------------------

inline void set_error(archoera_error* err,
                      int32_t code,
                      const char* message,
                      const char* detail = nullptr) {
  if (err == nullptr) {
    return;
  }
  err->code = code;
  err->message = message;
  err->detail = detail;
}

inline void clear_error(archoera_error* err) {
  if (err == nullptr) {
    return;
  }
  err->code = static_cast<int32_t>(ARCHOERA_OK);
  err->message = nullptr;
  err->detail = nullptr;
}

// ---------------------------------------------------------------------------
// Event / wake primitives
// ---------------------------------------------------------------------------

inline bool is_terminal(archoera_task_state state) {
  return state == ARCHOERA_TASK_COMPLETED || state == ARCHOERA_TASK_FAILED ||
         state == ARCHOERA_TASK_CANCELLED;
}

// Hard limit for the pending event queue. Producers must never block, so a
// full queue sheds progress/domain events instead. Terminal state events are
// never dropped: there is at most one per task, and dropping one would leave
// a Dart waiter hanging forever. If only terminal events remain, the queue is
// allowed to grow past this limit by the number of unread terminals; the
// 10-minute task TTL and release keep that count bounded.
inline constexpr std::size_t kMaxPendingEvents = 4096;

// How long a terminal task result stays readable without archoera_release.
// Mirrors docs/10-architecture-design.md §5.6.
inline constexpr std::chrono::minutes kTaskResultTtl{10};

// True when `event` may be discarded to make room for a newer event. Task
// state events are droppable only while the task is still running.
inline bool is_droppable_event(const EventRecord& event) {
  if (event.type == ARCHOERA_EV_TASK_STATE) {
    return !is_terminal(static_cast<archoera_task_state>(event.state));
  }
  return event.type == ARCHOERA_EV_TASK_PROGRESS ||
         event.type == ARCHOERA_EV_DOMAIN;
}

// Drops the oldest droppable event, or does nothing when every queued event is
// terminal. Kept separate from the queue so the cap policy is testable without
// constructing a core handle.
inline void drop_oldest_droppable_event(std::deque<EventRecord>& events) {
  const auto victim =
      std::find_if(events.begin(), events.end(), is_droppable_event);
  if (victim != events.end()) {
    events.erase(victim);
  }
}

// Appends `event` with a fresh, monotonic sequence number and enforces the
// queue cap. Requires the caller to hold core.mutex.
inline void enqueue_event(std::deque<EventRecord>& events,
                          uint64_t& next_seq,
                          EventRecord event) {
  event.seq = next_seq++;
  if (events.size() >= kMaxPendingEvents) {
    drop_oldest_droppable_event(events);
  }
  events.push_back(std::move(event));
}

// Requires core.mutex to be held.
inline void enqueue_event_locked(archoera_core& core, EventRecord event) {
  enqueue_event(core.events, core.next_event_seq, std::move(event));
}

// Requires core.mutex. Purges terminal results whose TTL elapsed. Non-terminal
// tasks are never purged: cancel must keep reaching a live task even when its
// result was never read.
inline void purge_expired_tasks_locked(archoera_core& core) {
  const auto now = std::chrono::steady_clock::now();
  for (auto it = core.tasks.begin(); it != core.tasks.end();) {
    const TaskRecord& task = it->second;
    const bool settled =
        task.settled_at.time_since_epoch().count() != 0;
    if (is_terminal(task.state) && settled &&
        now - task.settled_at >= kTaskResultTtl) {
      it = core.tasks.erase(it);
    } else {
      ++it;
    }
  }
}

inline WakeTarget wake_target_locked(const archoera_core& core) {
  WakeTarget target;
  target.fn = core.wake_fn;
  target.user = core.wake_user;
  return target;
}

inline void notify_wake(const WakeTarget& target) noexcept {
  if (target.fn == nullptr) {
    return;
  }
  try {
    target.fn(target.user);
  } catch (...) {
    // A C callback must not unwind through the ABI boundary.
  }
}

}  // namespace archoera
