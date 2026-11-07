/*
 * System appearance probing (light/dark + accent color).
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * The core keeps this platform detail behind a tiny interface: the Dart side
 * asks for the current values once and receives domain events when the OS
 * changes them. Implementations must never block the caller of current().
 */

#pragma once

#include <cstdint>
#include <memory>
#include <string>

namespace archoera {

struct SystemAppearance {
  bool theme_known = false;
  bool dark = false;
  bool accent_known = false;
  int32_t accent_r = 0;
  int32_t accent_g = 0;
  int32_t accent_b = 0;
  // Diagnostic label, for example "xdg-portal" or "windows-registry".
  std::string source = "unknown";
};

// Invoked on an implementation-owned thread whenever the OS appearance
// changes. Must not throw and must not call back into the watcher.
using AppearanceCallback = void (*)(void* user, const SystemAppearance&);

class PlatformAppearanceWatcher {
 public:
  virtual ~PlatformAppearanceWatcher() = default;

  // Cached snapshot. Implementations may kick off an asynchronous refresh but
  // must return immediately.
  virtual SystemAppearance current() const = 0;

  // Starts OS change notifications. Idempotent. The callback is never invoked
  // after stop() returns.
  virtual void start(void* user, AppearanceCallback callback) = 0;

  // Stops and joins any notification work. Idempotent.
  virtual void stop() = 0;
};

std::unique_ptr<PlatformAppearanceWatcher> create_platform_appearance();

}  // namespace archoera
