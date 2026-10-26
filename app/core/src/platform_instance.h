/*
 * Keeps the launcher to one process per user session and forwards a second
 * launch to the running instance.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * The primary process owns a per-user lock and listens on a local IPC channel.
 * A later process fails to take the lock, sends one activation message and
 * exits. The listener runs on its own thread and publishes through the core
 * event queue, so Dart never polls for a second launch.
 */

#pragma once

#include <functional>
#include <memory>
#include <string>

namespace archoera {

class PlatformInstance {
 public:
  PlatformInstance() = default;
  virtual ~PlatformInstance() = default;

  PlatformInstance(const PlatformInstance&) = delete;
  PlatformInstance& operator=(const PlatformInstance&) = delete;

  // Takes the per-user lock. `primary` is true when this process now owns it;
  // false means another process is already running. Returns false only when
  // the backend itself failed (permission, path length, OS error).
  virtual bool acquire(bool* primary, std::string* error) = 0;

  // Starts the activation listener. Called once by the primary process.
  virtual bool listen(std::function<void()> on_activate,
                      std::string* error) = 0;

  // Sends one activation message to the primary process. Called by a
  // secondary process after [acquire] returned primary=false.
  virtual bool notify(std::string* error) = 0;

  // Stops the listener, releases the lock and removes the local endpoint.
  virtual void stop() = 0;
};

// `name` is a stable, ASCII-only application id. The factory adds the current
// user/session scope; tests pass a unique name so they never contend with a
// running launcher.
std::unique_ptr<PlatformInstance> create_platform_instance(
    const std::string& name);

}  // namespace archoera
