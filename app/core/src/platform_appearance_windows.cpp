/*
 * Windows system appearance via documented registry values.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Theme: HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
 *        AppsUseLightTheme (1 = light, 0 = dark)
 * Accent: HKCU\Software\Microsoft\Windows\DWM\AccentColor (0xAABBGGRR)
 *         with a DwmGetColorizationColor fallback.
 *
 * RegNotifyChangeKeyValue is the documented notification mechanism, so the
 * watcher does not poll the registry while the values are stable.
 */

#include "platform_appearance.h"

#include <windows.h>
#include <dwmapi.h>

#include <atomic>
#include <mutex>
#include <thread>

namespace archoera {
namespace {

constexpr wchar_t kPersonalizeKey[] =
    L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
constexpr wchar_t kPersonalizeThemeValue[] = L"AppsUseLightTheme";
constexpr wchar_t kDwmKey[] = L"Software\\Microsoft\\Windows\\DWM";
constexpr wchar_t kDwmAccentValue[] = L"AccentColor";

bool read_dword(HKEY root,
                const wchar_t* subkey,
                const wchar_t* value_name,
                DWORD* value) {
  HKEY key = nullptr;
  if (RegOpenKeyExW(root, subkey, 0, KEY_QUERY_VALUE, &key) != ERROR_SUCCESS) {
    return false;
  }
  DWORD type = 0;
  DWORD size = sizeof(DWORD);
  DWORD data = 0;
  const LONG status = RegQueryValueExW(
      key, value_name, nullptr, &type, reinterpret_cast<BYTE*>(&data), &size);
  RegCloseKey(key);
  if (status != ERROR_SUCCESS || type != REG_DWORD) {
    return false;
  }
  *value = data;
  return true;
}

class WindowsAppearance final : public PlatformAppearanceWatcher {
 public:
  ~WindowsAppearance() override { stop(); }

  SystemAppearance current() const override {
    SystemAppearance next;
    read_into(&next);
    std::lock_guard<std::mutex> lock(mutex_);
    state_ = next;
    return next;
  }

  void start(void* user, AppearanceCallback callback) override {
    if (thread_.joinable()) {
      return;
    }
    user_ = user;
    callback_ = callback;
    stop_.store(false, std::memory_order_release);
    thread_ = std::thread(&WindowsAppearance::run, this);
  }

  void stop() override {
    stop_.store(true, std::memory_order_release);
    if (thread_.joinable()) {
      thread_.join();
    }
    callback_ = nullptr;
    user_ = nullptr;
  }

 private:
  void run() noexcept {
    const HANDLE personal_key = open_notify_key(kPersonalizeKey);
    const HANDLE dwm_key = open_notify_key(kDwmKey);
    HANDLE events[2] = {
        CreateEventW(nullptr, TRUE, FALSE, nullptr),
        CreateEventW(nullptr, TRUE, FALSE, nullptr),
    };

    if (events[0] == nullptr || events[1] == nullptr) {
      refresh();
      while (!stop_.load(std::memory_order_acquire)) {
        Sleep(250);
        refresh();
      }
      for (HANDLE event : events) {
        if (event != nullptr) {
          CloseHandle(event);
        }
      }
      if (personal_key != nullptr) {
        RegCloseKey(personal_key);
      }
      if (dwm_key != nullptr) {
        RegCloseKey(dwm_key);
      }
      return;
    }

    if (personal_key != nullptr && events[0] != nullptr) {
      RegNotifyChangeKeyValue(personal_key, FALSE, REG_NOTIFY_CHANGE_LAST_SET,
                              events[0], TRUE);
    }
    if (dwm_key != nullptr && events[1] != nullptr) {
      RegNotifyChangeKeyValue(dwm_key, FALSE, REG_NOTIFY_CHANGE_LAST_SET,
                              events[1], TRUE);
    }

    refresh();

    while (!stop_.load(std::memory_order_acquire)) {
      const DWORD wait =
          WaitForMultipleObjects(2, events, FALSE, 500);
      if (wait == WAIT_OBJECT_0 || wait == WAIT_OBJECT_0 + 1) {
        if (events[0] != nullptr) {
          ResetEvent(events[0]);
        }
        if (events[1] != nullptr) {
          ResetEvent(events[1]);
        }
        refresh();
        if (personal_key != nullptr && events[0] != nullptr) {
          RegNotifyChangeKeyValue(personal_key, FALSE,
                                  REG_NOTIFY_CHANGE_LAST_SET, events[0], TRUE);
        }
        if (dwm_key != nullptr && events[1] != nullptr) {
          RegNotifyChangeKeyValue(dwm_key, FALSE, REG_NOTIFY_CHANGE_LAST_SET,
                                  events[1], TRUE);
        }
      }
    }

    for (HANDLE event : events) {
      if (event != nullptr) {
        CloseHandle(event);
      }
    }
    if (personal_key != nullptr) {
      RegCloseKey(personal_key);
    }
    if (dwm_key != nullptr) {
      RegCloseKey(dwm_key);
    }
  }

