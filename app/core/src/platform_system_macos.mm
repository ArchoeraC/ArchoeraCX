/*
 * macOS native system actions: default browser + UserNotifications.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_system.h"

#import <AppKit/AppKit.h>
#import <UserNotifications/UserNotifications.h>

namespace archoera {
namespace {

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

}  // namespace archoera
