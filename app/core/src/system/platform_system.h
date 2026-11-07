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

// Whether this process owns superuser rights. Probe only: the launcher refuses
// an elevated start unless the user explicitly confirms, because root/admin
// would take ownership of every instance, mod and game file it touches.
// Implemented with the platform's own API (POSIX/macOS effective uid, Win32
// token elevation); callers probe once at startup and never per frame.
struct ProcessElevation {
  bool elevated = false;
  // Backend-specific label for the achieved privilege: "root" on POSIX,
  // "administrator" on Windows. Empty when [elevated] is false.
  std::string kind;
};

ProcessElevation query_process_elevation();

bool open_external_url(const std::string& url, std::string* error);

// Hands an already validated absolute directory to the platform file manager.
// Never used for files: opening arbitrary paths would launch applications.
bool open_path(const std::string& path, std::string* error);

// Reveals an already validated absolute directory in the platform file manager.
// Unlike open_path this uses the OS "show item" API where available: Linux
// org.freedesktop.FileManager1, Windows Explorer, macOS Finder.
bool reveal_path(const std::string& path, std::string* error);

bool send_system_notification(const std::string& title,
                              const std::string& body,
                              std::string* error);

// Inhibits idle sleep / screen locking while a game runs. Desktop backends
// return false when the environment exposes no usable public API; callers
// treat focus mode as best effort and never fail the launch.
// [backend] receives a stable label ("screensaver", "power-assertion",
// "power-request") for diagnostics.
bool enter_focus_mode(std::string* backend, std::string* error);

// Releases the inhibition created by enter_focus_mode. Safe to call when
// nothing was inhibited and idempotent across repeated launches.
void leave_focus_mode();

// Moves an existing absolute file or directory to the platform trash so the
// user can restore it from the file manager. The path is moved, not copied or
// followed (symbolic links are trashed themselves). [backend] receives a stable
// label ("freedesktop", "nsfilemanager", "shell") for diagnostics. Returns
// false when the platform has no trash API or the move failed; callers must
// keep the file in place and report the failure instead of hard-deleting.
bool move_to_trash(const std::string& path,
                   std::string* backend,
                   std::string* error);

// Shared validation used by every backend: only http(s) URLs without control
// characters may leave the process.
bool is_safe_external_url(const std::string& url);

// Shared validation for paths used only to select a volume: an absolute,
// bounded, control-character free path. The target may not exist yet, so a
// fresh install can still sample the volume that will hold new instances. Not
// for open/reveal, which must reject files and missing paths.
bool is_safe_absolute_path(const std::string& path);

// Shared validation used by every backend: only an existing absolute directory
// with a bounded, control-character free path may be handed to the OS.
bool is_safe_directory_path(const std::string& path);

// Shared validation for trash targets: an existing absolute file or directory
// with a bounded, control-character free path. Filesystem roots are rejected
// because moving a volume root to the trash must never be possible.
bool is_safe_trash_path(const std::string& path);

}  // namespace archoera
