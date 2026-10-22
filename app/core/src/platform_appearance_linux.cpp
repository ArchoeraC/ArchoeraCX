/*
 * Linux system appearance: light/dark scheme plus the desktop accent colour.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Sources, in order of preference for the accent:
 *   1. KDE   ~/.config/kdeglobals [General] AccentColor=r,g,b — exactly what
 *            the user picked in System Settings (the portal returns a variant
 *            darkened for the current scheme, which looks dull in-app).
 *   2. GNOME `gsettings get org.gnome.desktop.interface accent-color` — the
 *            named accent introduced in GNOME 47; mapped to its palette.
 *   3. XDG Desktop Portal `org.freedesktop.appearance` `accent-color` — the
 *            cross-desktop fallback, a (ddd) struct with sRGB in [0, 1].
 * Theme: portal `color-scheme` (0=unspecified, 1=dark, 2=light), with the
 * GNOME `color-scheme` setting as fallback.
 *
 * All portal reads are asynchronous (`dbus_connection_send_with_reply` plus a
 * notify callback dispatched by the pump thread); current() only ever reads a
 * cached snapshot. This mirrors the ArchoeraMusic platform bridge: no blocking
 * DBus call on the caller's thread, cached values, and an event pushed on every
 * change.
 */

#include "platform_appearance.h"

#include <dbus/dbus.h>

#include <algorithm>
#include <atomic>
#include <cctype>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <mutex>
#include <string>
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

constexpr int kReadTimeoutMs = 3000;
constexpr int kPumpSliceMs = 200;

int32_t to_byte(double value) {
  if (!std::isfinite(value) || value <= 0.0) {
    return 0;
  }
  if (value >= 1.0) {
    return 255;
  }
  return static_cast<int32_t>(value * 255.0 + 0.5);
}

std::string config_home() {
  const char* xdg = std::getenv("XDG_CONFIG_HOME");
  if (xdg != nullptr && *xdg != '\0') {
    return xdg;
  }
  const char* home = std::getenv("HOME");
  if (home != nullptr && *home != '\0') {
    return std::string(home) + "/.config";
  }
  return {};
}

std::string trim(std::string text) {
  auto is_space = [](unsigned char ch) { return std::isspace(ch) != 0; };
  text.erase(text.begin(),
             std::find_if(text.begin(), text.end(),
                          [&](char ch) { return !is_space(ch); }));
  text.erase(
      std::find_if(text.rbegin(), text.rend(),
                   [&](char ch) { return !is_space(ch); })
          .base(),
      text.end());
  return text;
}

bool run_command(const char* command, std::string* output) {
  if (output == nullptr) {
    return false;
  }
  FILE* pipe = popen(command, "r");
  if (pipe == nullptr) {
    return false;
  }
  char buffer[512];
  std::string result;
  while (std::fgets(buffer, sizeof(buffer), pipe) != nullptr) {
    result.append(buffer);
  }
  const int status = pclose(pipe);
  if (status != 0) {
    return false;
  }
  *output = trim(result);
  return !output->empty();
}

bool is_gnome_session() {
  const char* desktop = std::getenv("XDG_CURRENT_DESKTOP");
  if (desktop == nullptr) {
    desktop = std::getenv("XDG_SESSION_DESKTOP");
  }
  if (desktop == nullptr) {
    return false;
  }
  std::string value(desktop);
  std::transform(value.begin(), value.end(), value.begin(),
                 [](unsigned char ch) { return static_cast<char>(std::toupper(ch)); });
  return value.find("GNOME") != std::string::npos ||
         value.find("UNITY") != std::string::npos ||
         value.find("UBUNTU") != std::string::npos;
}

bool parse_rgb_triplet(const std::string& text,
                       int32_t* r,
                       int32_t* g,
                       int32_t* b) {
  int values[3] = {-1, -1, -1};
  if (std::sscanf(text.c_str(), "%d,%d,%d", &values[0], &values[1],
                  &values[2]) != 3) {
    return false;
  }
  for (int index = 0; index < 3; ++index) {
    if (values[index] < 0 || values[index] > 255) {
      return false;
    }
  }
  *r = values[0];
  *g = values[1];
  *b = values[2];
  return true;
}

