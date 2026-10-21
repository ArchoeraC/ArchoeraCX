/*
 * ArchoeraCX method registry.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "method_registry.h"

#include <array>

namespace archoera {
namespace {

constexpr std::string_view kCsModule = "archoera_cs";

/*
 * Stable public method table. C++ owns system.*; every other row is forwarded
 * to a named module. The module still validates parameters and owns all
 * business semantics.
 */
constexpr std::array<MethodRoute, 39> kRoutes = {{
    {"system.ping", MethodRouteKind::kCorePing, {}},
    {"system.pingAsync", MethodRouteKind::kCorePingAsync, {}},
    {"system.info", MethodRouteKind::kCoreInfo, {}},
    {"system.modules", MethodRouteKind::kCoreModules, {}},

    {"java.scan", MethodRouteKind::kModule, kCsModule},
    {"java.select", MethodRouteKind::kModule, kCsModule},
    {"version.list", MethodRouteKind::kModule, kCsModule},
    {"launch.start", MethodRouteKind::kModule, kCsModule},
    {"loader.verify", MethodRouteKind::kModule, kCsModule},
    {"loader.repair", MethodRouteKind::kModule, kCsModule},
    {"loader.versions", MethodRouteKind::kModule, kCsModule},
    {"loader.check", MethodRouteKind::kModule, kCsModule},
    {"loader.plan", MethodRouteKind::kModule, kCsModule},
    {"loader.commit", MethodRouteKind::kModule, kCsModule},

    {"resource.search", MethodRouteKind::kModule, kCsModule},
    {"resource.versions", MethodRouteKind::kModule, kCsModule},
    {"resource.project", MethodRouteKind::kModule, kCsModule},
    {"resource.plan", MethodRouteKind::kModule, kCsModule},
    {"download.execute", MethodRouteKind::kModule, kCsModule},

    {"system.memory", MethodRouteKind::kModule, kCsModule},
    {"settings.get", MethodRouteKind::kModule, kCsModule},
    {"settings.set", MethodRouteKind::kModule, kCsModule},

    {"instance.list", MethodRouteKind::kModule, kCsModule},
    {"instance.create", MethodRouteKind::kModule, kCsModule},
    {"instance.update", MethodRouteKind::kModule, kCsModule},
    {"instance.delete", MethodRouteKind::kModule, kCsModule},
    {"instance.sync", MethodRouteKind::kModule, kCsModule},
    {"instance.mods.list", MethodRouteKind::kModule, kCsModule},
    {"instance.mods.setEnabled", MethodRouteKind::kModule, kCsModule},
    {"instance.mods.remove", MethodRouteKind::kModule, kCsModule},

    {"account.list", MethodRouteKind::kModule, kCsModule},
    {"account.current", MethodRouteKind::kModule, kCsModule},
    {"account.select", MethodRouteKind::kModule, kCsModule},
    {"account.login.microsoft.start", MethodRouteKind::kModule, kCsModule},
    {"account.login.microsoft.poll", MethodRouteKind::kModule, kCsModule},
    {"account.login.microsoft.cancel", MethodRouteKind::kModule, kCsModule},
    {"account.login.offline", MethodRouteKind::kModule, kCsModule},
    {"account.refresh", MethodRouteKind::kModule, kCsModule},
    {"account.remove", MethodRouteKind::kModule, kCsModule},
}};

}  // namespace

const MethodRoute* find_method_route(std::string_view method) noexcept {
  for (const auto& route : kRoutes) {
    if (route.name == method) {
      return &route;
    }
  }
  return nullptr;
}

}  // namespace archoera
