/*
 * Native system actions (system.openUrl / system.openPath / system.notify).
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "core_actions.h"

#include "core_internal.h"
#include "core_json.h"
#include "core_result.h"
#include "core_tasks.h"
#include "platform_system.h"

#include <string>
#include <utility>

namespace archoera {
namespace {

enum class SystemActionKind { kOpenUrl, kOpenPath, kNotify };

struct SystemAction {
  SystemActionKind kind = SystemActionKind::kOpenUrl;
  std::string url;
  std::string path;
  std::string title;
  std::string body;
};

std::string make_system_action_result(const SystemAction& action,
                                      const bool success) {
  if (action.kind == SystemActionKind::kOpenPath) {
    return make_open_path_result(action.path, success);
  }
  return make_action_result(
      action.kind == SystemActionKind::kOpenUrl ? "opened" : "sent", success);
}

struct SystemActionPayload {
  SystemAction action;
};

void system_action_worker(archoera_core* core,
                          const uint64_t task_id,
                          SystemActionPayload* payload) noexcept {
  std::unique_ptr<SystemActionPayload> held(payload);
  try {
    bool success = false;
    {
      std::string error;
      try {
        if (held->action.kind == SystemActionKind::kOpenUrl) {
          success = open_external_url(held->action.url, &error);
        } else if (held->action.kind == SystemActionKind::kOpenPath) {
          success = open_path(held->action.path, &error);
        } else {
          success = send_system_notification(held->action.title,
                                             held->action.body, &error);
        }
      } catch (...) {
        success = false;
      }
    }

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
        settle_task_locked(*core, task, ARCHOERA_TASK_COMPLETED,
                           make_system_action_result(held->action, success),
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

}  // namespace

archoera_status handle_system_open_url(archoera_core& core,
                                       const MethodRoute& route,
                                       const std::string_view params,
                                       std::string&,
                                       uint64_t* out_task_id,
                                       archoera_error* err) {
  SystemAction action;
  action.kind = SystemActionKind::kOpenUrl;
  if (!extract_json_string(params, "url", &action.url) ||
      !is_safe_external_url(action.url)) {
    set_error(err, ARCHOERA_E_INVALID, kMsgOpenUrlInvalid);
    return ARCHOERA_E_INVALID;
  }

  TaskRecord record;
  record.method = std::string(route.name);
  return spawn_async_task<SystemActionPayload>(
      &core, std::move(record), &system_action_worker,
      SystemActionPayload{std::move(action)}, out_task_id, err);
}

archoera_status handle_system_open_path(archoera_core& core,
                                        const MethodRoute& route,
                                        const std::string_view params,
                                        std::string&,
                                        uint64_t* out_task_id,
                                        archoera_error* err) {
  SystemAction action;
  action.kind = SystemActionKind::kOpenPath;
  if (!extract_json_string(params, "path", &action.path) ||
      !is_safe_directory_path(action.path)) {
    set_error(err, ARCHOERA_E_INVALID, kMsgOpenPathInvalid);
    return ARCHOERA_E_INVALID;
  }

  TaskRecord record;
  record.method = std::string(route.name);
  return spawn_async_task<SystemActionPayload>(
      &core, std::move(record), &system_action_worker,
      SystemActionPayload{std::move(action)}, out_task_id, err);
}

archoera_status handle_system_notify(archoera_core& core,
                                     const MethodRoute& route,
                                     const std::string_view params,
                                     std::string&,
                                     uint64_t* out_task_id,
                                     archoera_error* err) {
  SystemAction action;
  action.kind = SystemActionKind::kNotify;
  extract_json_string(params, "title", &action.title);
  extract_json_string(params, "body", &action.body);
  if (action.title.empty() && action.body.empty()) {
    set_error(err, ARCHOERA_E_INVALID, kMsgNotifyNeedsPayload);
    return ARCHOERA_E_INVALID;
  }
  if (action.title.size() > 200 || action.body.size() > 2000) {
    set_error(err, ARCHOERA_E_INVALID, kMsgNotifyTooLarge);
    return ARCHOERA_E_INVALID;
  }

  TaskRecord record;
  record.method = std::string(route.name);
  return spawn_async_task<SystemActionPayload>(
      &core, std::move(record), &system_action_worker,
      SystemActionPayload{std::move(action)}, out_task_id, err);
}

}  // namespace archoera
