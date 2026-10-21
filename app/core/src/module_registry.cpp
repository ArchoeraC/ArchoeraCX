/*
 * ArchoeraCX native module registry.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "module_registry.h"

#include "module_host.h"

namespace archoera {

ModuleRegistry::ModuleRegistry()
    : cs_(std::make_unique<ModuleHost>("archoera_cs", "ARCHOERACX_CS_MODULE")),
      dl_(std::make_unique<ModuleHost>("archoera_dl", "ARCHOERACX_DL_MODULE")) {}

ModuleRegistry::~ModuleRegistry() {
  shutdown();
}

bool ModuleRegistry::load() noexcept {
  try {
    // C# business module is currently required for module-backed methods, but
    // a missing module still leaves system.* usable for diagnostics.
    cs_->load();
    // Downloader module is optional today; it will reject methods until the
    // Rust cdylib lands. Loading failure is intentionally non-fatal.
    dl_->load();
    return cs_->loaded() || dl_->loaded();
  } catch (...) {
    return false;
  }
}

ModuleHost* ModuleRegistry::find(std::string_view name) const noexcept {
  if (cs_ != nullptr && cs_->loaded() &&
      (cs_->name() == name || cs_->base_name() == name)) {
    return cs_.get();
  }
  if (dl_ != nullptr && dl_->loaded() &&
      (dl_->name() == name || dl_->base_name() == name)) {
    return dl_.get();
  }
  return nullptr;
}

bool ModuleRegistry::loaded(std::string_view name) const noexcept {
  return find(name) != nullptr;
}

bool ModuleRegistry::any_loaded() const noexcept {
  return (cs_ != nullptr && cs_->loaded()) || (dl_ != nullptr && dl_->loaded());
}

std::vector<ModuleInfo> ModuleRegistry::modules() const {
  std::vector<ModuleInfo> result;
  if (cs_ != nullptr && cs_->loaded()) {
    result.push_back({cs_->name(), cs_->version()});
  }
  if (dl_ != nullptr && dl_->loaded()) {
    result.push_back({dl_->name(), dl_->version()});
  }
  return result;
}

int32_t ModuleRegistry::call(std::string_view module,
                             uint64_t task_id,
                             const char* method,
                             const char* params_json,
                             archoera_module_done_fn done,
                             void* user) noexcept {
  try {
    ModuleHost* host = find(module);
    if (host == nullptr) {
      return ARCHOERA_E_UNSUPPORTED;
    }
    return host->call(task_id, method, params_json, done, user);
  } catch (...) {
    return ARCHOERA_E_INTERNAL;
  }
}

int32_t ModuleRegistry::cancel(std::string_view module, uint64_t task_id) noexcept {
  try {
    ModuleHost* host = find(module);
    if (host == nullptr) {
      return ARCHOERA_E_UNSUPPORTED;
    }
    return host->cancel(task_id);
  } catch (...) {
    return ARCHOERA_E_INTERNAL;
  }
}

void ModuleRegistry::shutdown() noexcept {
  try {
    if (cs_ != nullptr) {
      cs_->shutdown();
    }
    if (dl_ != nullptr) {
      dl_->shutdown();
    }
  } catch (...) {
    // A module must not throw through the registry boundary.
  }
}

}  // namespace archoera
