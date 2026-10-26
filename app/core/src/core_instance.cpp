/*
 * platform.singleInstance method.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "core_instance.h"

#include "core_internal.h"
#include "core_tasks.h"
#include "platform_instance.h"

#include <mutex>
#include <string>
#include <utility>

namespace archoera {
namespace {

constexpr std::string_view kInstanceName = "archoera-cx";

std::string make_result(const bool primary, const bool signaled) {
  std::string json =
      "{\"ok\":true,\"data\":{\"primary\":";
  json.append(primary ? "true" : "false");
  json.append(",\"signaled\":");
  json.append(signaled ? "true" : "false");
  json.append("},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

// Runs on the platform listener thread. The core already has an attached event
// pump, and publish_domain_event drops the event once shutdown begins.
void publish_activation(archoera_core* core) noexcept {
  try {
    publish_domain_event(
        core,
        "{\"schemaVersion\":1,\"kind\":\"platform.activate\"}");
  } catch (...) {
    // A missed wake must not escape the listener thread.
  }
}

}  // namespace

archoera_status handle_platform_single_instance(archoera_core& core,
                                                const MethodRoute&,
                                                std::string_view,
                                                std::string& result,
                                                uint64_t*,
                                                archoera_error* err) {
  // Separate from core.mutex: notify() retries a connect for up to 1.5 s and
  // must not block the event pump.
  std::lock_guard<std::mutex> serial(core.instance_mutex);

  if (core.shutting_down.load(std::memory_order_acquire)) {
    set_error(err, ARCHOERA_E_INVALID, kMsgShuttingDown);
    return ARCHOERA_E_INVALID;
  }

  bool primary = false;
  std::string detail;
  if (core.instance == nullptr) {
    auto instance = create_platform_instance(std::string(kInstanceName));
    if (instance == nullptr ||
        !instance->acquire(&primary, &detail)) {
      set_error(err, ARCHOERA_E_INTERNAL, kMsgInstanceUnavailable);
      return ARCHOERA_E_INTERNAL;
    }
    core.instance = std::move(instance);
    core.instance_primary = primary;
  } else {
    primary = core.instance_primary;
  }

  bool signaled = false;
  if (primary) {
    if (!core.instance_listening) {
      archoera_core* core_pointer = &core;
      auto callback = [core_pointer]() { publish_activation(core_pointer); };
      if (!core.instance->listen(std::move(callback), &detail)) {
        // Do not keep a lock that has no listener: later launches would exit
        // without being able to wake this window.
        core.instance->stop();
        core.instance.reset();
        core.instance_primary = false;
        set_error(err, ARCHOERA_E_INTERNAL, kMsgInstanceUnavailable);
        return ARCHOERA_E_INTERNAL;
      }
      core.instance_listening = true;
    }
  } else {
    // A failed forward still means "do not start a second window"; Dart uses
    // primary=false and exits either way.
    signaled = core.instance->notify(&detail);
  }

  result = make_result(primary, signaled);
  return ARCHOERA_OK;
}

}  // namespace archoera
