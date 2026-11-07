/*
 * Fallback single-instance backend for platforms without a native
 * implementation. It keeps the launcher usable (single-instance arbitration is
 * disabled) instead of failing startup.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_instance.h"

#include <utility>

namespace archoera {
namespace {

class StubPlatformInstance final : public PlatformInstance {
 public:
  bool acquire(bool* primary, std::string*) override {
    if (primary == nullptr) {
      return false;
    }
    *primary = true;
    return true;
  }

  bool listen(std::function<void()>, std::string*) override { return true; }

  bool notify(std::string*) override { return true; }

  void stop() override {}
};

}  // namespace

std::unique_ptr<PlatformInstance> create_platform_instance(
    const std::string&) {
  return std::make_unique<StubPlatformInstance>();
}

}  // namespace archoera
