/*
 * Explicit method routing table for ArchoeraCX.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * The dispatcher is the only place that maps a public method name to a
 * handler. Every row carries its handler function (plus the module name for
 * forwarded methods), so archoera_call resolves a method by a single table
 * lookup and one handler call. Adding a method means adding the handler and
 * one row here; no dispatch if-chain or switch ever grows.
 */

#pragma once

#include "archoera_abi.h"

#include <span>
#include <string>
#include <string_view>

struct archoera_core;  /* Global opaque handle; defined by core_internal.h. */

namespace archoera {

struct MethodRoute;

/*
 * Signature shared by every method handler.
 *
 * Synchronous handlers fill `result` with the complete envelope and leave
 * *out_task_id untouched (archoera_call then registers the COMPLETED task and
 * event). Asynchronous handlers start the task and write *out_task_id,
 * leaving `result` empty. A returned non-OK status has already filled `err`.
 */
using CoreMethodFn = archoera_status (*)(archoera_core& core,
                                         const MethodRoute& route,
                                         std::string_view params,
                                         std::string& result,
                                         uint64_t* out_task_id,
                                         archoera_error* err);

struct MethodRoute {
  std::string_view name;
  CoreMethodFn handler;
  /* Module file stem that owns the method as registered in
   * ModuleMethodRegistry; empty for core-owned methods. */
  std::string_view module;
};

/* Returns nullptr for unknown methods. */
const MethodRoute* find_method_route(std::string_view method) noexcept;

/* The full routing table, used by result builders that enumerate methods. */
std::span<const MethodRoute> method_routes() noexcept;

}  // namespace archoera
