/*
 * Envelope and payload construction for core-owned methods.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "core_result.h"

#include "core_json.h"
#include "platform_gpu.h"

#include <cstdio>
#include <string>

namespace archoera {

std::string make_state_json(std::string_view method,
                            const char* phase,
                            const char* message) {
  std::string json;
  json.reserve(method.size() + 96);
  json.append("{\"method\":\"");
  json.append(method);
  json.append("\",\"phase\":\"");
  json.append(phase);
  json.append("\",\"message\":\"");
  json.append(message);
  json.append("\"}");
  return json;
}

std::string make_info_result() {
  std::string json;
  json.reserve(256);
  json.append("{\"ok\":true,\"data\":{\"abiVersion\":");
  json.append(std::to_string(ARCHOERA_ABI_VERSION));
  json.append(",\"buildInfo\":");
  json.append(kBuildInfoJson);
  json.append(
      ",\"modules\":[{\"name\":\"system\",\"state\":\"ready\"}]},"
      "\"meta\":{\"schemaVersion\":1}}");
  return json;
}

void append_module_entries(std::string& json, const archoera_core& core) {
  // Core-owned methods come straight from the routing table; forwarded
  // methods are attached to the loaded host that answers their module token.
  const std::span<const MethodRoute> routes = method_routes();

  json.append("{\"name\":\"system\",\"version\":\"");
  append_json_escaped(json, kArchoeraVersion);
  json.append("\",\"state\":\"ready\",\"methods\":[");
  bool first = true;
  for (const MethodRoute& route : routes) {
    if (!route.module.empty()) {
      continue;
    }
    if (!first) {
      json.append(",");
    }
    first = false;
    json.append("\"");
    append_json_escaped(json, route.name);
    json.append("\"");
  }
  json.append("]}");

  if (core.module_registry == nullptr) {
    return;
  }
  for (const LoadedHost& host : core.module_registry->loaded_hosts()) {
    json.append(",{\"name\":\"");
    append_json_escaped(json, host.name);
    json.append("\",\"version\":\"");
    append_json_escaped(json, host.version);
    json.append("\",\"state\":\"ready\",\"methods\":[");
    bool module_first = true;
    for (const MethodRoute& route : routes) {
      if (route.module.empty()) {
        continue;
      }
      if (route.module != host.base_name && route.module != host.name) {
        continue;
      }
      if (!module_first) {
        json.append(",");
      }
      module_first = false;
      json.append("\"");
      append_json_escaped(json, route.name);
      json.append("\"");
    }
    json.append("]}");
  }
}

std::string make_modules_result(const archoera_core& core) {
  std::string json =
      "{\"ok\":true,\"data\":{\"modules\":[";
  append_module_entries(json, core);
  json.append("]},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

std::string make_gpu_result() {
  const GpuStatus& gpu = sample_gpu_status();
  std::string json = "{\"ok\":true,\"data\":{\"platform\":\"";
  append_json_escaped(json, gpu.platform);
  json.append("\",\"primaryVendor\":");
  if (gpu.primary_vendor.empty()) {
    json.append("null");
  } else {
    json.append("\"");
    append_json_escaped(json, gpu.primary_vendor);
    json.append("\"");
  }
  json.append(",\"hybrid\":");
  json.append(gpu.hybrid ? "true" : "false");
  json.append(",\"gpus\":[");
  bool first = true;
  for (const GpuDevice& device : gpu.gpus) {
    if (!first) json.append(",");
    first = false;
    json.append("{\"vendor\":\"");
    append_json_escaped(json, device.vendor);
    json.append("\",\"name\":\"");
    append_json_escaped(json, device.name);
    json.append("\",\"discrete\":");
    json.append(device.discrete ? "true" : "false");
    json.append(",\"driverLoaded\":");
    json.append(device.driver_loaded ? "true" : "false");
    json.append("}");
  }
  json.append("]},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

std::string make_action_result(const char* field, bool value) {
  std::string json = "{\"ok\":true,\"data\":{\"";
  json.append(field);
  json.append("\":");
  json.append(value ? "true" : "false");
  json.append("},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

// system.openPath echoes the directory back so Dart can correlate the result
// without tracking parameters itself.
std::string make_open_path_result(const std::string& path, bool opened) {
  std::string json = "{\"ok\":true,\"data\":{\"path\":\"";
  append_json_escaped(json, path);
  json.append("\",\"opened\":");
  json.append(opened ? "true" : "false");
  json.append("},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

std::string make_appearance_data(const SystemAppearance& state) {
  std::string json = "{\"theme\":\"";
  if (!state.theme_known) {
    json.append("unknown");
  } else {
    json.append(state.dark ? "dark" : "light");
  }
  json.append("\",\"accent\":");
  if (state.accent_known) {
    json.append("{\"r\":");
    json.append(std::to_string(state.accent_r));
    json.append(",\"g\":");
    json.append(std::to_string(state.accent_g));
    json.append(",\"b\":");
    json.append(std::to_string(state.accent_b));
    json.append("}");
  } else {
    json.append("null");
  }
  json.append(",\"source\":\"");
  append_json_escaped(json, state.source);
  json.append("\"}");
  return json;
}

std::string make_appearance_result(const SystemAppearance& state) {
  std::string json = "{\"ok\":true,\"data\":";
  json.append(make_appearance_data(state));
  json.append(",\"meta\":{\"schemaVersion\":1}}");
  return json;
}

std::string make_error_result(int32_t code, const char* message) {
  std::string json;
  json.reserve(160);
  json.append("{\"ok\":false,\"error\":{\"code\":");
  json.append(std::to_string(code));
  json.append(",\"message\":\"");
  append_json_escaped(json,
                      message != nullptr ? message : "module call failed");
  json.append("\"},\"meta\":{\"schemaVersion\":1}}");
  return json;
}

// ---------------------------------------------------------------------------
// Synchronous method handlers
// ---------------------------------------------------------------------------

archoera_status handle_system_ping(archoera_core&,
                                   const MethodRoute&,
                                   std::string_view,
                                   std::string& result,
                                   uint64_t*,
                                   archoera_error*) {
  result = kPingResult;
  return ARCHOERA_OK;
}

archoera_status handle_system_info(archoera_core&,
                                   const MethodRoute&,
                                   std::string_view,
                                   std::string& result,
                                   uint64_t*,
                                   archoera_error*) {
  result = make_info_result();
  return ARCHOERA_OK;
}

archoera_status handle_system_modules(archoera_core& core,
                                      const MethodRoute&,
                                      std::string_view,
                                      std::string& result,
                                      uint64_t*,
                                      archoera_error*) {
  result = make_modules_result(core);
  return ARCHOERA_OK;
}

archoera_status handle_system_gpu(archoera_core&,
                                  const MethodRoute&,
                                  std::string_view,
                                  std::string& result,
                                  uint64_t*,
                                  archoera_error*) {
  result = make_gpu_result();
  return ARCHOERA_OK;
}

}  // namespace archoera
