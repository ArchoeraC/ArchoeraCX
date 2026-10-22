/*
 * Minimal native module host for ArchoeraCX.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "module_host.h"

#include <cstdlib>
#include <filesystem>
#include <string>
#include <vector>

#if defined(_WIN32)
#  include <windows.h>
#else
#  include <dlfcn.h>
#endif

namespace archoera {
namespace {

namespace fs = std::filesystem;

constexpr const char* kEnvDataDir = "ARCHOERACX_DATA_DIR";

std::string module_file_name(const std::string& base) {
#if defined(_WIN32)
  return base + ".dll";
#elif defined(__APPLE__)
  return base + ".dylib";
#else
  return base + ".so";
#endif
}

std::string lib_module_file_name(const std::string& base) {
#if defined(_WIN32)
  return module_file_name(base);
#else
  return "lib" + module_file_name(base);
#endif
}

std::string get_env(const char* name) {
  const char* value = std::getenv(name);
  return value != nullptr ? std::string(value) : std::string();
}

#if defined(_WIN32)
std::wstring utf8_to_wide(const std::string& text) {
  if (text.empty()) {
    return std::wstring();
  }
  const int size =
      MultiByteToWideChar(CP_UTF8, 0, text.data(),
                          static_cast<int>(text.size()), nullptr, 0);
  if (size <= 0) {
    return std::wstring();
  }
  std::wstring wide(static_cast<size_t>(size), L'\0');
  MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
                      wide.data(), size);
  return wide;
}
#endif

void append_json_escaped(std::string& out, const std::string& text) {
  for (const char ch : text) {
    switch (ch) {
      case '\\':
        out.append("\\\\");
        break;
      case '"':
        out.append("\\\"");
        break;
      case '\n':
        out.append("\\n");
        break;
      case '\r':
        out.append("\\r");
        break;
      case '\t':
        out.append("\\t");
        break;
      default:
        out.push_back(ch);
        break;
    }
  }
}

std::string make_module_config() {
  // The host treats the module config as opaque data. dataDir is currently
  // derived from the environment; an empty value is valid for the smoke
  // tests and for development runs that do not need persistent storage.
  std::string json = "{\"schemaVersion\":1,\"appVersion\":\"0.0.1+1\",\"dataDir\":\"";
  append_json_escaped(json, get_env(kEnvDataDir));
  json.append("\"}");
  return json;
}

std::string core_library_dir() {
#if defined(_WIN32)
  HMODULE module = nullptr;
  const auto address = reinterpret_cast<LPCWSTR>(
      reinterpret_cast<uintptr_t>(&archoera_abi_version));
  if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                             GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                         address, &module) == 0 ||
      module == nullptr) {
    return std::string();
  }

  std::wstring buffer(MAX_PATH, L'\0');
  for (;;) {
    const DWORD length =
        GetModuleFileNameW(module, buffer.data(),
                           static_cast<DWORD>(buffer.size()));
    if (length == 0) {
      return std::string();
    }
    if (length < buffer.size()) {
      buffer.resize(length);
      break;
    }
    buffer.resize(buffer.size() * 2);
  }

  const fs::path path(buffer);
  const std::wstring parent = path.parent_path().wstring();
  if (parent.empty()) {
    return std::string();
  }
  const int size = WideCharToMultiByte(CP_UTF8, 0, parent.data(),
                                       static_cast<int>(parent.size()), nullptr,
                                       0, nullptr, nullptr);
  if (size <= 0) {
    return std::string();
  }
  std::string utf8(static_cast<size_t>(size), '\0');
  WideCharToMultiByte(CP_UTF8, 0, parent.data(),
                      static_cast<int>(parent.size()), utf8.data(), size,
                      nullptr, nullptr);
  return utf8;
#else
  Dl_info info{};
  if (dladdr(reinterpret_cast<void*>(&archoera_abi_version), &info) == 0 ||
      info.dli_fname == nullptr) {
    return std::string();
  }
  return fs::path(info.dli_fname).parent_path().string();
#endif
}

std::vector<std::string> module_candidates(const std::string& base_name,
                                           const std::string& env_name) {
  std::vector<std::string> candidates;

  const std::string explicit_path = get_env(env_name.c_str());
  if (!explicit_path.empty()) {
    candidates.push_back(explicit_path);
  }

  const std::string dir = core_library_dir();
  if (!dir.empty()) {
    candidates.push_back((fs::path(dir) / module_file_name(base_name)).string());
    candidates.push_back(
        (fs::path(dir) / lib_module_file_name(base_name)).string());
  }

  candidates.emplace_back("./build/native/" + module_file_name(base_name));
  candidates.emplace_back("./build/native/" + lib_module_file_name(base_name));
  candidates.emplace_back("../app/build/native/" + module_file_name(base_name));
  candidates.emplace_back("../app/build/native/" +
                          lib_module_file_name(base_name));
  candidates.emplace_back("./" + module_file_name(base_name));
  candidates.emplace_back("./" + lib_module_file_name(base_name));

  return candidates;
}

void* open_library(const std::string& path) {
#if defined(_WIN32)
  const std::wstring wide = utf8_to_wide(path);
  if (wide.empty()) {
    return nullptr;
  }
  HMODULE module = LoadLibraryExW(
      wide.c_str(), nullptr,
      LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
  if (module == nullptr) {
    module = LoadLibraryW(wide.c_str());
  }
  return reinterpret_cast<void*>(module);
#else
  return dlopen(path.c_str(), RTLD_NOW | RTLD_LOCAL);
#endif
}

void* library_symbol(void* handle, const char* name) {
#if defined(_WIN32)
  return reinterpret_cast<void*>(
      GetProcAddress(static_cast<HMODULE>(handle), name));
#else
  return dlsym(handle, name);
#endif
}

}  // namespace

ModuleHost::ModuleHost(std::string base_name, std::string env_name)
    : base_name_(std::move(base_name)), env_name_(std::move(env_name)) {}

ModuleHost::~ModuleHost() {
  shutdown();
}

bool ModuleHost::load() noexcept {
  try {
    std::lock_guard<std::mutex> lock(mutex_);
    if (closed_) {
      return false;
    }
    if (entry_ != nullptr) {
      return true;
    }

    for (const std::string& candidate :
         module_candidates(base_name_, env_name_)) {
      void* handle = open_library(candidate);
      if (handle == nullptr) {
        continue;
      }

      // From this point on the library stays mapped for the process lifetime,
      // even if the handshake below fails. Unloading code that may have
      // started its runtime already is not safe.
      handle_ = handle;

      using EntryFn = const archoera_module_v1* (*)();
      auto entry_fn = reinterpret_cast<EntryFn>(
          library_symbol(handle, "archoera_module_entry"));
      if (entry_fn == nullptr) {
        return false;
      }

      const archoera_module_v1* entry = entry_fn();
      if (entry == nullptr) {
        return false;
      }
      if (entry->abi_version != ARCHOERA_MODULE_ABI_VERSION) {
        return false;
      }
      if (entry->name == nullptr || entry->name[0] == '\0' ||
          entry->version == nullptr || entry->version[0] == '\0') {
        return false;
      }
      if (entry->init == nullptr || entry->call == nullptr ||
          entry->cancel == nullptr || entry->shutdown == nullptr) {
        return false;
      }

      const std::string config = make_module_config();
      void* self = nullptr;
      archoera_error error = {};
      const int32_t status = entry->init(config.c_str(), &self, &error);
      if (status != ARCHOERA_OK || self == nullptr) {
        return false;
      }

      name_ = entry->name;
      version_ = entry->version;
      entry_ = entry;
      self_ = self;
      return true;
    }

    return false;
  } catch (...) {
    return false;
  }
}

bool ModuleHost::loaded() const noexcept {
  try {
    std::lock_guard<std::mutex> lock(mutex_);
    return entry_ != nullptr && self_ != nullptr && !closed_;
  } catch (...) {
    return false;
  }
}

std::string ModuleHost::name() const {
  try {
    std::lock_guard<std::mutex> lock(mutex_);
    return name_;
  } catch (...) {
    return std::string();
  }
}

std::string ModuleHost::version() const {
  try {
    std::lock_guard<std::mutex> lock(mutex_);
    return version_;
  } catch (...) {
    return std::string();
  }
}

std::string ModuleHost::base_name() const {
  try {
    std::lock_guard<std::mutex> lock(mutex_);
    return base_name_;
  } catch (...) {
    return std::string();
  }
}

int32_t ModuleHost::call(uint64_t task_id,
                         const char* method,
                         const char* params_json,
                         archoera_module_done_fn done,
                         archoera_module_progress_fn progress,
                         void* user) noexcept {
  try {
    // A module call is allowed to run for a long time (for example an OAuth
    // browser flow). Do not hold the host mutex across the call, otherwise a
    // concurrent cancel cannot reach the module and the UI appears hung.
    const archoera_module_v1* entry = nullptr;
    void* self = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (closed_ || entry_ == nullptr || self_ == nullptr) {
        return ARCHOERA_E_UNSUPPORTED;
      }
      entry = entry_;
      self = self_;
    }
    return entry->call(self, task_id, method, params_json, done, progress, user);
  } catch (...) {
    return ARCHOERA_E_INTERNAL;
  }
}

int32_t ModuleHost::cancel(uint64_t task_id) noexcept {
  try {
    // Do not wait for an in-flight module call; cancel must be able to break
    // that call's long-running wait.
    const archoera_module_v1* entry = nullptr;
    void* self = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (closed_ || entry_ == nullptr || self_ == nullptr) {
        return ARCHOERA_E_UNSUPPORTED;
      }
      entry = entry_;
      self = self_;
    }
    return entry->cancel(self, task_id);
  } catch (...) {
    return ARCHOERA_E_INTERNAL;
  }
}

void ModuleHost::shutdown() noexcept {
  try {
    std::lock_guard<std::mutex> lock(mutex_);
    if (closed_) {
      return;
    }
    closed_ = true;
    if (entry_ != nullptr && self_ != nullptr) {
      entry_->shutdown(self_);
    }
  } catch (...) {
    // A module must not throw through the host boundary.
  }
}

}  // namespace archoera
