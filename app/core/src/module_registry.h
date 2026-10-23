/*
 * ArchoeraCX native module registry.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Owns every dynamically loaded native module for the process lifetime. The
 * method registry decides which module name handles a public method; this
 * class resolves that name to the loaded host and forwards call/cancel/shutdown.
 */

#pragma once

#include "archoera_module_abi.h"

#include <cstdint>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace archoera {

class ModuleHost;

/* A loaded host as seen by the method layer: reported name/version plus the
 * module file stem that method routes refer to. */
struct LoadedHost {
  std::string name;
  std::string version;
  std::string base_name;
};

class ModuleRegistry {
 public:
  ModuleRegistry();
  ~ModuleRegistry();

  ModuleRegistry(const ModuleRegistry&) = delete;
  ModuleRegistry& operator=(const ModuleRegistry&) = delete;

  /* Loads the known modules. Missing optional modules do not fail the core. */
  bool load() noexcept;

  bool loaded(std::string_view name) const noexcept;
  bool any_loaded() const noexcept;

  /* Loaded hosts in registration order (list only loaded entries). */
  std::vector<LoadedHost> loaded_hosts() const;

  int32_t call(std::string_view module,
               uint64_t task_id,
               const char* method,
               const char* params_json,
               archoera_module_done_fn done,
               archoera_module_progress_fn progress,
               void* user) noexcept;

  int32_t cancel(std::string_view module, uint64_t task_id) noexcept;

  void shutdown() noexcept;

 private:
  ModuleHost* find(std::string_view name) const noexcept;

  std::unique_ptr<ModuleHost> cs_;
  std::unique_ptr<ModuleHost> dl_;
};

}  // namespace archoera
