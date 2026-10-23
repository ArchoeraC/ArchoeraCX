/*
 * System appearance sync method (system.appearance).
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#pragma once

#include "method_registry.h"

struct archoera_core;

namespace archoera {

archoera_status handle_system_appearance(archoera_core& core,
                                         const MethodRoute& route,
                                         std::string_view params,
                                         std::string& result,
                                         uint64_t* out_task_id,
                                         archoera_error* err);

}  // namespace archoera
