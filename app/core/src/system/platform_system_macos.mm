/*
 * macOS native system actions: default browser, Finder + UserNotifications.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_system.h"

#import <AppKit/AppKit.h>
#import <IOKit/pwr_mgt/IOPMLib.h>
#import <UserNotifications/UserNotifications.h>

#include <mutex>
#include <string>
#include <unistd.h>

namespace archoera {
namespace {

std::mutex g_focus_mutex;
IOPMAssertionID g_system_assertion = kIOPMNullAssertionID;
IOPMAssertionID g_display_assertion = kIOPMNullAssertionID;

NSString* to_nsstring(const std::string& text) {
  return [[NSString alloc] initWithBytes:text.data()
                                  length:text.size()
                                encoding:NSUTF8StringEncoding];
}

}  // namespace

bool open_external_url(const std::string& url, std::string* error) {
  if (!is_safe_external_url(url)) {
    if (error != nullptr) *error = "invalid url";
    return false;
  }
  NSString* text = to_nsstring(url);
  NSURL* target = text != nil ? [NSURL URLWithString:text] : nil;
  if (target == nil) {
    if (error != nullptr) *error = "invalid url";
    return false;
  }
  const BOOL opened = [[NSWorkspace sharedWorkspace] openURL:target];
  if (!opened) {
    if (error != nullptr) *error = "NSWorkspace refused the url";
    return false;
  }
  return true;
}

bool open_path(const std::string& path, std::string* error) {
  if (!is_safe_directory_path(path)) {
    if (error != nullptr) *error = "invalid directory";
    return false;
  }
  NSString* text = to_nsstring(path);
  if (text == nil) {
    if (error != nullptr) *error = "invalid encoding";
    return false;
  }
  NSURL* target = [NSURL fileURLWithPath:text isDirectory:YES];
  if (target == nil) {
    if (error != nullptr) *error = "invalid path";
    return false;
  }
  const BOOL opened = [[NSWorkspace sharedWorkspace] openURL:target];
  if (!opened) {
    if (error != nullptr) *error = "NSWorkspace refused the folder";
    return false;
  }
  return true;
}

bool reveal_path(const std::string& path, std::string* error) {
  return open_path(path, error);
}

bool send_system_notification(const std::string& title,
                              const std::string& body,
                              std::string* error) {
  NSString* ns_title = to_nsstring(title);
  NSString* ns_body = to_nsstring(body);
  if (ns_title == nil) ns_title = @"ArchoeraCX";
  if (ns_body == nil) ns_body = @"";

  UNUserNotificationCenter* center =
      [UNUserNotificationCenter currentNotificationCenter];
  if (center == nil) {
    if (error != nullptr) *error = "notification center unavailable";
    return false;
  }

  [center requestAuthorizationWithOptions:(UNAuthorizationOptionAlert |
                                          UNAuthorizationOptionSound)
                        completionHandler:^(BOOL granted, NSError* failure) {
                          (void)granted;
                          (void)failure;
                        }];

  UNMutableNotificationContent* content =
      [[UNMutableNotificationContent alloc] init];
  content.title = ns_title;
  content.body = ns_body;
  NSString* identifier = [NSString
      stringWithFormat:@"archoera-cx-%lld",
                       (long long)([NSDate date].timeIntervalSince1970 * 1000)];
  UNNotificationRequest* request =
      [UNNotificationRequest requestWithIdentifier:identifier
                                           content:content
                                           trigger:nil];
  [center addNotificationRequest:request
           withCompletionHandler:^(NSError* failure) {
             (void)failure;
           }];
  return true;
}

bool enter_focus_mode(std::string* backend, std::string* error) {
  std::lock_guard<std::mutex> lock(g_focus_mutex);
  if (g_system_assertion != kIOPMNullAssertionID) {
    if (backend != nullptr) *backend = "power-assertion";
    return true;
  }

  IOReturn result = IOPMAssertionCreateWithName(
      CFSTR("PreventUserIdleSystemSleep"),
      kIOPMAssertionLevelOn,
      CFSTR("ArchoeraCX is running a game"),
      &g_system_assertion);
  if (result != kIOReturnSuccess) {
    g_system_assertion = kIOPMNullAssertionID;
    if (error != nullptr) *error = "power assertion refused";
    return false;
  }

  // Display sleep is optional: some Macs reject it while the lid is closed,
  // but keeping the system awake is enough for the game to continue.
  if (IOPMAssertionCreateWithName(
          CFSTR("PreventUserIdleDisplaySleep"),
          kIOPMAssertionLevelOn,
          CFSTR("ArchoeraCX is running a game"),
          &g_display_assertion) != kIOReturnSuccess) {
    g_display_assertion = kIOPMNullAssertionID;
  }

  if (backend != nullptr) *backend = "power-assertion";
  return true;
}

void leave_focus_mode() {
  std::lock_guard<std::mutex> lock(g_focus_mutex);
  if (g_display_assertion != kIOPMNullAssertionID) {
    IOPMAssertionRelease(g_display_assertion);
    g_display_assertion = kIOPMNullAssertionID;
  }
  if (g_system_assertion != kIOPMNullAssertionID) {
    IOPMAssertionRelease(g_system_assertion);
    g_system_assertion = kIOPMNullAssertionID;
  }
}

bool move_to_trash(const std::string& path,
                   std::string* backend,
                   std::string* error) {
  if (!is_safe_trash_path(path)) {
    if (error != nullptr) *error = "invalid trash path";
    return false;
  }

  NSString* text = to_nsstring(path);
  if (text == nil) {
    if (error != nullptr) *error = "invalid encoding";
    return false;
  }
  NSURL* target = [NSURL fileURLWithPath:text];
  if (target == nil) {
    if (error != nullptr) *error = "invalid path";
    return false;
  }

  NSURL* resulting = nil;
  NSError* failure = nil;
  const BOOL moved = [[NSFileManager defaultManager] trashItemAtURL:target
                                                   resultingItemURL:&resulting
                                                              error:&failure];
  if (!moved) {
    if (error != nullptr) {
      *error = failure.localizedDescription != nil
                   ? failure.localizedDescription.UTF8String
                   : "trashItemAtURL failed";
    }
    return false;
  }
  if (backend != nullptr) *backend = "nsfilemanager";
  return true;
}

ProcessElevation query_process_elevation() {
  ProcessElevation elevation;

  // Darwin has no Win32-style token elevation: root and `sudo` are the only
  // ways to gain superuser rights, and both leave the effective uid at 0.
  // getuid() stays at the invoking user, so only geteuid() is checked.
  if (geteuid() == 0) {
    elevation.elevated = true;
    elevation.kind = "root";
  }
  return elevation;
}

}  // namespace archoera
