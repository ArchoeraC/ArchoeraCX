/*
 * Minimal native module host for ArchoeraCX.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Owns one dynamically loaded native module. ModuleRegistry owns the set of
 * hosts. A host performs the ABI handshake and forwards calls; it deliberately
 * contains no business logic and never unloads a module (dlclose/FreeLibrary
 * would let module-owned threads call back into freed code).
 */

#pragma once

#include "archoera_module_abi.h"

#include <cstdint>
#include <mutex>
#include <string>

namespace archoera {

class ModuleHost {
 public:
  /*
   * `base_name` is the module file stem, for example "archoera_cs". The host
   * appends the platform suffix and probes `env_name` first.
   */
  ModuleHost(std::string base_name, std::string env_name);
  ~ModuleHost();

  ModuleHost(const ModuleHost&) = delete;
  ModuleHost& operator=(const ModuleHost&) = delete;

  /*
   * Locate, load, handshake and initialize the module. Returns false when no
   * candidate can be loaded or the handshake fails; the core keeps running in
   * that case (java.* simply stays unsupported). Never throws.
   *
   * Candidate order:
   *   1. the module-specific environment variable (for example
   *      ARCHOERACX_CS_MODULE)
   *   2. <directory of libarchoera_core.so>/<base>.so (+ lib prefix)
   */
  bool load() noexcept;

  bool loaded() const noexcept;
  std::string name() const;
  std::string version() const;
  std::string base_name() const;

  /*
   * Forward one asynchronous call. A non-OK return means the module did not
   * accept the call and `done` must not be invoked. Thread-safe.
   */
  int32_t call(uint64_t task_id,
               const char* method,
               const char* params_json,
               archoera_module_done_fn done,
               archoera_module_progress_fn progress,
               void* user) noexcept;

  /* Cooperative, idempotent cancel. Safe before/after shutdown. */
  int32_t cancel(uint64_t task_id) noexcept;

  /*
   * Block until the module has stopped all callbacks, then mark the host
   * closed. Idempotent. Must run before the core (and this host) is destroyed.
   */
  void shutdown() noexcept;

 private:
  mutable std::mutex mutex_;
  void* handle_ = nullptr;
  const archoera_module_v1* entry_ = nullptr;
  void* self_ = nullptr;
  bool closed_ = false;
  std::string base_name_;
  std::string env_name_;
  std::string name_;
  std::string version_;
};

}  // namespace archoera
