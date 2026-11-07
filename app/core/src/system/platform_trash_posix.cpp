/*
 * freedesktop.org trash implementation for POSIX systems without a desktop
 * toolkit dependency.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Follows the Trash specification: the item is renamed into
 * <trash>/files/<name> and a matching <trash>/info/<name>.trashinfo records
 * the original path and deletion time. The home trash ($XDG_DATA_HOME/Trash)
 * is used when the item lives on the same device; otherwise the trash folder
 * at the top of that volume is used, because a cross-device rename is not
 * atomic and would copy user data silently.
 */

#include "platform_system.h"

#include <ctime>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <string>
#include <system_error>

#include <pwd.h>
#include <sys/stat.h>
#include <unistd.h>

namespace archoera {
namespace {

constexpr size_t kMaxTrashNameAttempts = 1000;

std::filesystem::path home_directory() {
  const char* home = std::getenv("HOME");
  if (home != nullptr && *home != '\0') {
    return std::filesystem::path(home);
  }
  const passwd* entry = getpwuid(getuid());
  if (entry != nullptr && entry->pw_dir != nullptr) {
    return std::filesystem::path(entry->pw_dir);
  }
  return {};
}

std::filesystem::path user_data_home() {
  const char* path = std::getenv("XDG_DATA_HOME");
  if (path != nullptr && *path == '/') {
    return std::filesystem::path(path);
  }
  const auto home = home_directory();
  return home.empty() ? home : home / ".local" / "share";
}

bool same_device(const std::filesystem::path& first,
                 const std::filesystem::path& second) {
  struct stat first_stat {};
  struct stat second_stat {};
  // lstat keeps a symbolic link on its own volume instead of following the
  // link target.
  if (lstat(first.c_str(), &first_stat) != 0) return false;
  if (lstat(second.c_str(), &second_stat) != 0) return false;
  return first_stat.st_dev == second_stat.st_dev;
}

// Returns the highest ancestor of [path] on the same device. Walking stops
// when the parent belongs to another mount, so removable media get their own
// trash instead of a cross-device move into the home trash.
std::filesystem::path mount_root_of(const std::filesystem::path& path) {
  struct stat path_stat {};
  if (lstat(path.c_str(), &path_stat) != 0) return {};
  std::filesystem::path current = path;
  while (current.has_parent_path()) {
    const std::filesystem::path parent = current.parent_path();
    if (parent == current) break;
    struct stat parent_stat {};
    if (stat(parent.c_str(), &parent_stat) != 0) break;
    if (parent_stat.st_dev != path_stat.st_dev) break;
    current = parent;
  }
  return current;
}

bool ensure_layout(const std::filesystem::path& trash_root,
                   std::filesystem::path* files,
                   std::filesystem::path* info) {
  std::filesystem::path root = trash_root;
  // The top-directory variant must be created with the owner-only mode the
  // specification requires; the home trash inherits the user's data directory
  // permissions.
  std::error_code code;
  std::filesystem::create_directories(root / "files", code);
  if (code) return false;
  std::filesystem::create_directories(root / "info", code);
  if (code) return false;
  *files = root / "files";
  *info = root / "info";
  return true;
}

std::filesystem::path top_directory_trash(const std::filesystem::path& mount_root,
                                          bool* create_uid_variant) {
  *create_uid_variant = false;
  const std::filesystem::path shared = mount_root / ".Trash";
  std::error_code code;
  if (std::filesystem::is_directory(shared, code) && !code) {
    return shared / std::to_string(getuid());
  }
  // Fall back to the per-user variant at the top of the volume.
  *create_uid_variant = true;
  return mount_root / (".Trash-" + std::to_string(getuid()));
}

std::string percent_encode_path(const std::string& path) {
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

std::string deletion_date() {
  std::time_t now = std::time(nullptr);
  std::tm local {};
  localtime_r(&now, &local);
  char buffer[32] = {};
  std::strftime(buffer, sizeof(buffer), "%Y-%m-%dT%H:%M:%S", &local);
  return buffer;
}

bool write_info_file(const std::filesystem::path& info_dir,
                     const std::string& name,
                     const std::filesystem::path& source,
                     std::string* error) {
  const std::filesystem::path target = info_dir / (name + ".trashinfo");
  const std::filesystem::path temporary = target.string() + ".tmp";
  {
    std::ofstream stream(temporary, std::ios::binary | std::ios::trunc);
    if (!stream) {
      if (error != nullptr) *error = "cannot write trash info";
      return false;
    }
    stream << "[Trash Info]\n";
    stream << "Path=" << percent_encode_path(source.generic_string()) << "\n";
    stream << "DeletionDate=" << deletion_date() << "\n";
    if (!stream) {
      if (error != nullptr) *error = "cannot write trash info";
      return false;
    }
  }
  std::error_code code;
  std::filesystem::rename(temporary, target, code);
  if (code) {
    std::filesystem::remove(temporary, code);
    if (error != nullptr) *error = "cannot store trash info";
    return false;
  }
  return true;
}

bool trash_into(const std::filesystem::path& trash_root,
                const std::filesystem::path& source,
                std::string* error) {
  std::filesystem::path files;
  std::filesystem::path info;
  if (!ensure_layout(trash_root, &files, &info)) {
    if (error != nullptr) *error = "cannot create trash directories";
    return false;
  }

  const std::string base = source.filename().string();
  std::error_code code;
  for (size_t attempt = 0; attempt < kMaxTrashNameAttempts; ++attempt) {
    const std::string candidate =
        attempt == 0 ? base : base + "." + std::to_string(attempt);
    const std::filesystem::path destination = files / candidate;
    const std::filesystem::path info_file = info / (candidate + ".trashinfo");
    if (std::filesystem::exists(destination, code) ||
        std::filesystem::exists(info_file, code)) {
      continue;
    }

    if (!write_info_file(info, candidate, source, error)) return false;

    std::filesystem::rename(source, destination, code);
    if (code) {
      std::error_code ignored;
      std::filesystem::remove(info_file, ignored);
      if (error != nullptr) *error = "cannot move the item to the trash";
      return false;
    }
    return true;
  }

  if (error != nullptr) *error = "no free trash name";
  return false;
}

}  // namespace

bool move_to_trash(const std::string& path,
                   std::string* backend,
                   std::string* error) {
  if (!is_safe_trash_path(path)) {
    if (error != nullptr) *error = "invalid trash path";
    return false;
  }

  const std::filesystem::path source(path);
  const auto data_home = user_data_home();
  if (data_home.empty()) {
    if (error != nullptr) *error = "home directory unavailable";
    return false;
  }

  const std::filesystem::path home_trash = data_home / "Trash";
  // Same-device rename keeps the operation atomic. For another volume the
  // specification wants the trash at the top of that volume, which we only
  // use when it already exists or can be created by this user.
  const bool same_as_home = same_device(source, data_home);
  if (same_as_home) {
    if (trash_into(home_trash, source, error)) {
      if (backend != nullptr) *backend = "freedesktop";
      return true;
    }
    return false;
  }

  const std::filesystem::path mount_root = mount_root_of(source);
  if (!mount_root.empty()) {
    bool create_uid_variant = false;
    const std::filesystem::path volume_trash =
        top_directory_trash(mount_root, &create_uid_variant);
    std::error_code code;
    if (create_uid_variant) {
      std::filesystem::create_directories(volume_trash, code);
      if (!code) {
        std::filesystem::permissions(
            volume_trash,
            std::filesystem::perms::owner_all,
            std::filesystem::perm_options::replace,
            code);
      }
    }
    if (!code && trash_into(volume_trash, source, error)) {
      if (backend != nullptr) *backend = "freedesktop";
      return true;
    }
  }

  if (error != nullptr) *error = "no trash available on this volume";
  return false;
}

}  // namespace archoera
