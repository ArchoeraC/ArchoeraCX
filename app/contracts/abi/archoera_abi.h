/*
 * ArchoeraCX core ABI v1 (minimal vertical slice)
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * This header is the only C boundary between Dart and the native core.
 * Keep it in sync with docs/10-architecture-design.md §6 and the C harness.
 */
#pragma once

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define ARCHOERA_ABI_VERSION 1u

typedef struct archoera_core archoera_core;

typedef enum {
  ARCHOERA_OK = 0,
  ARCHOERA_E_INVALID = 1,
  ARCHOERA_E_NOT_FOUND = 2,
  ARCHOERA_E_AUTH = 3,
  ARCHOERA_E_NETWORK = 4,
  ARCHOERA_E_DISK = 5,
  ARCHOERA_E_CANCELLED = 6,
  ARCHOERA_E_UNSUPPORTED = 7,
  ARCHOERA_E_INTERNAL = 8,
  ARCHOERA_E_BUFFER = 9,
  ARCHOERA_E_BUSY = 10,
  ARCHOERA_E_POLICY = 11
} archoera_status;

typedef struct {
  int32_t code;
  const char* message;
  const char* detail;
} archoera_error;

typedef enum {
  ARCHOERA_TASK_QUEUED = 0,
  ARCHOERA_TASK_RUNNING = 1,
  ARCHOERA_TASK_WAITING = 2,
  ARCHOERA_TASK_COMPLETED = 3,
  ARCHOERA_TASK_FAILED = 4,
  ARCHOERA_TASK_CANCELLED = 5
} archoera_task_state;

typedef enum {
  ARCHOERA_EV_TASK_STATE = 1,
  ARCHOERA_EV_TASK_PROGRESS = 2,
  ARCHOERA_EV_LOG = 3,
  ARCHOERA_EV_DOMAIN = 4,
  ARCHOERA_EV_MODULE = 5
} archoera_event_type;

typedef struct {
  int32_t type;
  int32_t state;
  int32_t code;
  uint32_t flags;
  uint64_t seq;
  uint64_t task_id;
  int64_t done;
  int64_t total;
  int64_t bytes_per_sec;
  const char* json;
} archoera_event;

uint32_t archoera_abi_version(void);
const char* archoera_build_info(void);

archoera_status archoera_init(const char* config_json,
                              archoera_core** out,
                              archoera_error* err);

void archoera_shutdown(archoera_core* core);

typedef void (*archoera_wake_fn)(void* user);

archoera_status archoera_set_wakeup(archoera_core* core,
                                    archoera_wake_fn fn,
                                    void* user);

int32_t archoera_poll(archoera_core* core,
                      archoera_event* out,
                      int32_t max);

archoera_status archoera_call(archoera_core* core,
                              const char* method,
                              const char* params_json,
                              uint64_t* out_task_id,
                              archoera_error* err);

archoera_status archoera_cancel(archoera_core* core, uint64_t task_id);

archoera_status archoera_result(archoera_core* core,
                                uint64_t task_id,
                                char* buf,
                                size_t buf_len,
                                size_t* out_len,
                                archoera_error* err);

void archoera_release(archoera_core* core, uint64_t task_id);

#ifdef __cplusplus
}
#endif
