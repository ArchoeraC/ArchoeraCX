/*
 * Explicit method routing table for ArchoeraCX.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * The dispatcher is the only place that maps a public method name to a
 * handler. Keeping it in one table means C++ never grows a method if-chain and
 * a new domain is added by registering its exact method names here and in the
 * business module's ModuleMethodRegistry.
 */

#pragma once

#include <string>
#include <string_view>

namespace archoera {

enum class MethodRouteKind {
  kCorePing,
  kCorePingAsync,
  kCoreInfo,
  kCoreModules,
  kCoreAppearance,
  kCoreOpenUrl,
  kCoreOpenPath,
  kCoreNotify,
  kCoreStatus,
  kCoreStatusWatch,
  kCoreStatusUnwatch,
  kCoreGpu,
  kModule,
};

struct MethodRoute {
  std::string_view name;
  MethodRouteKind kind;
  std::string_view module;
};

/* Returns nullptr for unknown methods. */
const MethodRoute* find_method_route(std::string_view method) noexcept;

}  // namespace archoera
