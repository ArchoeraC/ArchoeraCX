/*
 * platform.singleInstance method.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * The first process takes the per-user lock and starts the activation
 * listener. Later processes forward one activation message, then the Dart
 * entry point exits before creating a second window. Listener wakeups arrive
 * as `platform.activate` domain events, so the UI stays wake-driven.
 */

#pragma once

#include "method_registry.h"

struct archoera_core;

namespace archoera {

archoera_status handle_platform_single_instance(archoera_core& core,
                                                const MethodRoute& route,
                                                std::string_view params,
                                                std::string& result,
                                                uint64_t* out_task_id,
                                                archoera_error* err);

}  // namespace archoera
