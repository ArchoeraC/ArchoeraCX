/*
 * Windows native system actions: default browser, file manager + tray balloon
 * notification.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * ShellExecuteW is the documented way to hand an http(s) URL to the default
 * browser and a directory to Explorer. Notifications use Shell_NotifyIcon with
 * NIF_INFO, which does not require an installer-created Start Menu shortcut or
 * AppUserModelID.
 */

#include "platform_system.h"

#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0A00
#endif

#include <windows.h>
#include <shellapi.h>

#include <memory>
#include <mutex>
#include <string>
#include <thread>

namespace archoera {
namespace {

constexpr wchar_t kNotificationClass[] = L"ArchoeraCxNotification";
constexpr UINT kNotificationId = 1;

std::wstring widen(const std::string& text) {
  if (text.empty()) return {};
  const int length = MultiByteToWideChar(CP_UTF8, 0, text.data(),
                                         static_cast<int>(text.size()),
                                         nullptr, 0);
  if (length <= 0) return {};
  std::wstring result(static_cast<size_t>(length), L'\0');
  MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()),
                      result.data(), length);
  return result;
}

LRESULT CALLBACK NotificationWindowProc(HWND window,
                                        UINT message,
                                        WPARAM wparam,
                                        LPARAM lparam) {
  return DefWindowProcW(window, message, wparam, lparam);
}

void ensure_window_class() {
  static std::once_flag once;
  std::call_once(once, [] {
    WNDCLASSEXW type{};
    type.cbSize = sizeof(type);
    type.lpfnWndProc = NotificationWindowProc;
    type.hInstance = GetModuleHandleW(nullptr);
    type.lpszClassName = kNotificationClass;
    RegisterClassExW(&type);
  });
}

void run_notification(std::wstring title, std::wstring body) {
  ensure_window_class();
  HWND window = CreateWindowExW(
      0, kNotificationClass, L"", 0, 0, 0, 0, 0, nullptr, nullptr,
      GetModuleHandleW(nullptr), nullptr);
  if (window == nullptr) return;

  NOTIFYICONDATAW icon{};
  icon.cbSize = sizeof(icon);
  icon.hWnd = window;
  icon.uID = kNotificationId;
  icon.uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE | NIF_SHOWTIP;
  icon.uCallbackMessage = WM_APP + 1;
  icon.hIcon = LoadIconW(nullptr, IDI_INFORMATION);
  lstrcpynW(icon.szTip, L"ArchoeraCX", ARRAYSIZE(icon.szTip));

  if (!Shell_NotifyIconW(NIM_ADD, &icon)) {
    DestroyWindow(window);
    return;
  }
  icon.uVersion = NOTIFYICON_VERSION_4;
  Shell_NotifyIconW(NIM_SETVERSION, &icon);

  icon.uFlags = NIF_INFO;
  icon.dwInfoFlags = NIIF_INFO;
  icon.uTimeout = 6000;
  lstrcpynW(icon.szInfoTitle, title.c_str(), ARRAYSIZE(icon.szInfoTitle));
  lstrcpynW(icon.szInfo, body.c_str(), ARRAYSIZE(icon.szInfo));
  Shell_NotifyIconW(NIM_MODIFY, &icon);

  const DWORD deadline = GetTickCount() + 6500;
  MSG message{};
  while (GetTickCount() < deadline) {
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
      TranslateMessage(&message);
      DispatchMessageW(&message);
    }
    Sleep(50);
  }

  Shell_NotifyIconW(NIM_DELETE, &icon);
  DestroyWindow(window);
}

}  // namespace

bool open_external_url(const std::string& url, std::string* error) {
  if (!is_safe_external_url(url)) {
    if (error != nullptr) *error = "invalid url";
    return false;
  }
  const std::wstring wide = widen(url);
  if (wide.empty()) {
    if (error != nullptr) *error = "invalid encoding";
    return false;
  }
  const auto result = reinterpret_cast<INT_PTR>(ShellExecuteW(
      nullptr, L"open", wide.c_str(), nullptr, nullptr, SW_SHOWNORMAL));
  if (result <= 32) {
    if (error != nullptr) *error = "ShellExecuteW failed";
    return false;
  }
  return true;
}

bool open_path(const std::string& path, std::string* error) {
  if (!is_safe_directory_path(path)) {
    if (error != nullptr) *error = "invalid directory";
    return false;
  }
  const std::wstring wide = widen(path);
  if (wide.empty()) {
    if (error != nullptr) *error = "invalid encoding";
    return false;
  }
  // The "open" verb on a directory starts Explorer at that folder.
  const auto result = reinterpret_cast<INT_PTR>(ShellExecuteW(
      nullptr, L"open", wide.c_str(), nullptr, nullptr, SW_SHOWNORMAL));
  if (result <= 32) {
    if (error != nullptr) *error = "ShellExecuteW failed";
    return false;
  }
  return true;
}

bool reveal_path(const std::string& path, std::string* error) {
  // Directories are opened with Explorer; files are intentionally rejected by
  // the shared validator, so there is no executable verb to reach here.
  return open_path(path, error);
}

bool send_system_notification(const std::string& title,
                              const std::string& body,
                              std::string* error) {
  std::wstring wide_title = widen(title);
  std::wstring wide_body = widen(body);
  if (wide_title.empty() && wide_body.empty()) {
    if (error != nullptr) *error = "empty notification";
    return false;
  }
  if (wide_title.empty()) wide_title = L"ArchoeraCX";

  try {
    std::thread(&run_notification, std::move(wide_title),
                std::move(wide_body))
        .detach();
  } catch (...) {
    if (error != nullptr) *error = "notification thread failed";
    return false;
  }
  return true;
}

}  // namespace archoera
