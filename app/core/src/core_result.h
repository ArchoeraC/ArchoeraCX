/*
 * Envelope and payload construction for core-owned methods.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Every JSON text handed to Dart is assembled here: state-change events,
 * the one-shot info / modules / gpu / appearance / action envelopes and the
 * error envelope stored on failed tasks. The modules result enumerates the
 * routing table instead of carrying a hand-written method list.
 */

#pragma once

#include "core_internal.h"

namespace archoera {

/* Frozen sync envelopes for the two ping probes. */
constexpr const char* kPingResult =
    "{\"ok\":true,\"data\":{\"pong\":true,\"method\":\"system.ping\"},"
    "\"meta\":{\"schemaVersion\":1}}";
constexpr const char* kPingAsyncResult =
    "{\"ok\":true,\"data\":{\"pong\":true,\"method\":\"system.pingAsync\"},"
    "\"meta\":{\"schemaVersion\":1}}";

/* Event payload pieces */
std::string make_state_json(std::string_view method,
                            const char* phase,
                            const char* message);

/* One-shot result envelopes */
std::string make_info_result();
std::string make_modules_result(const archoera_core& core);
std::string make_gpu_result();
std::string make_action_result(const char* field, bool value);
std::string make_open_path_result(const std::string& path, bool opened);
std::string make_appearance_data(const SystemAppearance& state);
std::string make_appearance_result(const SystemAppearance& state);
/* Error envelope stored as a failed task's result. ABI status codes are
 * normalized by the caller; domain detail would live in a future field. */
std::string make_error_result(int32_t code, const char* message);

/* Synchronous method handlers (routing table rows). */
archoera_status handle_system_ping(archoera_core& core,
                                   const MethodRoute& route,
                                   std::string_view params,
                                   std::string& result,
                                   uint64_t* out_task_id,
                                   archoera_error* err);
archoera_status handle_system_info(archoera_core& core,
                                   const MethodRoute& route,
                                   std::string_view params,
                                   std::string& result,
                                   uint64_t* out_task_id,
                                   archoera_error* err);
archoera_status handle_system_modules(archoera_core& core,
                                      const MethodRoute& route,
                                      std::string_view params,
                                      std::string& result,
                                      uint64_t* out_task_id,
                                      archoera_error* err);
archoera_status handle_system_gpu(archoera_core& core,
                                  const MethodRoute& route,
                                  std::string_view params,
                                  std::string& result,
                                  uint64_t* out_task_id,
                                  archoera_error* err);

}  // namespace archoera
