/*
 * ArchoeraCX method registry.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "method_registry.h"

#include "core_actions.h"
#include "core_appearance.h"
#include "core_module_bridge.h"
#include "core_result.h"
#include "core_status.h"
#include "core_tasks.h"

namespace archoera {
namespace {

constexpr std::string_view kCsModule = "archoera_cs";

/*
 * Stable public method table. C++ owns system.*; every other row is forwarded
 * to a named module. The module still validates parameters and owns all
 * business semantics.
 */
constexpr MethodRoute kRoutes[] = {
    {"system.ping", &handle_system_ping, {}},
    {"system.pingAsync", &handle_system_ping_async, {}},
    {"system.info", &handle_system_info, {}},
    {"system.modules", &handle_system_modules, {}},
    {"system.appearance", &handle_system_appearance, {}},
    {"system.openUrl", &handle_system_open_url, {}},
    {"system.openPath", &handle_system_open_path, {}},
    {"system.notify", &handle_system_notify, {}},
    {"system.status", &handle_system_status, {}},
    {"system.status.watch", &handle_system_status_watch, {}},
    {"system.status.unwatch", &handle_system_status_unwatch, {}},
    {"system.gpu", &handle_system_gpu, {}},

    {"java.scan", &handle_module_method, kCsModule},
    {"java.select", &handle_module_method, kCsModule},
    {"java.runtime.plan", &handle_module_method, kCsModule},
    {"version.list", &handle_module_method, kCsModule},
    {"version.plan", &handle_module_method, kCsModule},
    {"version.commit", &handle_module_method, kCsModule},
    {"launch.start", &handle_module_method, kCsModule},
    {"launch.stop", &handle_module_method, kCsModule},
    {"launch.prepare", &handle_module_method, kCsModule},
    {"launch.memory", &handle_module_method, kCsModule},
    {"launch.logs", &handle_module_method, kCsModule},
    {"launch.log.read", &handle_module_method, kCsModule},
    {"launch.log.tail", &handle_module_method, kCsModule},
    {"launch.logs.clear", &handle_module_method, kCsModule},
    {"loader.verify", &handle_module_method, kCsModule},
    {"loader.repair", &handle_module_method, kCsModule},
    {"loader.check", &handle_module_method, kCsModule},
    {"loader.versions", &handle_module_method, kCsModule},
    {"loader.plan", &handle_module_method, kCsModule},
    {"loader.commit", &handle_module_method, kCsModule},

    {"resource.search", &handle_module_method, kCsModule},
    {"resource.versions", &handle_module_method, kCsModule},
    {"resource.project", &handle_module_method, kCsModule},
    {"resource.plan", &handle_module_method, kCsModule},
    {"download.execute", &handle_module_method, kCsModule},
    {"download.policy", &handle_module_method, kCsModule},
    {"region.policy", &handle_module_method, kCsModule},
    {"update.check", &handle_module_method, kCsModule},
    {"maintenance.cleanPartials", &handle_module_method, kCsModule},
    {"modpack.inspect", &handle_module_method, kCsModule},
    {"modpack.install", &handle_module_method, kCsModule},
    {"modpack.installLocal", &handle_module_method, kCsModule},
    {"loader.autoRepair", &handle_module_method, kCsModule},

    {"system.memory", &handle_module_method, kCsModule},
    {"system.identity", &handle_module_method, kCsModule},
    {"wiki.feed", &handle_module_method, kCsModule},
    {"settings.get", &handle_module_method, kCsModule},
    {"settings.set", &handle_module_method, kCsModule},

    {"instance.list", &handle_module_method, kCsModule},
    {"instance.create", &handle_module_method, kCsModule},
    {"instance.update", &handle_module_method, kCsModule},
    {"instance.duplicate", &handle_module_method, kCsModule},
    {"instance.delete", &handle_module_method, kCsModule},
    {"instance.sync", &handle_module_method, kCsModule},
    {"instance.mods.list", &handle_module_method, kCsModule},
    {"instance.mods.setEnabled", &handle_module_method, kCsModule},
    {"instance.mods.remove", &handle_module_method, kCsModule},

    {"account.list", &handle_module_method, kCsModule},
    {"account.current", &handle_module_method, kCsModule},
    {"account.select", &handle_module_method, kCsModule},
    {"account.login.microsoft.start", &handle_module_method, kCsModule},
    {"account.login.microsoft.poll", &handle_module_method, kCsModule},
    {"account.login.microsoft.cancel", &handle_module_method, kCsModule},
    {"account.login.offline", &handle_module_method, kCsModule},
    {"account.refresh", &handle_module_method, kCsModule},
    {"account.remove", &handle_module_method, kCsModule},
};

std::span<const MethodRoute> route_span() noexcept {
  return {kRoutes, sizeof(kRoutes) / sizeof(kRoutes[0])};
}

}  // namespace

const MethodRoute* find_method_route(std::string_view method) noexcept {
  for (const auto& route : kRoutes) {
    if (route.name == method) {
      return &route;
    }
  }
  return nullptr;
}

std::span<const MethodRoute> method_routes() noexcept {
  return route_span();
}

}  // namespace archoera
