/*
 * Small OS integration helpers that need native APIs.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * These calls are intentionally narrow: opening a validated http(s) URL in the
 * user's default browser and posting a system notification. Implementations
 * return false instead of throwing so Dart can fall back gracefully.
 */

#pragma once

#include <string>

namespace archoera {

bool open_external_url(const std::string& url, std::string* error);

bool send_system_notification(const std::string& title,
                              const std::string& body,
                              std::string* error);

// Shared validation used by every backend: only http(s) URLs without control
// characters may leave the process.
bool is_safe_external_url(const std::string& url);

}  // namespace archoera
