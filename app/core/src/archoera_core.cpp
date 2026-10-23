/*
 * ArchoeraCX native core ABI v1.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * This translation unit owns the C boundary declared in
 * contracts/abi/archoera_abi.h. It intentionally contains no business logic:
 * only lifecycle, task bookkeeping, result storage and the event queue.
 */

#include "archoera_abi.h"
#include "method_registry.h"
#include "module_registry.h"
#include "platform_appearance.h"
#include "platform_status.h"
#include "platform_system.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <deque>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

using archoera::MethodRoute;
using archoera::MethodRouteKind;
using archoera::ModuleRegistry;
using archoera::find_method_route;

#if defined(_WIN32) || defined(__CYGWIN__)
#  if defined(ARCHOERA_CORE_EXPORTS)
#    define ARCHOERA_API __declspec(dllexport)
#  else
#    define ARCHOERA_API __declspec(dllimport)
#  endif
#elif defined(__GNUC__) || defined(__clang__)
#  define ARCHOERA_API __attribute__((visibility("default")))
#else
#  define ARCHOERA_API
#endif

namespace {

constexpr int32_t kMaxPollBatch = 64;

#if defined(NDEBUG)
#  define ARCHOERA_BUILD_TYPE "Release"
#else
#  define ARCHOERA_BUILD_TYPE "Debug"
#endif

// Static JSON. The pointer returned by archoera_build_info() stays valid for
// the whole process lifetime, as required by the ABI.
constexpr const char* kBuildInfo =
    "{\"name\":\"archoera_core\",\"version\":\"0.0.1+1\",\"buildType\":\""
    ARCHOERA_BUILD_TYPE
    "\",\"abiVersion\":1,\"modules\":[\"system\"]}";

constexpr const char* kPingResult =
    "{\"ok\":true,\"data\":{\"pong\":true,\"method\":\"system.ping\"},"
    "\"meta\":{\"schemaVersion\":1}}";

constexpr const char* kPingAsyncResult =
    "{\"ok\":true,\"data\":{\"pong\":true,\"method\":\"system.pingAsync\"},"
    "\"meta\":{\"schemaVersion\":1}}";

// system.pingAsync is a deliberately small probe task used to exercise the
// wake path end to end. It is not business logic: it only moves through the
// task state machine and emits observable progress.
constexpr const char* kPingAsyncMethod = "system.pingAsync";
constexpr const char* kPingAsyncProgressJson = "{\"phase\":\"probe\"}";
constexpr int64_t kPingAsyncTotal = 3;
constexpr auto kPingAsyncStepDelay = std::chrono::milliseconds(25);

// Public routing is owned by method_registry.cpp.

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

}  // namespace

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

// The opaque handle from the public header. It must stay in the global
// namespace so it is the same type as the forward declaration in the ABI.
struct archoera_core {
  std::mutex mutex;
  std::unordered_map<uint64_t, TaskRecord> tasks;
  std::deque<EventRecord> events;

  // Backing storage for event.json pointers handed out by the most recent
  // poll. They stay valid until the next poll clears this deque.
  std::deque<std::string> poll_payloads;

  archoera_wake_fn wake_fn = nullptr;
  void* wake_user = nullptr;
  uint64_t next_task_id = 1;
  uint64_t next_event_seq = 1;
  std::string config_json;

  // Worker lifecycle. Threads are joined by archoera_shutdown before the core
  // is deleted; they are never detached. shutting_down is set under mutex and
  // is the last gate a worker checks before touching the task/event state.
  std::vector<std::thread> workers;
  std::condition_variable worker_cv;
  std::atomic<bool> shutting_down{false};

  // Native module host. Constructed by archoera_init; may be unloaded (no
  // entry point) when the module is missing. It is shut down after every
  // worker has been joined and before this core is deleted.
  std::unique_ptr<ModuleRegistry> module_registry;
  // In-flight module calls, keyed by task id and held until done arrives.
  std::unordered_map<uint64_t, std::shared_ptr<ModuleCallContext>> module_calls;

  // Lazily created on the first system.appearance call. The watcher owns an
  // OS notification thread and is stopped before this core is deleted.
  std::unique_ptr<archoera::PlatformAppearanceWatcher> appearance;
  bool appearance_started = false;

  // system.status.watch: one native sampler thread that pushes a domain event
  // every interval. Started on demand and joined before the core is deleted.
  std::unique_ptr<StatusWatch> status_watch;

  // Used for the launcher process uptime row; steady clock never jumps.
  std::chrono::steady_clock::time_point started_at =
      std::chrono::steady_clock::now();
};

