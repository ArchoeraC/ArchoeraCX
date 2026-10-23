/*
 * Linux native system actions: default browser, file manager + desktop
 * notification.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * All go through standard freedesktop D-Bus interfaces:
 *   - org.freedesktop.portal.OpenURI.OpenURI
 *   - org.freedesktop.Notifications.Notify
 * No shell process is started, so no shell metacharacters can reach a program
 * name and a directory cannot be mistaken for an executable.
 */

#include "platform_system.h"

#include <dbus/dbus.h>

#include <cstdint>

namespace archoera {
namespace {

DBusConnection* open_session_bus() {
  DBusError error;
  dbus_error_init(&error);
  DBusConnection* connection =
      dbus_bus_get_private(DBUS_BUS_SESSION, &error);
  if (dbus_error_is_set(&error)) {
    dbus_error_free(&error);
  }
  if (connection != nullptr) {
    dbus_connection_set_exit_on_disconnect(connection, FALSE);
  }
  return connection;
}

void close_session_bus(DBusConnection* connection) {
  if (connection == nullptr) return;
  dbus_connection_close(connection);
  dbus_connection_unref(connection);
}

DBusMessage* call_with_reply(DBusConnection* connection,
                             DBusMessage* message,
                             int timeout_ms) {
  if (connection == nullptr || message == nullptr) {
    return nullptr;
  }
  DBusError error;
  dbus_error_init(&error);
  DBusMessage* reply =
      dbus_connection_send_with_reply_and_block(connection, message,
                                                timeout_ms, &error);
  if (dbus_error_is_set(&error)) {
    dbus_error_free(&error);
  }
  return reply;
}

// Opens any portal URI (http(s) or file). Callers validate the URI first.
bool open_uri_via_portal(const std::string& uri, std::string* error) {
  DBusConnection* connection = open_session_bus();
  if (connection == nullptr) {
    if (error != nullptr) *error = "session bus unavailable";
    return false;
  }

  DBusMessage* message = dbus_message_new_method_call(
      "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop",
      "org.freedesktop.portal.OpenURI", "OpenURI");
  if (message == nullptr) {
    close_session_bus(connection);
    if (error != nullptr) *error = "out of memory";
    return false;
  }

  const char* parent_window = "";
  const char* target = uri.c_str();
  DBusMessageIter iterator;
  DBusMessageIter options;
  dbus_message_iter_init_append(message, &iterator);
  dbus_message_iter_append_basic(&iterator, DBUS_TYPE_STRING, &parent_window);
  dbus_message_iter_append_basic(&iterator, DBUS_TYPE_STRING, &target);
  dbus_message_iter_open_container(&iterator, DBUS_TYPE_ARRAY, "{sv}",
                                   &options);
  dbus_message_iter_close_container(&iterator, &options);

  DBusMessage* reply = call_with_reply(connection, message, 1500);
  dbus_message_unref(message);
  close_session_bus(connection);

  if (reply == nullptr) {
    if (error != nullptr) *error = "portal call failed";
    return false;
  }
  dbus_message_unref(reply);
  return true;
}

// RFC 3986 encoding for the path part of a file URI: unreserved characters
// and '/' stay literal, everything else (spaces, UTF-8 bytes, '#', '?') is
// percent encoded so the portal sees exactly one path.
std::string encode_uri_path(const std::string& path) {
  static constexpr char kHex[] = "0123456789ABCDEF";
  std::string encoded;
  encoded.reserve(path.size());
  for (const unsigned char ch : path) {
    const bool unreserved =
        (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') ||
        (ch >= '0' && ch <= '9') || ch == '-' || ch == '.' || ch == '_' ||
        ch == '~' || ch == '/';
    if (unreserved) {
      encoded.push_back(static_cast<char>(ch));
    } else {
      encoded.push_back('%');
      encoded.push_back(kHex[ch >> 4]);
      encoded.push_back(kHex[ch & 0x0f]);
    }
  }
  return encoded;
}

}  // namespace

bool open_external_url(const std::string& url, std::string* error) {
  if (!is_safe_external_url(url)) {
    if (error != nullptr) *error = "invalid url";
    return false;
  }
  return open_uri_via_portal(url, error);
}

bool open_path(const std::string& path, std::string* error) {
  if (!is_safe_directory_path(path)) {
    if (error != nullptr) *error = "invalid directory";
    return false;
  }

  std::string uri = "file://";
  uri.append(encode_uri_path(path));
  // Trailing slash tells the portal (and the file manager) this is a folder.
  if (uri.back() != '/') {
    uri.push_back('/');
  }
  return open_uri_via_portal(uri, error);
}

bool send_system_notification(const std::string& title,
                              const std::string& body,
                              std::string* error) {
  if (title.empty() && body.empty()) {
    if (error != nullptr) *error = "empty notification";
    return false;
  }

  DBusConnection* connection = open_session_bus();
  if (connection == nullptr) {
    if (error != nullptr) *error = "session bus unavailable";
    return false;
  }

  DBusMessage* message = dbus_message_new_method_call(
      "org.freedesktop.Notifications", "/org/freedesktop/Notifications",
      "org.freedesktop.Notifications", "Notify");
  if (message == nullptr) {
    close_session_bus(connection);
    if (error != nullptr) *error = "out of memory";
    return false;
  }

  const char* app_name = "ArchoeraCX";
  const char* app_icon = "";
  const char* summary = title.empty() ? "ArchoeraCX" : title.c_str();
  const char* text = body.c_str();
  uint32_t replaces_id = 0;
  int32_t timeout = -1;

  DBusMessageIter iterator;
  DBusMessageIter actions;
  DBusMessageIter hints;
  dbus_message_iter_init_append(message, &iterator);
  dbus_message_iter_append_basic(&iterator, DBUS_TYPE_STRING, &app_name);
  dbus_message_iter_append_basic(&iterator, DBUS_TYPE_UINT32, &replaces_id);
  dbus_message_iter_append_basic(&iterator, DBUS_TYPE_STRING, &app_icon);
  dbus_message_iter_append_basic(&iterator, DBUS_TYPE_STRING, &summary);
  dbus_message_iter_append_basic(&iterator, DBUS_TYPE_STRING, &text);
  dbus_message_iter_open_container(&iterator, DBUS_TYPE_ARRAY, "s", &actions);
  dbus_message_iter_close_container(&iterator, &actions);
  dbus_message_iter_open_container(&iterator, DBUS_TYPE_ARRAY, "{sv}", &hints);
  dbus_message_iter_close_container(&iterator, &hints);
  dbus_message_iter_append_basic(&iterator, DBUS_TYPE_INT32, &timeout);

  DBusMessage* reply = call_with_reply(connection, message, 3000);
  dbus_message_unref(message);
  close_session_bus(connection);

  if (reply == nullptr) {
    if (error != nullptr) *error = "notification service unavailable";
    return false;
  }
  dbus_message_unref(reply);
  return true;
}

}  // namespace archoera