// GNOME 47+ named accents (libadwaita palette).
bool gnome_named_accent(const std::string& name,
                        int32_t* r,
                        int32_t* g,
                        int32_t* b) {
  struct Entry {
    const char* name;
    int32_t r;
    int32_t g;
    int32_t b;
  };
  static constexpr Entry kPalette[] = {
      {"blue", 0x35, 0x84, 0xE4},   {"teal", 0x21, 0x90, 0xA4},
      {"green", 0x3A, 0x94, 0x4A},  {"yellow", 0xC8, 0x88, 0x00},
      {"orange", 0xED, 0x5B, 0x00}, {"red", 0xE6, 0x2D, 0x42},
      {"pink", 0xD5, 0x61, 0x99},   {"purple", 0x91, 0x41, 0xAC},
      {"slate", 0x6F, 0x83, 0x96},
  };
  for (const auto& entry : kPalette) {
    if (name == entry.name) {
      *r = entry.r;
      *g = entry.g;
      *b = entry.b;
      return true;
    }
  }
  return false;
}

class LinuxAppearance final : public PlatformAppearanceWatcher {
 public:
  LinuxAppearance() = default;
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
  struct ReplyContext {
    LinuxAppearance* self;
    bool accent;
  };

  struct AccentReading {
    bool known = false;
    int32_t r = 0;
    int32_t g = 0;
    int32_t b = 0;
  };

  void run() noexcept {
    try {
      dbus_threads_init_default();

      // Desktop config files need no bus and give the user's exact accent, so
      // read them first: the very first event can then already be complete.
      read_desktop_config(true);
      publish();

      DBusError error;
      dbus_error_init(&error);
      DBusConnection* connection =
          dbus_bus_get_private(DBUS_BUS_SESSION, &error);
      if (dbus_error_is_set(&error)) {
        dbus_error_free(&error);
      }
      if (connection == nullptr) {
        set_source("unavailable");
        publish();
        return;
      }
      dbus_connection_set_exit_on_disconnect(connection, FALSE);

      dbus_bus_add_match(connection, kMatchRule, &error);
      if (dbus_error_is_set(&error)) {
        dbus_error_free(&error);
      }
      dbus_connection_add_filter(connection, &LinuxAppearance::filter, this,
                                 nullptr);
      dbus_connection_flush(connection);

      request(connection, kColorSchemeKey, false);
      request(connection, kAccentKey, true);

      while (!stop_.load(std::memory_order_acquire)) {
        dbus_connection_read_write_dispatch(connection, kPumpSliceMs);
      }

      dbus_connection_flush(connection);
      dbus_connection_close(connection);
      dbus_connection_unref(connection);
    } catch (...) {
      // A watcher thread must never terminate the process.
    }
  }

  static DBusHandlerResult filter(DBusConnection* /*connection*/,
                                  DBusMessage* message,
                                  void* user) {
    auto* self = static_cast<LinuxAppearance*>(user);
    if (self != nullptr &&
        dbus_message_is_signal(message, kPortalInterface, "SettingChanged")) {
      self->on_setting_changed(message);
      return DBUS_HANDLER_RESULT_HANDLED;
    }
    return DBUS_HANDLER_RESULT_NOT_YET_HANDLED;
  }

  static void on_reply(DBusPendingCall* pending, void* user) {
    auto* context = static_cast<ReplyContext*>(user);
    if (context == nullptr || context->self == nullptr) {
      return;
    }
    LinuxAppearance* self = context->self;
    const bool accent = context->accent;
    DBusMessage* reply = dbus_pending_call_steal_reply(pending);
    if (reply != nullptr) {
      if (accent) {
        self->apply_portal_accent(reply);
      } else {
        self->apply_portal_scheme(reply);
      }
      dbus_message_unref(reply);
    }
    self->publish();
  }

  static void free_reply_context(void* user) {
    delete static_cast<ReplyContext*>(user);
  }

  void request(DBusConnection* connection, const char* key, bool accent) {
    DBusMessage* message = dbus_message_new_method_call(
        kPortalBus, kPortalPath, kPortalInterface, "ReadOne");
    if (message == nullptr) {
      return;
    }
    const char* space = kAppearanceNamespace;
    if (!dbus_message_append_args(message, DBUS_TYPE_STRING, &space,
                                  DBUS_TYPE_STRING, &key, DBUS_TYPE_INVALID)) {
      dbus_message_unref(message);
      return;
    }
    DBusPendingCall* pending = nullptr;
    const bool sent =
        dbus_connection_send_with_reply(connection, message, &pending,
                                       kReadTimeoutMs) &&
        pending != nullptr;
    if (sent) {
      auto* context = new ReplyContext{this, accent};
      dbus_pending_call_set_notify(pending, &LinuxAppearance::on_reply, context,
                                   &LinuxAppearance::free_reply_context);
      dbus_pending_call_unref(pending);
    }
    dbus_message_unref(message);
  }