namespace {

// Shutdown is documented as idempotent. Tracking live handles lets a repeated
// shutdown (or a shutdown of an already-freed handle) become a no-op instead of
// a double delete. Callers must still not use a handle after shutdown.
std::mutex g_live_mutex;
std::unordered_set<archoera_core*> g_live_cores;

void set_error(archoera_error* err,
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

void clear_error(archoera_error* err) {
  if (err == nullptr) {
    return;
  }
  err->code = static_cast<int32_t>(ARCHOERA_OK);
  err->message = nullptr;
  err->detail = nullptr;
}

bool is_terminal(archoera_task_state state) {
  return state == ARCHOERA_TASK_COMPLETED || state == ARCHOERA_TASK_FAILED ||
         state == ARCHOERA_TASK_CANCELLED;
}

// Requires core.mutex to be held.
void enqueue_event_locked(archoera_core& core, EventRecord event) {
  event.seq = core.next_event_seq++;
  core.events.push_back(std::move(event));
}

WakeTarget wake_target_locked(const archoera_core& core) {
  WakeTarget target;
  target.fn = core.wake_fn;
  target.user = core.wake_user;
  return target;
}

void notify_wake(const WakeTarget& target) noexcept {
  if (target.fn == nullptr) {
    return;
  }
  try {
    target.fn(target.user);
  } catch (...) {
    // A C callback must not unwind through the ABI boundary.
  }
}

std::string make_state_json(const std::string& method,
                            const char* phase,
                            const char* message) {
  std::string json;
  json.reserve(method.size() + 96);
  json.append("{\"method\":\"");
  json.append(method);
  json.append("\",\"phase\":\"");
  json.append(phase);
  json.append("\",\"message\":\"");
  json.append(message);
  json.append("\"}");
  return json;
}

std::string make_info_result() {
  std::string json;
  json.reserve(256);
  json.append("{\"ok\":true,\"data\":{\"abiVersion\":");
  json.append(std::to_string(ARCHOERA_ABI_VERSION));
  json.append(",\"buildInfo\":");
  json.append(kBuildInfo);
  json.append(
      ",\"modules\":[{\"name\":\"system\",\"state\":\"ready\"}]},"
      "\"meta\":{\"schemaVersion\":1}}");
  return json;
}

void append_json_escaped(std::string& out, const char* text);

std::string make_modules_result(const archoera_core& core) {
  std::string json =
      "{\"ok\":true,\"data\":{\"modules\":[{\"name\":\"system\","
      "\"version\":\"0.0.1+1\",\"state\":\"ready\","
      "\"methods\":[\"system.ping\",\"system.pingAsync\","
      "\"system.info\",\"system.modules\",\"system.appearance\","
      "\"system.openUrl\",\"system.openPath\",\"system.notify\","
      "\"system.status\","
      "\"system.status.watch\",\"system.status.unwatch\"]}";
  if (core.module_registry != nullptr) {
    for (const auto& module : core.module_registry->modules()) {
      json.append(",{\"name\":\"");
      append_json_escaped(json, module.name.c_str());
      json.append("\",\"version\":\"");
      append_json_escaped(json, module.version.c_str());
      json.append("\",\"state\":\"ready\"}");
    }
  }
  json.append("]},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

void append_json_escaped(std::string& out, const char* text) {
  if (text == nullptr) {
    return;
  }
  for (const char* p = text; *p != '\0'; ++p) {
    switch (*p) {
      case '\\':
        out.append("\\\\");
        break;
      case '"':
        out.append("\\\"");
        break;
      case '\n':
        out.append("\\n");
        break;
      case '\r':
        out.append("\\r");
        break;
      case '\t':
        out.append("\\t");
        break;
      default:
        out.push_back(*p);
        break;
    }
  }
}

// Minimal JSON string extraction for the few core methods that take scalar
// parameters (URL, notification title/body). The payloads are generated by
// Dart's jsonEncode, so this only needs to decode string escapes.
void append_utf8(std::string& out, uint32_t code_point) {
  if (code_point <= 0x7F) {
    out.push_back(static_cast<char>(code_point));
  } else if (code_point <= 0x7FF) {
    out.push_back(static_cast<char>(0xC0 | (code_point >> 6)));
    out.push_back(static_cast<char>(0x80 | (code_point & 0x3F)));
  } else if (code_point <= 0xFFFF) {
    out.push_back(static_cast<char>(0xE0 | (code_point >> 12)));
    out.push_back(static_cast<char>(0x80 | ((code_point >> 6) & 0x3F)));
    out.push_back(static_cast<char>(0x80 | (code_point & 0x3F)));
  } else {
    out.push_back(static_cast<char>(0xF0 | (code_point >> 18)));
    out.push_back(static_cast<char>(0x80 | ((code_point >> 12) & 0x3F)));
    out.push_back(static_cast<char>(0x80 | ((code_point >> 6) & 0x3F)));
    out.push_back(static_cast<char>(0x80 | (code_point & 0x3F)));
  }
}

int hex_value(char ch) {
  if (ch >= '0' && ch <= '9') return ch - '0';
  if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
  if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
  return -1;
}

bool extract_json_string(const std::string& json,
                         const char* key,
                         std::string* out) {
  if (out == nullptr || key == nullptr) {
    return false;
  }
  const std::string needle = std::string("\"") + key + "\"";
  size_t position = json.find(needle);
  if (position == std::string::npos) {
    return false;
  }
  position += needle.size();
  while (position < json.size() &&
         (json[position] == ' ' || json[position] == '\t' ||
          json[position] == '\r' || json[position] == '\n')) {
    ++position;
  }
  if (position >= json.size() || json[position] != ':') {
    return false;
  }
  ++position;
  while (position < json.size() &&
         (json[position] == ' ' || json[position] == '\t' ||
          json[position] == '\r' || json[position] == '\n')) {
    ++position;
  }
  if (position >= json.size() || json[position] != '"') {
    return false;
  }
  ++position;

  std::string value;
  while (position < json.size()) {
    const char ch = json[position++];
    if (ch == '"') {
      *out = std::move(value);
      return true;
    }
    if (ch != '\\') {
      value.push_back(ch);
      continue;
    }
    if (position >= json.size()) {
      return false;
    }
    const char escaped = json[position++];
    switch (escaped) {
      case '"':
      case '\\':
      case '/':
        value.push_back(escaped);
        break;
      case 'b':
        value.push_back('\b');
        break;
      case 'f':
        value.push_back('\f');
        break;
      case 'n':
        value.push_back('\n');
        break;
      case 'r':
        value.push_back('\r');
        break;
      case 't':
        value.push_back('\t');
        break;
      case 'u': {
        if (position + 4 > json.size()) {
          return false;
        }
        uint32_t code_point = 0;
        for (int index = 0; index < 4; ++index) {
          const int digit = hex_value(json[position + index]);
          if (digit < 0) {
            return false;
          }
          code_point = (code_point << 4) | static_cast<uint32_t>(digit);
        }
        position += 4;
        append_utf8(value, code_point);
        break;
      }
      default:
        return false;
    }
  }
  return false;
}

std::string make_action_result(const char* field, bool value) {
  std::string json = "{\"ok\":true,\"data\":{\"";
  json.append(field);
  json.append("\":");
  json.append(value ? "true" : "false");
  json.append("},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

std::string make_appearance_data(const archoera::SystemAppearance& state) {  std::string json = "{\"theme\":\"";
  if (!state.theme_known) {
    json.append("unknown");
  } else {
    json.append(state.dark ? "dark" : "light");
  }
  json.append("\",\"accent\":");
  if (state.accent_known) {
    json.append("{\"r\":");
    json.append(std::to_string(state.accent_r));
    json.append(",\"g\":");
    json.append(std::to_string(state.accent_g));
    json.append(",\"b\":");
    json.append(std::to_string(state.accent_b));
    json.append("}");
  } else {
    json.append("null");
  }
  json.append(",\"source\":\"");
  append_json_escaped(json, state.source.c_str());
  json.append("\"}");
  return json;
}

std::string make_appearance_result(const archoera::SystemAppearance& state) {
  std::string json = "{\"ok\":true,\"data\":";
  json.append(make_appearance_data(state));
  json.append(",\"meta\":{\"schemaVersion\":1}}");
  return json;
}

// Publishes an appearance change as a domain event. The event is not tied to a
// task, so task_id stays 0 and Dart routes it by payload kind.
void publish_appearance(archoera_core* core,
                        const archoera::SystemAppearance& state) noexcept {
  try {
    std::string json =
        "{\"schemaVersion\":1,\"kind\":\"system.appearance\",";
    const std::string data = make_appearance_data(state);
    // Reuse the data object's members without the outer braces.
    json.append(data.substr(1, data.size() - 2));
    json.append("}");

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
    // Appearance notifications are cosmetic; a failure must not escape.
  }
}

void appearance_callback(void* user,
                         const archoera::SystemAppearance& state) noexcept {
  if (user == nullptr) {
    return;
  }
  publish_appearance(static_cast<archoera_core*>(user), state);
}

// Error envelope stored as a failed task's result. ABI status codes are
// normalized by the caller; domain detail would live in a future field.
std::string make_error_result(int32_t code, const char* message) {
  std::string json;
  json.reserve(160);
  json.append("{\"ok\":false,\"error\":{\"code\":");
  json.append(std::to_string(code));
  json.append(",\"message\":\"");
  append_json_escaped(json, message != nullptr ? message : "module call failed");
  json.append("\"},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

constexpr const char* kMsgShuttingDown = "core is shutting down";

// Requires core.mutex to be held. A released/missing task is a stop signal for
// its worker because there is no longer anywhere to publish progress or state.
bool extract_json_int(const std::string& json,
                      const char* key,
                      long long* out) {
  if (out == nullptr || key == nullptr) return false;
  const std::string needle = std::string("\"") + key + "\"";
  size_t position = json.find(needle);
  if (position == std::string::npos) return false;
  position += needle.size();
  while (position < json.size() &&
         (json[position] == ' ' || json[position] == '\t' ||
          json[position] == '\r' || json[position] == '\n')) {
    ++position;
  }
  if (position >= json.size() || json[position] != ':') return false;
  ++position;
  while (position < json.size() &&
         (json[position] == ' ' || json[position] == '\t' ||
          json[position] == '\r' || json[position] == '\n')) {
    ++position;
  }
  char* end = nullptr;
  const long long value = std::strtoll(json.c_str() + position, &end, 10);
  if (end == json.c_str() + position) return false;
  *out = value;
  return true;
}

bool async_task_should_stop_locked(const archoera_core& core, uint64_t task_id) {
  auto it = core.tasks.find(task_id);
  if (it == core.tasks.end()) {
    return true;
  }
  return it->second.cancel_requested;
}

// Best-effort terminal transition for a worker that hit an internal error
// (normally std::bad_alloc). Never throws and never calls wake under the lock.
void fail_async_task(archoera_core* core, uint64_t task_id) noexcept {
  try {
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
      task.state = ARCHOERA_TASK_FAILED;
      task.result.clear();

      EventRecord event;
      event.type = ARCHOERA_EV_TASK_STATE;
      event.state = ARCHOERA_TASK_FAILED;
      event.task_id = task_id;
      event.json =
          make_state_json(task.method, "failed", "internal worker error");
      enqueue_event_locked(*core, std::move(event));

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

// Worker entry point for system.pingAsync. Runs on a core-owned thread, emits
// progress while the caller polls, then performs the terminal transition.
// Events are always enqueued under core.mutex, but the wake callback is always
// invoked after that mutex has been released.
void ping_async_worker(archoera_core* core, uint64_t task_id) noexcept {
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
              it->second.result.clear();

              EventRecord event;
              event.type = ARCHOERA_EV_TASK_STATE;
              event.state = ARCHOERA_TASK_CANCELLED;
              event.task_id = task_id;
              event.json = make_state_json(it->second.method, "cancelled",
                                           "task cancelled");
              enqueue_event_locked(*core, std::move(event));
              it->second.state = ARCHOERA_TASK_CANCELLED;
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
    // so a cancel that raced the final progress still wins.
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

      EventRecord event;
      event.type = ARCHOERA_EV_TASK_STATE;
      event.task_id = task_id;

      if (task.cancel_requested) {
        task.result.clear();
        event.state = ARCHOERA_TASK_CANCELLED;
        event.json =
            make_state_json(task.method, "cancelled", "task cancelled");
        enqueue_event_locked(*core, std::move(event));
        task.state = ARCHOERA_TASK_CANCELLED;
      } else {
        task.result = kPingAsyncResult;
        event.state = ARCHOERA_TASK_COMPLETED;
        event.json =
            make_state_json(task.method, "completed", "task completed");
        enqueue_event_locked(*core, std::move(event));
        task.state = ARCHOERA_TASK_COMPLETED;
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

archoera_status start_ping_async(archoera_core* core,
                                 uint64_t* out_task_id,
                                 archoera_error* err) {
  try {
    uint64_t task_id = 0;

    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        set_error(err, ARCHOERA_E_INVALID, kMsgShuttingDown);
        return ARCHOERA_E_INVALID;
      }

      task_id = core->next_task_id++;
      TaskRecord task;
      task.id = task_id;
      task.method = kPingAsyncMethod;
      task.state = ARCHOERA_TASK_RUNNING;
      task.async = true;

      auto inserted = core->tasks.emplace(task_id, std::move(task));
      try {
        core->workers.emplace_back(&ping_async_worker, core, task_id);
      } catch (...) {
        core->tasks.erase(inserted.first);
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

// ---------------------------------------------------------------------------
// Native system actions (openUrl / openPath / notify)
// ---------------------------------------------------------------------------

enum class SystemActionKind { kOpenUrl, kOpenPath, kNotify };

struct SystemAction {
  SystemActionKind kind = SystemActionKind::kOpenUrl;
  std::string method;
  std::string url;
  std::string path;
  std::string title;
  std::string body;
};

// system.openPath echoes the directory back so Dart can correlate the result
// without tracking parameters itself.
std::string make_open_path_result(const std::string& path, bool opened) {
  std::string json = "{\"ok\":true,\"data\":{\"path\":\"";
  append_json_escaped(json, path.c_str());
  json.append("\",\"opened\":");
  json.append(opened ? "true" : "false");
  json.append("},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

std::string make_system_action_result(const SystemAction& action,
                                      bool success) {
  if (action.kind == SystemActionKind::kOpenPath) {
    return make_open_path_result(action.path, success);
  }
  return make_action_result(
      action.kind == SystemActionKind::kOpenUrl ? "opened" : "sent", success);
}

void system_action_worker(archoera_core* core,
                          uint64_t task_id,
                          SystemAction action) noexcept {
  bool success = false;
  try {
    std::string error;
    if (action.kind == SystemActionKind::kOpenUrl) {
      success = archoera::open_external_url(action.url, &error);
    } else if (action.kind == SystemActionKind::kOpenPath) {
      success = archoera::open_path(action.path, &error);
    } else {
      success = archoera::send_system_notification(action.title, action.body,
                                                    &error);
    }
  } catch (...) {
    success = false;
  }

  try {
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
      EventRecord event;
      event.type = ARCHOERA_EV_TASK_STATE;
      event.task_id = task_id;

      if (task.cancel_requested) {
        task.state = ARCHOERA_TASK_CANCELLED;
        task.result.clear();
        event.state = ARCHOERA_TASK_CANCELLED;
        event.json = make_state_json(task.method, "cancelled", "task cancelled");
      } else {
        task.state = ARCHOERA_TASK_COMPLETED;
        task.result = make_system_action_result(action, success);
        event.state = ARCHOERA_TASK_COMPLETED;
        event.json =
            make_state_json(task.method, "completed", "task completed");
      }

      enqueue_event_locked(*core, std::move(event));
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

archoera_status start_system_action(archoera_core* core,
                                    SystemAction action,
                                    uint64_t* out_task_id,
                                    archoera_error* err) {
  try {
    uint64_t task_id = 0;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        set_error(err, ARCHOERA_E_INVALID, kMsgShuttingDown);
        return ARCHOERA_E_INVALID;
      }

      task_id = core->next_task_id++;
      TaskRecord task;
      task.id = task_id;
      task.method = action.method;
      task.state = ARCHOERA_TASK_RUNNING;
      task.async = true;

      auto inserted = core->tasks.emplace(task_id, std::move(task));
      try {
        core->workers.emplace_back(&system_action_worker, core, task_id,
                                   std::move(action));
      } catch (...) {
        core->tasks.erase(inserted.first);
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


// ---------------------------------------------------------------------------
// Machine load (system.status / system.status.watch)
// ---------------------------------------------------------------------------

void append_number_field(std::string& json,
                         const char* name,
                         double value) {
  char buffer[64];
  std::snprintf(buffer, sizeof(buffer), "%.1f", value);
  json.append("\"");
  json.append(name);
  json.append("\":");
  json.append(buffer);
}

void append_int_field(std::string& json, const char* name, long long value) {
  json.append("\"");
  json.append(name);
  json.append("\":");
  json.append(std::to_string(value));
}

// Shared payload members for the one-shot result and the pushed event. The
// caller adds the `ok`/`data` or `kind` wrapper.
void append_status_fields(std::string& json,
                          const archoera::SystemStatus& status,
                          bool cpu_known,
                          double cpu_percent,
                          double launcher_uptime_seconds) {
  json.append("\"platform\":\"");
  append_json_escaped(json, status.platform.c_str());
  json.append("\",\"os\":\"");
  append_json_escaped(json, status.os.c_str());
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
    append_json_escaped(json, status.disk_path.c_str());
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
  append_number_field(json, "uptimeSeconds", launcher_uptime_seconds);
  json.append("},");

  json.append("\"system\":{");
  if (status.uptime_known) {
    append_number_field(json, "uptimeSeconds", status.system_uptime_seconds);
  } else {
    json.append("\"uptimeSeconds\":null");
  }
  json.append("}");
}

double launcher_uptime_seconds(const archoera_core& core) {
  const auto elapsed = std::chrono::steady_clock::now() - core.started_at;
  return std::chrono::duration<double>(elapsed).count();
}

std::string make_status_result(const archoera::SystemStatus& status,
                               bool cpu_known,
                               double cpu_percent,
                               double launcher_uptime) {
  std::string json = "{\"ok\":true,\"data\":{";
  append_status_fields(json, status, cpu_known, cpu_percent, launcher_uptime);
  json.append("},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

// Publishes one sample as a `system.status` domain event. Never throws and
// never calls wake under the core lock.
void publish_status(archoera_core* core,
                    const archoera::SystemStatus& status,
                    bool cpu_known,
                    double cpu_percent) noexcept {
  try {
    std::string json = "{\"schemaVersion\":1,\"kind\":\"system.status\",";
    append_status_fields(json, status, cpu_known, cpu_percent,
                         launcher_uptime_seconds(*core));
    json.append("}");

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
    // A missed sample must never escape the sampler thread.
  }
}

void status_watch_loop(archoera_core* core, StatusWatch* watch) noexcept {
  try {
    // First window: a short pair of samples so the first push already carries
    // CPU usage instead of a gap.
    const auto first = archoera::read_cpu_times();
    {
      std::unique_lock<std::mutex> lock(watch->mutex);
      if (watch->cv.wait_for(lock, std::chrono::milliseconds(120),
                             [watch] { return watch->stopping; })) {
        return;
      }
    }

    auto previous = archoera::read_cpu_times();
    double percent = 0.0;
    const bool cpu_known =
        archoera::cpu_usage_percent(first, previous, &percent);
    publish_status(core, archoera::sample_system_status(watch->disk_path),
                   cpu_known, percent);

    std::unique_lock<std::mutex> lock(watch->mutex);
    while (!watch->stopping) {
      if (watch->cv.wait_for(lock, watch->interval,
                             [watch] { return watch->stopping; })) {
        break;
      }
      const std::string path = watch->disk_path;
      lock.unlock();

      const auto current = archoera::read_cpu_times();
      double value = 0.0;
      const bool known = archoera::cpu_usage_percent(previous, current, &value);
      previous = current;
      publish_status(core, archoera::sample_system_status(path), known, value);

      lock.lock();
    }
  } catch (...) {
    // Stop pushing; Dart keeps the last sample and the manual refresh.
  }
}

bool start_status_watch(archoera_core& core,
                        std::chrono::milliseconds interval,
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

void status_worker(archoera_core* core,
                   uint64_t task_id,
                   std::string method,
                   std::string disk_path) noexcept {
  try {
    const auto first = archoera::read_cpu_times();
    std::this_thread::sleep_for(std::chrono::milliseconds(140));
    const auto second = archoera::read_cpu_times();
    double percent = 0.0;
    const bool cpu_known =
        archoera::cpu_usage_percent(first, second, &percent);
    const std::string result = make_status_result(
        archoera::sample_system_status(disk_path), cpu_known, percent,
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
      EventRecord event;
      event.type = ARCHOERA_EV_TASK_STATE;
      event.task_id = task_id;

      if (task.cancel_requested) {
        task.state = ARCHOERA_TASK_CANCELLED;
        task.result.clear();
        event.state = ARCHOERA_TASK_CANCELLED;
        event.json = make_state_json(method, "cancelled", "task cancelled");
      } else {
        task.state = ARCHOERA_TASK_COMPLETED;
        task.result = result;
        event.state = ARCHOERA_TASK_COMPLETED;
        event.json = make_state_json(method, "completed", "task completed");
      }

      enqueue_event_locked(*core, std::move(event));
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

archoera_status start_status_call(archoera_core* core,
                                  std::string method,
                                  std::string disk_path,
                                  uint64_t* out_task_id,
                                  archoera_error* err) {
  try {
    uint64_t task_id = 0;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        set_error(err, ARCHOERA_E_INVALID, kMsgShuttingDown);
        return ARCHOERA_E_INVALID;
      }

      task_id = core->next_task_id++;
      TaskRecord task;
      task.id = task_id;
      task.method = method;
      task.state = ARCHOERA_TASK_RUNNING;
      task.async = true;

      auto inserted = core->tasks.emplace(task_id, std::move(task));
      try {
        core->workers.emplace_back(&status_worker, core, task_id,
                                   std::move(method), std::move(disk_path));
      } catch (...) {
        core->tasks.erase(inserted.first);
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

// ---------------------------------------------------------------------------
// Module-backed tasks (java.scan / loader.* / resource.* / download.execute)
// ---------------------------------------------------------------------------

// Module progress callback. May run on any module thread; it only enqueues a
// progress event under the core mutex and wakes the Dart listener afterwards.
void module_progress_callback(void* user,
                              int64_t done,
                              int64_t total,
                              int64_t bytes_per_sec,
                              const char* json) noexcept {
  try {
    auto* ctx = static_cast<ModuleCallContext*>(user);
    if (ctx == nullptr || ctx->core == nullptr) {
      return;
    }

    archoera_core* core = ctx->core;
    WakeTarget wake;
    bool notify = false;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        return;
      }

      auto it = core->tasks.find(ctx->task_id);
      if (it == core->tasks.end() || is_terminal(it->second.state) ||
          it->second.cancel_requested) {
        return;
      }

      EventRecord event;
      event.type = ARCHOERA_EV_TASK_PROGRESS;
      event.state = 0;
      event.task_id = ctx->task_id;
      event.done = done;
      event.total = total;
      event.bytes_per_sec = bytes_per_sec;
      event.json = (json != nullptr && json[0] != '\0')
                       ? std::string(json)
                       : std::string("{\"source\":\"module\"}");
      enqueue_event_locked(*core, std::move(event));
      wake = wake_target_locked(*core);
      notify = true;
    }

    if (notify) {
      notify_wake(wake);
    }
  } catch (...) {
    // A module callback must not unwind through the ABI.
  }
}

// The module's done callback. May run on the calling worker (synchronously
// inside call) or on a module-owned thread. The context is removed from the
// map while the mutex is held but a local shared_ptr keeps it alive until this
// function returns. The wake callback is always invoked after unlocking.
void module_done_callback(void* user,
                          int32_t status,
                          const char* result_json) noexcept {
  try {
    auto* ctx = static_cast<ModuleCallContext*>(user);
    if (ctx == nullptr || ctx->core == nullptr) {
      return;
    }

    archoera_core* core = ctx->core;
    const uint64_t task_id = ctx->task_id;

    WakeTarget wake;
    bool notify = false;
    std::shared_ptr<ModuleCallContext> keep_alive;

    {
      std::lock_guard<std::mutex> lock(core->mutex);

      auto ctx_it = core->module_calls.find(task_id);
      if (ctx_it != core->module_calls.end()) {
        keep_alive = ctx_it->second;
        core->module_calls.erase(ctx_it);
      }

      // During shutdown the task map is no longer observable; drop the result
      // instead of publishing an event after the UI has gone away.
      if (core->shutting_down.load(std::memory_order_acquire)) {
        return;
      }

      auto it = core->tasks.find(task_id);
      if (it == core->tasks.end() || is_terminal(it->second.state)) {
        return;
      }

      TaskRecord& task = it->second;
      EventRecord event;
      event.type = ARCHOERA_EV_TASK_STATE;
      event.task_id = task_id;
      event.code = status;

      if (status == ARCHOERA_OK && !task.cancel_requested) {
        if (result_json == nullptr) {
          task.state = ARCHOERA_TASK_FAILED;
          task.result =
              make_error_result(ARCHOERA_E_INTERNAL, "module returned no result");
          event.state = ARCHOERA_TASK_FAILED;
          event.json =
              make_state_json(task.method, "failed", "module returned no result");
        } else {
          task.state = ARCHOERA_TASK_COMPLETED;
          task.result = result_json;
          event.state = ARCHOERA_TASK_COMPLETED;
          event.json =
              make_state_json(task.method, "completed", "task completed");
        }
      } else if (status == ARCHOERA_E_CANCELLED || task.cancel_requested) {
        // Cancellation wins even if the module raced to a successful result.
        task.state = ARCHOERA_TASK_CANCELLED;
        task.result.clear();
        event.state = ARCHOERA_TASK_CANCELLED;
        event.json =
            make_state_json(task.method, "cancelled", "task cancelled");
      } else {
        task.state = ARCHOERA_TASK_FAILED;
        task.result = make_error_result(status, "module call failed");
        event.state = ARCHOERA_TASK_FAILED;
        event.json = make_state_json(task.method, "failed", "module call failed");
      }

      enqueue_event_locked(*core, std::move(event));
      wake = wake_target_locked(*core);
      notify = true;
    }

    if (notify) {
      notify_wake(wake);
    }
  } catch (...) {
    // Exceptions must not cross the module ABI boundary.
  }
}

// Terminal transition used when `call` rejects the request (no done follows)
// or the worker itself fails. Never throws and never wakes under the lock.
void fail_module_task(archoera_core* core,
                      uint64_t task_id,
                      int32_t status,
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
      if (it == core->tasks.end() || is_terminal(it->second.state)) {
        return;
      }

      TaskRecord& task = it->second;
      task.state = ARCHOERA_TASK_FAILED;
      task.result = make_error_result(
          status == ARCHOERA_OK ? static_cast<int32_t>(ARCHOERA_E_INTERNAL)
                                : status,
          message);

      EventRecord event;
      event.type = ARCHOERA_EV_TASK_STATE;
      event.state = ARCHOERA_TASK_FAILED;
      event.code = status;
      event.task_id = task_id;
      event.json =
          make_state_json(task.method, "failed",
                          message != nullptr ? message : "module call failed");
      enqueue_event_locked(*core, std::move(event));

      wake = wake_target_locked(*core);
      notify = true;
    }

    if (notify) {
      notify_wake(wake);
    }
  } catch (...) {
    // Best effort: shutdown still joins this worker.
  }
}

// Settle a module task as CANCELLED when the request raced ahead of the
// module (cancel arrived before `call` registered the task). Never throws.
void settle_module_cancelled(archoera_core* core, uint64_t task_id) noexcept {
  try {
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
      task.state = ARCHOERA_TASK_CANCELLED;
      task.result.clear();

      EventRecord event;
      event.type = ARCHOERA_EV_TASK_STATE;
      event.state = ARCHOERA_TASK_CANCELLED;
      event.task_id = task_id;
      event.json = make_state_json(task.method, "cancelled", "task cancelled");
      enqueue_event_locked(*core, std::move(event));

      wake = wake_target_locked(*core);
      notify = true;
    }

    if (notify) {
      notify_wake(wake);
    }
  } catch (...) {
    // Best effort.
  }
}

// Worker entry point for module-backed calls. The module runs on a core-owned
// thread so a slow/hostile module never blocks archoera_call or the event pump.
void module_call_worker(archoera_core* core, uint64_t task_id) noexcept {
  try {
    std::string method;
    std::string params;
    std::string module;
    bool cancel_requested = false;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        return;
      }
      auto it = core->tasks.find(task_id);
      if (it == core->tasks.end()) {
        return;
      }
      method = it->second.method;
      params = it->second.params;
      module = it->second.module;
      cancel_requested = it->second.cancel_requested;
    }

    if (cancel_requested) {
      // Cancel beat the module call; the module never saw this task id, so
      // there will be no done callback to wait for.
      settle_module_cancelled(core, task_id);
      return;
    }

    ModuleRegistry* registry = core->module_registry.get();
    if (registry == nullptr || !registry->loaded(module)) {
      fail_module_task(core, task_id, ARCHOERA_E_UNSUPPORTED,
                       kMsgModuleUnavailable);
      return;
    }

    auto ctx = std::make_shared<ModuleCallContext>();
    ctx->core = core;
    ctx->task_id = task_id;

    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        return;
      }
      core->module_calls[task_id] = ctx;
    }

    const int32_t status = registry->call(
        module, task_id, method.c_str(), params.c_str(), &module_done_callback,
        &module_progress_callback, ctx.get());
    if (status != ARCHOERA_OK) {
      // Contract: a non-OK call was not accepted, so done will never arrive.
      {
        std::lock_guard<std::mutex> lock(core->mutex);
        auto it = core->module_calls.find(task_id);
        if (it != core->module_calls.end() && it->second == ctx) {
          core->module_calls.erase(it);
        }
      }
      fail_module_task(core, task_id, status, kMsgModuleRejected);
      return;
    }

    // Close the cancel race where archoera_cancel ran between the check above
    // and the module registering the task in `call`. Now the module knows the
    // id, so forwarding cancel guarantees it will eventually call done.
    bool cancel_after_call = false;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      auto it = core->tasks.find(task_id);
      if (it != core->tasks.end() && !is_terminal(it->second.state)) {
        cancel_after_call = it->second.cancel_requested;
      }
    }
    if (cancel_after_call) {
      registry->cancel(module, task_id);
    }
  } catch (...) {
    fail_module_task(core, task_id, ARCHOERA_E_INTERNAL, kMsgInternal);
  }
}

archoera_status start_module_call(archoera_core* core,
                                  std::string_view module,
                                  const char* method,
                                  const char* params_json,
                                  uint64_t* out_task_id,
                                  archoera_error* err) {
  if (core->module_registry == nullptr || !core->module_registry->loaded(module)) {
    set_error(err, ARCHOERA_E_UNSUPPORTED, kMsgModuleUnavailable);
    return ARCHOERA_E_UNSUPPORTED;
  }

  try {
    const std::string method_name(method);
    const std::string params =
        (params_json != nullptr && params_json[0] != '\0')
            ? std::string(params_json)
            : std::string("{}");
    uint64_t task_id = 0;

    {
      std::lock_guard<std::mutex> lock(core->mutex);
      if (core->shutting_down.load(std::memory_order_acquire)) {
        set_error(err, ARCHOERA_E_INVALID, kMsgShuttingDown);
        return ARCHOERA_E_INVALID;
      }

      task_id = core->next_task_id++;
      TaskRecord task;
      task.id = task_id;
      task.method = method_name;
      task.params = params;
      task.state = ARCHOERA_TASK_RUNNING;
      task.async = true;
      task.module_backed = true;
      task.module = std::string(module);

      auto inserted = core->tasks.emplace(task_id, std::move(task));
      try {
        core->workers.emplace_back(&module_call_worker, core, task_id);
      } catch (...) {
        core->tasks.erase(inserted.first);
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

}  // namespace

extern "C" {

ARCHOERA_API uint32_t archoera_abi_version(void) {
  return ARCHOERA_ABI_VERSION;
}

ARCHOERA_API const char* archoera_build_info(void) {
  return kBuildInfo;
}

ARCHOERA_API archoera_status archoera_init(const char* config_json,
                                           archoera_core** out,
                                           archoera_error* err) {
  if (out == nullptr) {
    set_error(err, ARCHOERA_E_INVALID, kMsgNullOutput,
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
    core->module_registry = std::make_unique<ModuleRegistry>();
    core->module_registry->load();

    {
      std::lock_guard<std::mutex> lock(g_live_mutex);
      g_live_cores.insert(core.get());
    }

    clear_error(err);
    *out = core.release();
    return ARCHOERA_OK;
  } catch (const std::bad_alloc&) {
    set_error(err, ARCHOERA_E_INTERNAL, kMsgOutOfMemory);
    return ARCHOERA_E_INTERNAL;
  } catch (...) {
    set_error(err, ARCHOERA_E_INTERNAL, kMsgInternal);
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
    std::vector<std::thread> workers;
    std::vector<std::pair<std::string, uint64_t>> module_tasks;
    {
      std::lock_guard<std::mutex> lock(core->mutex);
      core->shutting_down.store(true, std::memory_order_release);
      for (auto& entry : core->tasks) {
        TaskRecord& task = entry.second;
        if (task.async && !is_terminal(task.state)) {
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

    for (std::thread& worker : workers) {
      if (worker.joinable()) {
        worker.join();
      }
    }

    // The appearance watcher may call publish_appearance from an OS thread;
    // stop() joins it before the core and its event queue are destroyed.
    if (core->appearance != nullptr) {
      core->appearance->stop();
    }

    // The status sampler pushes domain events; join it before the queue dies.
    stop_status_watch(*core);

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
                                   int32_t max) {
  if (core == nullptr || out == nullptr || max <= 0) {
    return -1;
  }

  try {
    std::lock_guard<std::mutex> lock(core->mutex);

    // Any pointers returned by the previous poll become invalid here.
    core->poll_payloads.clear();

    const int32_t limit = std::min<int32_t>(max, kMaxPollBatch);
    int32_t count = 0;

    while (count < limit && !core->events.empty()) {
      EventRecord event = std::move(core->events.front());
      core->events.pop_front();

      core->poll_payloads.push_back(std::move(event.json));
      const std::string& payload = core->poll_payloads.back();

      archoera_event& dst = out[count];
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

      ++count;
    }

    return count;
  } catch (...) {
    return -1;
  }
}

void ensure_appearance_started(archoera_core& core) noexcept {
  try {
    archoera::PlatformAppearanceWatcher* watcher = nullptr;
    {
      std::lock_guard<std::mutex> lock(core.mutex);
      if (!core.appearance_started) {
        core.appearance_started = true;
        core.appearance = archoera::create_platform_appearance();
      }
      watcher = core.appearance.get();
    }
    if (watcher != nullptr) {
      watcher->start(&core, &appearance_callback);
    }
  } catch (...) {
    // A missing backend only means Dart falls back to its platform brightness.
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
    set_error(err, ARCHOERA_E_INVALID, kMsgNullCore);
    return ARCHOERA_E_INVALID;
  }
  if (method == nullptr || method[0] == '\0') {
    set_error(err, ARCHOERA_E_INVALID, kMsgInvalidMethod);
    return ARCHOERA_E_INVALID;
  }
  if (out_task_id == nullptr) {
    set_error(err, ARCHOERA_E_INVALID, kMsgNullOutput,
              "archoera_call requires a non-null task id output");
    return ARCHOERA_E_INVALID;
  }

  const char* raw_params = params_json != nullptr ? params_json : "{}";

  try {
    const std::string method_name(method);
    const MethodRoute* route = find_method_route(method_name);
    if (route == nullptr) {
      set_error(err, ARCHOERA_E_UNSUPPORTED, kMsgUnknownMethod);
      return ARCHOERA_E_UNSUPPORTED;
    }

    // Sync core methods fill this instead of breaking into the switch's
    // result builder below (watch/unwatch need their parsed parameters).
    std::string early_result;
    bool has_early_result = false;

    switch (route->kind) {
      case MethodRouteKind::kCorePingAsync:
        return start_ping_async(core, out_task_id, err);
      case MethodRouteKind::kModule:
        return start_module_call(core, route->module, method_name.c_str(),
                                 raw_params, out_task_id, err);
      case MethodRouteKind::kCorePing:
      case MethodRouteKind::kCoreInfo:
      case MethodRouteKind::kCoreModules:
        break;
      case MethodRouteKind::kCoreAppearance:
        ensure_appearance_started(*core);
        break;
      case MethodRouteKind::kCoreOpenUrl: {
        SystemAction action;
        action.kind = SystemActionKind::kOpenUrl;
        action.method = method_name;
        if (!extract_json_string(raw_params, "url", &action.url) ||
            !archoera::is_safe_external_url(action.url)) {
          set_error(err, ARCHOERA_E_INVALID, "system.openUrl requires http(s)");
          return ARCHOERA_E_INVALID;
        }
        return start_system_action(core, std::move(action), out_task_id, err);
      }
      case MethodRouteKind::kCoreOpenPath: {
        SystemAction action;
        action.kind = SystemActionKind::kOpenPath;
        action.method = method_name;
        if (!extract_json_string(raw_params, "path", &action.path) ||
            !archoera::is_safe_directory_path(action.path)) {
          set_error(err, ARCHOERA_E_INVALID,
                    "system.openPath requires an absolute existing directory");
          return ARCHOERA_E_INVALID;
        }
        return start_system_action(core, std::move(action), out_task_id, err);
      }
      case MethodRouteKind::kCoreNotify: {
        SystemAction action;
        action.kind = SystemActionKind::kNotify;
        action.method = method_name;
        extract_json_string(raw_params, "title", &action.title);
        extract_json_string(raw_params, "body", &action.body);
        if (action.title.empty() && action.body.empty()) {
          set_error(err, ARCHOERA_E_INVALID,
                    "system.notify requires a title or body");
          return ARCHOERA_E_INVALID;
        }
        if (action.title.size() > 200 || action.body.size() > 2000) {
          set_error(err, ARCHOERA_E_INVALID,
                    "system.notify payload is too large");
          return ARCHOERA_E_INVALID;
        }
        return start_system_action(core, std::move(action), out_task_id, err);
      }
      case MethodRouteKind::kCoreStatus: {
        std::string path;
        extract_json_string(raw_params, "path", &path);
        return start_status_call(core, method_name, std::move(path),
                                 out_task_id, err);
      }
      case MethodRouteKind::kCoreStatusWatch: {
        long long interval = 5000;
        extract_json_int(raw_params, "intervalMs", &interval);
        if (interval < 1000) interval = 1000;
        if (interval > 60000) interval = 60000;
        std::string path;
        extract_json_string(raw_params, "path", &path);
        if (!start_status_watch(*core, std::chrono::milliseconds(interval),
                                std::move(path))) {
          set_error(err, ARCHOERA_E_INVALID, kMsgShuttingDown);
          return ARCHOERA_E_INVALID;
        }
        early_result = "{\"ok\":true,\"data\":{\"watching\":true,"
                       "\"intervalMs\":" +
                       std::to_string(interval) +
                       "},\"meta\":{\"schemaVersion\":1}}";
        has_early_result = true;
        break;
      }
      case MethodRouteKind::kCoreStatusUnwatch:
        stop_status_watch(*core);
        early_result =
            "{\"ok\":true,\"data\":{\"watching\":false},"
            "\"meta\":{\"schemaVersion\":1}}";
        has_early_result = true;
        break;
    }

    std::string result;
    if (has_early_result) {
      result = std::move(early_result);
    } else if (route->kind == MethodRouteKind::kCorePing) {
      result = kPingResult;
    } else if (route->kind == MethodRouteKind::kCoreInfo) {
      result = make_info_result();
    } else if (route->kind == MethodRouteKind::kCoreModules) {
      result = make_modules_result(*core);
    } else {
      archoera::SystemAppearance state;
      if (core->appearance != nullptr) {
        state = core->appearance->current();
      }
      result = make_appearance_result(state);
    }

    WakeTarget wake;
    uint64_t task_id = 0;

    {
      std::lock_guard<std::mutex> lock(core->mutex);

      task_id = core->next_task_id++;
      TaskRecord task;
      task.id = task_id;
      task.method = method_name;
      task.state = ARCHOERA_TASK_COMPLETED;
      task.result = std::move(result);

      auto inserted = core->tasks.emplace(task_id, std::move(task));
      try {
        EventRecord event;
        event.type = ARCHOERA_EV_TASK_STATE;
        event.state = ARCHOERA_TASK_COMPLETED;
        event.task_id = task_id;
        event.json = make_state_json(method_name, "completed", "task completed");
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

ARCHOERA_API archoera_status archoera_cancel(archoera_core* core,
                                             uint64_t task_id) {
  if (core == nullptr) {
    return ARCHOERA_E_INVALID;
  }
  if (task_id == 0) {
    return ARCHOERA_E_NOT_FOUND;
  }

  try {
    WakeTarget wake;
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

      TaskRecord& task = it->second;
      if (is_terminal(task.state)) {
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
        task.state = ARCHOERA_TASK_CANCELLED;
        task.result.clear();

        EventRecord event;
        event.type = ARCHOERA_EV_TASK_STATE;
        event.state = ARCHOERA_TASK_CANCELLED;
        event.task_id = task_id;
        event.json =
            make_state_json(task.method, "cancelled", "task cancelled");
        enqueue_event_locked(*core, std::move(event));

        wake = wake_target_locked(*core);
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
      notify_wake(wake);
    }
    return ARCHOERA_OK;
  } catch (...) {
    return ARCHOERA_E_INTERNAL;
  }
}

ARCHOERA_API archoera_status archoera_result(archoera_core* core,
                                             uint64_t task_id,
                                             char* buf,
                                             size_t buf_len,
                                             size_t* out_len,
                                             archoera_error* err) {
  if (out_len == nullptr) {
    set_error(err, ARCHOERA_E_INVALID, kMsgNullOutput,
              "archoera_result requires a non-null length output");
    return ARCHOERA_E_INVALID;
  }
  *out_len = 0;

  if (core == nullptr) {
    set_error(err, ARCHOERA_E_INVALID, kMsgNullCore);
    return ARCHOERA_E_INVALID;
  }
  if (task_id == 0) {
    set_error(err, ARCHOERA_E_NOT_FOUND, kMsgTaskNotFound);
    return ARCHOERA_E_NOT_FOUND;
  }

  try {
    std::lock_guard<std::mutex> lock(core->mutex);

    auto it = core->tasks.find(task_id);
    if (it == core->tasks.end()) {
      set_error(err, ARCHOERA_E_NOT_FOUND, kMsgTaskNotFound);
      return ARCHOERA_E_NOT_FOUND;
    }

    const TaskRecord& task = it->second;
    if (!is_terminal(task.state)) {
      set_error(err, ARCHOERA_E_BUSY, kMsgTaskBusy);
      return ARCHOERA_E_BUSY;
    }

    const size_t required = task.result.size() + 1;
    *out_len = required;

    // Two-phase read: the caller asks for the required size first.
    if (buf == nullptr || buf_len == 0) {
      clear_error(err);
      return ARCHOERA_OK;
    }

    if (buf_len < required) {
      set_error(err, ARCHOERA_E_BUFFER, kMsgBufferTooSmall);
      return ARCHOERA_E_BUFFER;
    }

    if (!task.result.empty()) {
      std::memcpy(buf, task.result.data(), task.result.size());
    }
    buf[task.result.size()] = '\0';

    clear_error(err);
    return ARCHOERA_OK;
  } catch (...) {
    set_error(err, ARCHOERA_E_INTERNAL, kMsgInternal);
    return ARCHOERA_E_INTERNAL;
  }
}

ARCHOERA_API void archoera_release(archoera_core* core, uint64_t task_id) {
  if (core == nullptr) {
    return;
  }

  try {
    std::lock_guard<std::mutex> lock(core->mutex);
    core->tasks.erase(task_id);
  } catch (...) {
    // Never let C++ exceptions escape the ABI.
  }
}

}  // extern "C"
