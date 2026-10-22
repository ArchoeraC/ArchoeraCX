/*
 * Linux system appearance via the XDG Desktop Portal.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Standard interface used by GTK/KDE/Qt applications:
 *   org.freedesktop.portal.Desktop
 *   /org/freedesktop/portal/desktop
 *   org.freedesktop.portal.Settings.ReadOne("org.freedesktop.appearance", ...)
 *
 * "color-scheme" is 0=unspecified, 1=dark, 2=light.
 * "accent-color" is a (dddd) struct with sRGB components in [0, 1].
 *
 * The blocking read and the signal loop run on a watcher-owned thread; the
 * public current() only reads a cached snapshot.
 */

#include "platform_appearance.h"

#include <dbus/dbus.h>

#include <atomic>
#include <cmath>
#include <mutex>
#include <thread>

namespace archoera {
namespace {

constexpr const char* kPortalBus = "org.freedesktop.portal.Desktop";
constexpr const char* kPortalPath = "/org/freedesktop/portal/desktop";
constexpr const char* kPortalInterface = "org.freedesktop.portal.Settings";
constexpr const char* kAppearanceNamespace = "org.freedesktop.appearance";
constexpr const char* kColorSchemeKey = "color-scheme";
constexpr const char* kAccentKey = "accent-color";

constexpr const char* kMatchRule =
    "type='signal',interface='org.freedesktop.portal.Settings',"
    "member='SettingChanged',path='/org/freedesktop/portal/desktop'";

int32_t to_byte(double value) {
  if (!std::isfinite(value) || value <= 0.0) {
    return 0;
  }
  if (value >= 1.0) {
    return 255;
  }
  return static_cast<int32_t>(value * 255.0 + 0.5);
}

bool read_uint32(DBusMessage* message, uint32_t* value) {
  if (message == nullptr || value == nullptr) {
    return false;
  }
  DBusMessageIter root;
  if (!dbus_message_iter_init(message, &root) ||
      dbus_message_iter_get_arg_type(&root) != DBUS_TYPE_VARIANT) {
    return false;
  }
  DBusMessageIter variant;
  dbus_message_iter_recurse(&root, &variant);
  if (dbus_message_iter_get_arg_type(&variant) != DBUS_TYPE_UINT32) {
    return false;
  }
  dbus_message_iter_get_basic(&variant, value);
  return true;
}

bool read_accent(DBusMessage* message,
                 int32_t* r,
                 int32_t* g,
                 int32_t* b) {
  if (message == nullptr || r == nullptr || g == nullptr || b == nullptr) {
    return false;
  }
  DBusMessageIter root;
  if (!dbus_message_iter_init(message, &root) ||
      dbus_message_iter_get_arg_type(&root) != DBUS_TYPE_VARIANT) {
    return false;
  }
  DBusMessageIter variant;
  dbus_message_iter_recurse(&root, &variant);
  if (dbus_message_iter_get_arg_type(&variant) != DBUS_TYPE_STRUCT) {
    return false;
  }
  DBusMessageIter field;
  dbus_message_iter_recurse(&variant, &field);
  double values[3] = {0.0, 0.0, 0.0};
  for (int index = 0; index < 3; ++index) {
    if (dbus_message_iter_get_arg_type(&field) != DBUS_TYPE_DOUBLE) {
      return false;
    }
    dbus_message_iter_get_basic(&field, &values[index]);
    if (index < 2 && !dbus_message_iter_next(&field)) {
      return false;
    }
  }
  *r = to_byte(values[0]);
  *g = to_byte(values[1]);
  *b = to_byte(values[2]);
  return true;
}

DBusMessage* read_one(DBusConnection* connection, const char* key) {
  DBusMessage* request = dbus_message_new_method_call(
      kPortalBus, kPortalPath, kPortalInterface, "ReadOne");
  if (request == nullptr) {
    return nullptr;
  }

  const char* space = kAppearanceNamespace;
  if (!dbus_message_append_args(request, DBUS_TYPE_STRING, &space,
                                DBUS_TYPE_STRING, &key, DBUS_TYPE_INVALID)) {
    dbus_message_unref(request);
    return nullptr;
  }

  DBusError error;
  dbus_error_init(&error);
  DBusMessage* reply = dbus_connection_send_with_reply_and_block(
      connection, request, 1500, &error);
  dbus_message_unref(request);
  if (dbus_error_is_set(&error)) {
    dbus_error_free(&error);
    return nullptr;
  }
  return reply;
}

class LinuxAppearance final : public PlatformAppearanceWatcher {
 public:
  ~LinuxAppearance() override { stop(); }

  SystemAppearance current() const override {
    std::lock_guard<std::mutex> lock(mutex_);
    return state_;
  }

  void start(void* user, AppearanceCallback callback) override {
    if (thread_.joinable()) {
      return;
    }
    user_ = user;
    callback_ = callback;
    stop_.store(false, std::memory_order_release);
    thread_ = std::thread(&LinuxAppearance::run, this);
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
    try {
      dbus_threads_init_default();
      DBusError error;
      dbus_error_init(&error);
      DBusConnection* connection =
          dbus_bus_get_private(DBUS_BUS_SESSION, &error);
      if (dbus_error_is_set(&error)) {
        dbus_error_free(&error);
      }
      if (connection == nullptr) {
        {
          std::lock_guard<std::mutex> lock(mutex_);
          state_.source = "xdg-portal-unavailable";
        }
        return;
      }
      dbus_connection_set_exit_on_disconnect(connection, FALSE);

      dbus_bus_add_match(connection, kMatchRule, &error);
      if (dbus_error_is_set(&error)) {
        dbus_error_free(&error);
      }
      dbus_connection_flush(connection);

      refresh(connection, true);

      while (!stop_.load(std::memory_order_acquire)) {
        if (!dbus_connection_read_write(connection, 200)) {
          break;
        }
        DBusMessage* message = nullptr;
        while ((message = dbus_connection_pop_message(connection)) != nullptr) {
          if (dbus_message_is_signal(message, kPortalInterface,
                                     "SettingChanged")) {
            apply_signal(message);
          }
          dbus_message_unref(message);
        }
      }

      dbus_connection_close(connection);
      dbus_connection_unref(connection);
    } catch (...) {
      // A watcher thread must never terminate the process.
    }
  }

