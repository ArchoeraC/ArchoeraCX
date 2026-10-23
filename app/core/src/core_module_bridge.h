/*
 * Module-backed call glue (java.* / loader.* / resource.* / download.*).
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * One handler serves every routed module row: it starts the module call on a
 * core worker thread; the module's done/progress callbacks settle through the
 * shared task primitives in core_tasks.
 */

#pragma once

#include "method_registry.h"

struct archoera_core;

namespace archoera {

archoera_status handle_module_method(archoera_core& core,
                                     const MethodRoute& route,
                                     std::string_view params,
                                     std::string& result,
                                     uint64_t* out_task_id,
                                     archoera_error* err);

}  // namespace archoera
