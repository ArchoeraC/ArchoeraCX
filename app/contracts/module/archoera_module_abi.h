/*
 * ArchoeraCX module ABI v1 (minimal host slice)
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Native modules (C# NativeAOT, Rust cdylib) expose one function table through
 * `archoera_module_entry`. The C++ hub is the only caller.
 */
#pragma once

#include <stdint.h>
#include "../abi/archoera_abi.h"

#ifdef __cplusplus
extern "C" {
#endif

#define ARCHOERA_MODULE_ABI_VERSION 1u

typedef void (*archoera_module_done_fn)(void* user,
                                        int32_t status,
                                        const char* result_json);

typedef struct archoera_module_v1 {
  uint32_t abi_version;
  const char* name;
  const char* version;

  /* Returns ARCHOERA_OK and a non-null self, or an error and null self. */
  int32_t (*init)(const char* config_json, void** out_self, archoera_error* err);

  /*
   * Returns ARCHOERA_OK if accepted; `done` is then called exactly once,
   * possibly before `call` returns or from another thread.
   * Returns a non-OK status if not accepted; `done` must not be called.
   * result_json is a UTF-8 envelope valid only during the callback.
   */
  int32_t (*call)(void* self,
                  uint64_t task_id,
                  const char* method,
                  const char* params_json,
                  archoera_module_done_fn done,
                  void* user);

  /* Idempotent cooperative cancel; unknown tasks are ignored. */
  int32_t (*cancel)(void* self, uint64_t task_id);

  /* Blocks until callbacks stop. No callback may follow shutdown. */
  void (*shutdown)(void* self);
} archoera_module_v1;

/* Exported symbol name every module must provide. */
const archoera_module_v1* archoera_module_entry(void);

#ifdef __cplusplus
}
#endif
