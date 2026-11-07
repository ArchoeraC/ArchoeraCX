/*
 * Fallback system appearance watcher for platforms without a native backend.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_appearance.h"

namespace archoera {
namespace {

class StubAppearance final : public PlatformAppearanceWatcher {
 public:
  SystemAppearance current() const override {
    SystemAppearance state;
    state.source = "unsupported";
    return state;
  }

  void start(void*, AppearanceCallback) override {}
  void stop() override {}
};

}  // namespace

std::unique_ptr<PlatformAppearanceWatcher> create_platform_appearance() {
  return std::make_unique<StubAppearance>();
}

}  // namespace archoera
