/*
 * System appearance sync method (system.appearance).
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "core_appearance.h"

#include "core_internal.h"
#include "core_result.h"
#include "core_tasks.h"
#include "platform_appearance.h"

#include <string>
#include <utility>

namespace archoera {

// Publishes an appearance change as a domain event. The event is not tied to
// a task, so task_id stays 0 and Dart routes it by payload kind.
void publish_appearance(archoera_core* core,
                        const SystemAppearance& state) noexcept {
  try {
    std::string json =
        "{\"schemaVersion\":1,\"kind\":\"system.appearance\",";
    const std::string data = make_appearance_data(state);
    // Reuse the data object's members without the outer braces.
    json.append(data.substr(1, data.size() - 2));
    json.append("}");
    publish_domain_event(core, std::move(json));
  } catch (...) {
    // Appearance notifications are cosmetic; a failure must not escape.
  }
}

void appearance_callback(void* user,
                         const SystemAppearance& state) noexcept {
  if (user == nullptr) {
    return;
  }
  publish_appearance(static_cast<archoera_core*>(user), state);
}

}  // namespace archoera

namespace {

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
      watcher->start(&core, &archoera::appearance_callback);
    }
  } catch (...) {
    // A missing backend only means Dart falls back to its platform brightness.
  }
}

}  // namespace

namespace archoera {

archoera_status handle_system_appearance(archoera_core& core,
                                         const MethodRoute&,
                                         std::string_view,
                                         std::string& result,
                                         uint64_t*,
                                         archoera_error*) {
  ensure_appearance_started(core);
  SystemAppearance state;
  if (core.appearance != nullptr) {
    state = core.appearance->current();
  }
  result = make_appearance_result(state);
  return ARCHOERA_OK;
}

}  // namespace archoera
