/*
 * Native system actions (system.openUrl / system.openPath / system.notify).
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Handlers validate while the caller is still on the ABI thread, then run
 * the platform call on a core worker thread and settle the task. Parameter
 * document shape and defaults match the platform_* backends.
 */

#pragma once

#include "method_registry.h"

struct archoera_core;

namespace archoera {

archoera_status handle_system_open_url(archoera_core& core,
                                       const MethodRoute& route,
                                       std::string_view params,
                                       std::string& result,
                                       uint64_t* out_task_id,
                                       archoera_error* err);

archoera_status handle_system_open_path(archoera_core& core,
                                        const MethodRoute& route,
                                        std::string_view params,
                                        std::string& result,
                                        uint64_t* out_task_id,
                                        archoera_error* err);

archoera_status handle_system_reveal_path(archoera_core& core,
                                          const MethodRoute& route,
                                          std::string_view params,
                                          std::string& result,
                                          uint64_t* out_task_id,
                                          archoera_error* err);

archoera_status handle_system_notify(archoera_core& core,
                                     const MethodRoute& route,
                                     std::string_view params,
                                     std::string& result,
                                     uint64_t* out_task_id,
                                     archoera_error* err);

}  // namespace archoera