  void on_setting_changed(DBusMessage* message) {
    DBusMessageIter root;
    if (!dbus_message_iter_init(message, &root)) {
      return;
    }
    const char* space = nullptr;
    if (dbus_message_iter_get_arg_type(&root) != DBUS_TYPE_STRING) {
      return;
    }
    dbus_message_iter_get_basic(&root, &space);
    if (!dbus_message_iter_next(&root) ||
        dbus_message_iter_get_arg_type(&root) != DBUS_TYPE_STRING) {
      return;
    }
    const char* key = nullptr;
    dbus_message_iter_get_basic(&root, &key);
    if (!dbus_message_iter_next(&root) ||
        dbus_message_iter_get_arg_type(&root) != DBUS_TYPE_VARIANT) {
      return;
    }

    DBusMessageIter variant;
    dbus_message_iter_recurse(&root, &variant);
    if (key != nullptr && std::strcmp(key, kColorSchemeKey) == 0) {
      if (dbus_message_iter_get_arg_type(&variant) == DBUS_TYPE_UINT32) {
        uint32_t value = 0;
        dbus_message_iter_get_basic(&variant, &value);
        apply_scheme(value == 1 || value == 2, value == 1);
      }
    } else if (key != nullptr && std::strcmp(key, kAccentKey) == 0) {
      if (dbus_message_iter_get_arg_type(&variant) == DBUS_TYPE_STRUCT) {
        int32_t r = 0;
        int32_t g = 0;
        int32_t b = 0;
        if (read_accent_struct(variant, &r, &g, &b)) {
          apply_portal_accent_color(r, g, b);
        }
      }
    }

    // KDE rewrites kdeglobals before emitting the portal signal; re-read it so
    // the exact user choice wins over the portal's derived variant.
    read_desktop_config(false);
    publish();
  }

  void apply_portal_scheme(DBusMessage* reply) {
    DBusMessageIter root;
    if (!dbus_message_iter_init(reply, &root) ||
        dbus_message_iter_get_arg_type(&root) != DBUS_TYPE_VARIANT) {
      return;
    }
    DBusMessageIter variant;
    dbus_message_iter_recurse(&root, &variant);
    if (dbus_message_iter_get_arg_type(&variant) != DBUS_TYPE_UINT32) {
      return;
    }
    uint32_t value = 0;
    dbus_message_iter_get_basic(&variant, &value);
    if (value == 1 || value == 2) {
      apply_scheme(true, value == 1);
    }
  }

  void apply_portal_accent(DBusMessage* reply) {
    DBusMessageIter root;
    if (!dbus_message_iter_init(reply, &root) ||
        dbus_message_iter_get_arg_type(&root) != DBUS_TYPE_VARIANT) {
      return;
    }
    DBusMessageIter variant;
    dbus_message_iter_recurse(&root, &variant);
    if (dbus_message_iter_get_arg_type(&variant) != DBUS_TYPE_STRUCT) {
      return;
    }
    int32_t r = 0;
    int32_t g = 0;
    int32_t b = 0;
    if (read_accent_struct(variant, &r, &g, &b)) {
      apply_portal_accent_color(r, g, b);
    }
  }

