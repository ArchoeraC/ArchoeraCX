/*
 * Module-backed call glue (java.* / loader.* / resource.* / download.*).
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "core_module_bridge.h"

#include "core_internal.h"
#include "core_result.h"
#include "core_tasks.h"

#include <string>
#include <string_view>
#include <utility>

namespace archoera {
namespace {

struct ModuleCallPayload {};

// Module progress callback. May run on any module thread; it only enqueues a
// progress event under the core mutex and wakes the Dart listener afterwards.
void module_progress_callback(void* user,
                              const int64_t done,
                              const int64_t total,
                              const int64_t bytes_per_sec,
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
                          const int32_t status,
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
      if (it == core->tasks.end()) {
        return;
      }

      TaskRecord& task = it->second;
      if (status == ARCHOERA_OK && !task.cancel_requested) {
        if (result_json == nullptr) {
          settle_task_locked(*core, task, ARCHOERA_TASK_FAILED,
                             make_error_result(ARCHOERA_E_INTERNAL,
                                               "module returned no result"),
                             status, "module returned no result");
        } else {
          settle_task_locked(*core, task, ARCHOERA_TASK_COMPLETED,
                             std::string(result_json), status, nullptr);
        }
      } else if (status == ARCHOERA_E_CANCELLED || task.cancel_requested) {
        // Cancellation wins even if the module raced to a successful result.
        settle_task_locked(*core, task, ARCHOERA_TASK_CANCELLED, std::string(),
                           status, nullptr);
      } else {
        settle_task_locked(*core, task, ARCHOERA_TASK_FAILED,
                           make_error_result(status, "module call failed"),
                           status, "module call failed");
      }

      if (is_terminal(task.state)) {
        wake = wake_target_locked(*core);
        notify = true;
      }
    }

    if (notify) {
      notify_wake(wake);
    }
  } catch (...) {
    // Exceptions must not cross the module ABI boundary.
  }
}

// Worker entry point for module-backed calls. The module runs on a core-owned
// thread so a slow/hostile module never blocks archoera_call or the event
// pump.
void module_call_worker(archoera_core* core,
                        const uint64_t task_id,
                        ModuleCallPayload*) noexcept {
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
      settle_task_terminal(core, task_id, ARCHOERA_TASK_CANCELLED,
                           std::string(), 0, nullptr);
      return;
    }

    ModuleRegistry* registry = core->module_registry.get();
    if (registry == nullptr || !registry->loaded(module)) {
      settle_task_terminal(
          core, task_id, ARCHOERA_TASK_FAILED,
          make_error_result(ARCHOERA_E_UNSUPPORTED, kMsgModuleUnavailable),
          ARCHOERA_E_UNSUPPORTED, kMsgModuleUnavailable);
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
      settle_task_terminal(
          core, task_id, ARCHOERA_TASK_FAILED,
          make_error_result(status, kMsgModuleRejected), status,
          kMsgModuleRejected);
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
    settle_task_terminal(
        core, task_id, ARCHOERA_TASK_FAILED,
        make_error_result(ARCHOERA_E_INTERNAL, kMsgInternal),
        ARCHOERA_E_INTERNAL, kMsgInternal);
  }
}

}  // namespace

archoera_status handle_module_method(archoera_core& core,
                                     const MethodRoute& route,
                                     const std::string_view params,
                                     std::string&,
                                     uint64_t* out_task_id,
                                     archoera_error* err) {
  if (core.module_registry == nullptr ||
      !core.module_registry->loaded(route.module)) {
    set_error(err, ARCHOERA_E_UNSUPPORTED, kMsgModuleUnavailable);
    return ARCHOERA_E_UNSUPPORTED;
  }

  TaskRecord record;
  record.method = std::string(route.name);
  record.params =
      (params.empty()) ? std::string("{}") : std::string(params);
  record.module_backed = true;
  record.module = std::string(route.module);
  return spawn_async_task<ModuleCallPayload>(
      &core, std::move(record), &module_call_worker, ModuleCallPayload{},
      out_task_id, err);
}

}  // namespace archoera
