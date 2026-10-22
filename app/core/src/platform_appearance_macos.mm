/*
 * macOS system appearance via AppKit.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Dark mode: NSUserDefaults AppleInterfaceStyle == "Dark".
 * Accent: NSColor.controlAccentColor converted to sRGB.
 *
 * Changes arrive through the distributed notifications Apple posts for
 * interface-theme and color-preference changes, so no timer is needed.
 */

#include "platform_appearance.h"

#import <AppKit/AppKit.h>

#include <mutex>

namespace archoera {
namespace {

class MacAppearance final : public PlatformAppearanceWatcher {
 public:
  ~MacAppearance() override { stop(); }

  SystemAppearance current() const override {
    SystemAppearance next;
    read_into(&next);
    std::lock_guard<std::mutex> lock(mutex_);
    state_ = next;
    return next;
  }

  void start(void* user, AppearanceCallback callback) override {
    if (theme_token_ != nil || color_token_ != nil) {
      return;
    }
    user_ = user;
    callback_ = callback;

    NSDistributedNotificationCenter* center =
        [NSDistributedNotificationCenter defaultCenter];
    __block MacAppearance* self = this;
    theme_token_ = [center
        addObserverForName:@"AppleInterfaceThemeChangedNotification"
                    object:nil
                     queue:nil
                usingBlock:^(NSNotification* notification) {
                  (void)notification;
                  self->notify_now();
                }];
    color_token_ = [center
        addObserverForName:@"AppleColorPreferencesChangedNotification"
                    object:nil
                     queue:nil
                usingBlock:^(NSNotification* notification) {
                  (void)notification;
                  self->notify_now();
                }];

    notify_now();
  }

  void stop() override {
    NSDistributedNotificationCenter* center =
        [NSDistributedNotificationCenter defaultCenter];
    if (theme_token_ != nil) {
      [center removeObserver:theme_token_];
      theme_token_ = nil;
    }
    if (color_token_ != nil) {
      [center removeObserver:color_token_];
      color_token_ = nil;
    }
    callback_ = nullptr;
    user_ = nullptr;
  }

 private:
  static void read_into(SystemAppearance* state) {
    if (state == nullptr) {
      return;
    }
    state->source = "macos";

    NSString* style = [[NSUserDefaults standardUserDefaults]
        stringForKey:@"AppleInterfaceStyle"];
    state->theme_known = true;
    state->dark =
        style != nil &&
        [style caseInsensitiveCompare:@"Dark"] == NSOrderedSame;

    NSColor* accent = [NSColor controlAccentColor];
    if (accent != nil) {
      NSColor* srgb =
          [accent colorUsingColorSpace:[NSColorSpace sRGBColorSpace]];
      if (srgb != nil) {
        state->accent_known = true;
        state->accent_r =
            static_cast<int32_t>([srgb redComponent] * 255.0 + 0.5);
        state->accent_g =
            static_cast<int32_t>([srgb greenComponent] * 255.0 + 0.5);
        state->accent_b =
            static_cast<int32_t>([srgb blueComponent] * 255.0 + 0.5);
      }
    }
  }

  void notify_now() {
    SystemAppearance next;
    read_into(&next);

    SystemAppearance snapshot;
    void* user = nullptr;
    AppearanceCallback callback = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      state_ = next;
      snapshot = state_;
      user = user_;
      callback = callback_;
    }
    if (callback != nullptr) {
      callback(user, snapshot);
    }
  }

  mutable std::mutex mutex_;
  mutable SystemAppearance state_;
  void* user_ = nullptr;
  AppearanceCallback callback_ = nullptr;
  id theme_token_ = nil;
  id color_token_ = nil;
};

}  // namespace

std::unique_ptr<PlatformAppearanceWatcher> create_platform_appearance() {
  return std::make_unique<MacAppearance>();
}

}  // namespace archoera
