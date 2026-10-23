/*
 * Fallback system actions for unsupported platforms.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "platform_system.h"

namespace archoera {

bool open_external_url(const std::string&, std::string* error) {
  if (error != nullptr) *error = "unsupported platform";
  return false;
}

bool open_path(const std::string&, std::string* error) {
  if (error != nullptr) *error = "unsupported platform";
  return false;
}

bool send_system_notification(const std::string&,
                              const std::string&,
                              std::string* error) {
  if (error != nullptr) *error = "unsupported platform";
  return false;
}

}  // namespace archoera