  void refresh() {
    SystemAppearance next;
    read_into(&next);

    SystemAppearance snapshot;
    void* user = nullptr;
    AppearanceCallback callback = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      const bool changed =
          next.theme_known != state_.theme_known ||
          next.dark != state_.dark ||
          next.accent_known != state_.accent_known ||
          next.accent_r != state_.accent_r ||
          next.accent_g != state_.accent_g ||
          next.accent_b != state_.accent_b;
      state_ = next;
      if (!changed && announced_) {
        return;
      }
      announced_ = true;
      snapshot = state_;
      user = user_;
      callback = callback_;
    }
    if (callback != nullptr) {
      callback(user, snapshot);
    }
  }

  static HANDLE open_notify_key(const wchar_t* subkey) {
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, subkey, 0,
                      KEY_NOTIFY | KEY_QUERY_VALUE, &key) != ERROR_SUCCESS) {
      return nullptr;
    }
    return reinterpret_cast<HANDLE>(key);
  }

  static void read_into(SystemAppearance* state) {
    if (state == nullptr) {
      return;
    }
    state->source = "windows-registry";

    DWORD light = 0;
    if (read_dword(HKEY_CURRENT_USER, kPersonalizeKey,
                   kPersonalizeThemeValue, &light)) {
      state->theme_known = true;
      state->dark = light == 0;
    }

    DWORD accent = 0;
    if (read_dword(HKEY_CURRENT_USER, kDwmKey, kDwmAccentValue, &accent)) {
      state->accent_known = true;
      state->accent_r = static_cast<int32_t>(accent & 0xFF);
      state->accent_g = static_cast<int32_t>((accent >> 8) & 0xFF);
      state->accent_b = static_cast<int32_t>((accent >> 16) & 0xFF);
    } else {
      DWORD colorization = 0;
      BOOL blend = FALSE;
      if (SUCCEEDED(DwmGetColorizationColor(&colorization, &blend)) &&
          colorization != 0) {
        state->accent_known = true;
        state->accent_r = static_cast<int32_t>((colorization >> 16) & 0xFF);
        state->accent_g = static_cast<int32_t>((colorization >> 8) & 0xFF);
        state->accent_b = static_cast<int32_t>(colorization & 0xFF);
      }
    }
  }

  mutable std::mutex mutex_;
  mutable SystemAppearance state_;
  bool announced_ = false;
  void* user_ = nullptr;
  AppearanceCallback callback_ = nullptr;
  std::atomic<bool> stop_{false};
  std::thread thread_;
};

}  // namespace

std::unique_ptr<PlatformAppearanceWatcher> create_platform_appearance() {
  return std::make_unique<WindowsAppearance>();
}

}  // namespace archoera
