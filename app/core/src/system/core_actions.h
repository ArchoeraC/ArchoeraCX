/*
 * Native system actions (system.openUrl / system.openPath / system.notify /
 * system.focusMode).
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Handlers validate while the caller is still on the ABI thread, then run
 * the platform call on a core worker thread and settle the task. Parameter
 * document shape and defaults match the platform_* backends.
 */

#pragma once

#include "host/method_registry.h"

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

// Toggles idle-sleep / screen-lock inhibition on the platform backend. The
// parameter document is `{"enabled": <bool>}`; the result echoes whether the
// inhibition is active now plus the backend label for diagnostics.
archoera_status handle_system_focus_mode(archoera_core& core,
                                         const MethodRoute& route,
                                         std::string_view params,
                                         std::string& result,
                                         uint64_t* out_task_id,
                                         archoera_error* err);

// Moves one absolute file or directory to the platform trash. The parameter
// document is `{"path": "<absolute path>"}`; the result echoes the path plus
// whether the item was trashed and the backend label.
archoera_status handle_system_trash(archoera_core& core,
                                    const MethodRoute& route,
                                    std::string_view params,
                                    std::string& result,
                                    uint64_t* out_task_id,
                                    archoera_error* err);

}  // namespace archoera
