/*
 * Small OS integration helpers that need native APIs.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * These calls are intentionally narrow: opening a validated http(s) URL in the
 * user's default browser, revealing an existing absolute directory in the
 * platform file manager, and posting a system notification. Implementations
 * return false instead of throwing so Dart can fall back gracefully.
 */

#pragma once

#include <string>

namespace archoera {

bool open_external_url(const std::string& url, std::string* error);

// Hands an already validated absolute directory to the platform file manager.
// Never used for files: opening arbitrary paths would launch applications.
bool open_path(const std::string& path, std::string* error);

bool send_system_notification(const std::string& title,
                              const std::string& body,
                              std::string* error);

// Shared validation used by every backend: only http(s) URLs without control
// characters may leave the process.
bool is_safe_external_url(const std::string& url);

// Shared validation used by every backend: only an existing absolute directory
// with a bounded, control-character free path may be handed to the OS.
bool is_safe_directory_path(const std::string& path);

}  // namespace archoera