  static bool read_accent_struct(DBusMessageIter struct_iter,
                                 int32_t* r,
                                 int32_t* g,
                                 int32_t* b) {
    DBusMessageIter field;
    dbus_message_iter_recurse(&struct_iter, &field);
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

  void read_desktop_config(bool initial) {
    std::lock_guard<std::mutex> lock(mutex_);
    AccentReading accent;
    std::string source;

    // 1. KDE: the accent the user picked in System Settings.
    if (!config_home().empty()) {
      std::ifstream file(config_home() + "/kdeglobals");
      bool in_general = false;
      std::string line;
      while (std::getline(file, line)) {
        const std::string trimmed = trim(line);
        if (trimmed.empty() || trimmed[0] == '#') {
          continue;
        }
        if (trimmed.front() == '[') {
          in_general = trimmed == "[General]";
          continue;
        }
        if (!in_general) {
          continue;
        }
        const std::size_t eq = trimmed.find('=');
        if (eq == std::string::npos) {
          continue;
        }
        if (trim(trimmed.substr(0, eq)) != "AccentColor") {
          continue;
        }
        int32_t r = 0;
        int32_t g = 0;
        int32_t b = 0;
        if (parse_rgb_triplet(trim(trimmed.substr(eq + 1)), &r, &g, &b)) {
          accent = AccentReading{true, r, g, b};
          source = "kde-config";
        }
        break;
      }
    }

    // 2. GNOME: named accent (16.04-era desktops simply return nothing).
    if (!accent.known && is_gnome_session()) {
      std::string output;
      if (run_command(
              "gsettings get org.gnome.desktop.interface accent-color",
              &output)) {
        std::string name = output;
        name.erase(std::remove(name.begin(), name.end(), '\''), name.end());
        int32_t r = 0;
        int32_t g = 0;
        int32_t b = 0;
        if (gnome_named_accent(name, &r, &g, &b)) {
          accent = AccentReading{true, r, g, b};
          source = "gnome-settings";
        }
      }
    }

    desktop_accent_ = accent;
    if (accent.known) {
      state_.accent_known = true;
      state_.accent_r = accent.r;
      state_.accent_g = accent.g;
      state_.accent_b = accent.b;
      if (initial || state_.source.empty() || state_.source == "unknown") {
        state_.source = source;
      }
    } else {
      apply_effective_accent_locked();
    }

    // GNOME theme fallback when the portal does not answer.
    if (!state_.theme_known && is_gnome_session()) {
      std::string output;
      if (run_command(
              "gsettings get org.gnome.desktop.interface color-scheme",
              &output)) {
        if (output.find("prefer-dark") != std::string::npos) {
          state_.theme_known = true;
          state_.dark = true;
          state_.source = source.empty() ? "gnome-settings" : source;
        } else if (output.find("prefer-light") != std::string::npos ||
                   output.find("default") != std::string::npos) {
          state_.theme_known = true;
          state_.dark = false;
          state_.source = source.empty() ? "gnome-settings" : source;
        }
      }
    }
  }

  void apply_scheme(bool known, bool dark) {
    if (!known) {
      return;
    }
    std::lock_guard<std::mutex> lock(mutex_);
    state_.theme_known = true;
    state_.dark = dark;
    if (state_.source.empty() || state_.source == "unknown") {
      state_.source = "xdg-portal";
    }
  }

  void apply_portal_accent_color(int32_t r, int32_t g, int32_t b) {
    std::lock_guard<std::mutex> lock(mutex_);
    portal_accent_ = AccentReading{true, r, g, b};
    // The desktop's own config is the value the user picked; the portal value
    // is a scheme-adjusted variant, so it only fills in when nothing else did.
    apply_effective_accent_locked();
  }

  void apply_effective_accent_locked() {
    if (desktop_accent_.known) {
      state_.accent_known = true;
      state_.accent_r = desktop_accent_.r;
      state_.accent_g = desktop_accent_.g;
      state_.accent_b = desktop_accent_.b;
      return;
    }
    if (portal_accent_.known) {
      state_.accent_known = true;
      state_.accent_r = portal_accent_.r;
      state_.accent_g = portal_accent_.g;
      state_.accent_b = portal_accent_.b;
      if (state_.source.empty() || state_.source == "unknown") {
        state_.source = "xdg-portal";
      }
    }
  }

  void set_source(const char* source) {
    std::lock_guard<std::mutex> lock(mutex_);
    state_.source = source;
  }

  void publish() {
    SystemAppearance snapshot;
    AppearanceCallback callback = nullptr;
    void* user = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      snapshot = state_;
      callback = callback_;
      user = user_;
    }
    if (callback == nullptr || (!snapshot.theme_known && !snapshot.accent_known)) {
      return;
    }
    std::lock_guard<std::mutex> lock(emit_mutex_);
    if (last_published_.theme_known == snapshot.theme_known &&
        last_published_.dark == snapshot.dark &&
        last_published_.accent_known == snapshot.accent_known &&
        last_published_.accent_r == snapshot.accent_r &&
        last_published_.accent_g == snapshot.accent_g &&
        last_published_.accent_b == snapshot.accent_b) {
      return;
    }
    last_published_ = snapshot;
    callback(user, snapshot);
  }

  mutable std::mutex mutex_;
  std::mutex emit_mutex_;
  SystemAppearance state_;
  SystemAppearance last_published_;
  AccentReading desktop_accent_;
  AccentReading portal_accent_;
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