  void refresh(DBusConnection* connection, bool announce) {
    bool changed = false;

    DBusMessage* scheme = read_one(connection, kColorSchemeKey);
    if (scheme != nullptr) {
      uint32_t value = 0;
      if (read_uint32(scheme, &value) && (value == 1 || value == 2)) {
        std::lock_guard<std::mutex> lock(mutex_);
        const bool dark = value == 1;
        if (!state_.theme_known || state_.dark != dark) {
          state_.theme_known = true;
          state_.dark = dark;
          changed = true;
        }
        state_.source = "xdg-portal";
      }
      dbus_message_unref(scheme);
    }

    DBusMessage* accent = read_one(connection, kAccentKey);
    if (accent != nullptr) {
      int32_t r = 0;
      int32_t g = 0;
      int32_t b = 0;
      if (read_accent(accent, &r, &g, &b)) {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!state_.accent_known || state_.accent_r != r ||
            state_.accent_g != g || state_.accent_b != b) {
          state_.accent_known = true;
          state_.accent_r = r;
          state_.accent_g = g;
          state_.accent_b = b;
          changed = true;
        }
        state_.source = "xdg-portal";
      }
      dbus_message_unref(accent);
    }

    if (changed || announce) {
      emit_if_known();
    }
  }

  void apply_signal(DBusMessage* message) {
    DBusMessageIter root;
    if (!dbus_message_iter_init(message, &root)) {
      return;
    }
    const char* space = nullptr;
    if (dbus_message_iter_get_arg_type(&root) != DBUS_TYPE_STRING) {
      return;
    }
    dbus_message_iter_get_basic(&root, &space);
    dbus_message_iter_next(&root);
    if (space == nullptr || dbus_message_iter_get_arg_type(&root) !=
                                DBUS_TYPE_STRING) {
      return;
    }
    const char* key = nullptr;
    dbus_message_iter_get_basic(&root, &key);
    dbus_message_iter_next(&root);
    if (key == nullptr ||
        dbus_message_iter_get_arg_type(&root) != DBUS_TYPE_VARIANT) {
      return;
    }

    bool changed = false;
    if (std::string(key) == kColorSchemeKey) {
      DBusMessageIter variant;
      dbus_message_iter_recurse(&root, &variant);
      uint32_t value = 0;
      if (dbus_message_iter_get_arg_type(&variant) == DBUS_TYPE_UINT32) {
        dbus_message_iter_get_basic(&variant, &value);
        if (value == 1 || value == 2) {
          std::lock_guard<std::mutex> lock(mutex_);
          const bool dark = value == 1;
          if (!state_.theme_known || state_.dark != dark) {
            state_.theme_known = true;
            state_.dark = dark;
            changed = true;
          }
        }
      }
    } else if (std::string(key) == kAccentKey) {
      DBusMessageIter variant;
      dbus_message_iter_recurse(&root, &variant);
      if (dbus_message_iter_get_arg_type(&variant) == DBUS_TYPE_STRUCT) {
        DBusMessageIter field;
        dbus_message_iter_recurse(&variant, &field);
        double values[3] = {0.0, 0.0, 0.0};
        bool valid = true;
        for (int index = 0; index < 3; ++index) {
          if (dbus_message_iter_get_arg_type(&field) != DBUS_TYPE_DOUBLE) {
            valid = false;
            break;
          }
          dbus_message_iter_get_basic(&field, &values[index]);
          if (index < 2) {
            dbus_message_iter_next(&field);
          }
        }
        if (valid) {
          const int32_t r = to_byte(values[0]);
          const int32_t g = to_byte(values[1]);
          const int32_t b = to_byte(values[2]);
          std::lock_guard<std::mutex> lock(mutex_);
          if (!state_.accent_known || state_.accent_r != r ||
              state_.accent_g != g || state_.accent_b != b) {
            state_.accent_known = true;
            state_.accent_r = r;
            state_.accent_g = g;
            state_.accent_b = b;
            changed = true;
          }
        }
      }
    }

    if (changed) {
      emit_if_known();
    }
  }

  void emit_if_known() {
    SystemAppearance snapshot;
    void* user = nullptr;
    AppearanceCallback callback = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (!state_.theme_known && !state_.accent_known) {
        return;
      }
      snapshot = state_;
      user = user_;
      callback = callback_;
    }
    if (callback != nullptr) {
      callback(user, snapshot);
    }
  }

  mutable std::mutex mutex_;
  SystemAppearance state_;
  void* user_ = nullptr;
  AppearanceCallback callback_ = nullptr;
  std::atomic<bool> stop_{false};
  std::thread thread_;
};

}  // namespace

std::unique_ptr<PlatformAppearanceWatcher> create_platform_appearance() {
  return std::make_unique<LinuxAppearance>();
}

}  // namespace archoera
