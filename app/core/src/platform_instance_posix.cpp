/*
 * POSIX single-instance lock and activation channel.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Lock: flock(LOCK_EX|LOCK_NB) on a per-user file in XDG_RUNTIME_DIR (or
 * /tmp when the session does not provide one). The kernel releases the lock
 * when the process exits, so a crash never leaves a permanent stale lock.
 *
 * Channel: a Unix-domain socket next to the lock file. The primary accepts
 * one short "activate" message at a time; a secondary connects and writes it.
 * The socket is unlinked by the primary at startup and at shutdown. If the
 * primary died without unlinking, the next primary removes the stale path
 * after taking the lock.
 */

#include "platform_instance.h"

#include <sys/file.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <sys/un.h>

#include <fcntl.h>
#include <poll.h>
#include <unistd.h>

#include <atomic>
#include <cerrno>
#include <chrono>
#include <cctype>
#include <cstddef>
#include <cstdlib>
#include <cstring>
#include <string_view>
#include <thread>
#include <utility>

namespace archoera {
namespace {

constexpr const char* kActivationMessage = "activate";
constexpr auto kNotifyRetryDelay = std::chrono::milliseconds(20);
constexpr auto kNotifyTimeout = std::chrono::milliseconds(1500);

std::string sanitize_name(std::string name) {
  if (name.empty()) {
    return "archoera-cx";
  }
  for (char& value : name) {
    const unsigned char byte = static_cast<unsigned char>(value);
    if (!std::isalnum(byte) && value != '-' && value != '_') {
      value = '-';
    }
  }
  return name;
}

std::string runtime_directory() {
  const char* configured = std::getenv("XDG_RUNTIME_DIR");
  if (configured != nullptr && configured[0] == '/') {
    return configured;
  }
  // macOS has no XDG_RUNTIME_DIR; TMPDIR points at the per-user temp folder.
  const char* temporary = std::getenv("TMPDIR");
  if (temporary != nullptr && temporary[0] == '/') {
    return temporary;
  }
  return "/tmp";
}

std::string make_path(const std::string& directory,
                      const std::string& name,
                      const char* suffix) {
  return directory + "/." + name + "-" + std::to_string(::getuid()) + suffix;
}

class PosixPlatformInstance final : public PlatformInstance {
 public:
  explicit PosixPlatformInstance(std::string name)
      : lock_path_(make_path(runtime_directory(), sanitize_name(name),
                             ".lock")),
        socket_path_(make_path(runtime_directory(), sanitize_name(name),
                               ".sock")) {}

  ~PosixPlatformInstance() override { stop(); }

  bool acquire(bool* primary, std::string* error) override {
    if (primary == nullptr) {
      set_error(error, "primary output is null");
      return false;
    }
    *primary = false;

    if (lock_fd_ >= 0) {
      *primary = true;
      return true;
    }

    int flags = O_RDWR | O_CREAT | O_CLOEXEC;
#ifdef O_NOFOLLOW
    // The fallback path lives in a shared /tmp on systems without
    // XDG_RUNTIME_DIR; never follow a symlink planted there.
    flags |= O_NOFOLLOW;
#endif
    const int fd = ::open(lock_path_.c_str(), flags, S_IRUSR | S_IWUSR);
    if (fd < 0) {
      set_error(error, "cannot open the single-instance lock file");
      return false;
    }
    struct stat metadata {};
    if (::fstat(fd, &metadata) != 0 || !S_ISREG(metadata.st_mode) ||
        metadata.st_uid != ::getuid()) {
      ::close(fd);
      set_error(error,
                "the single-instance lock file is not owned by this user");
      return false;
    }
    (void)::fchmod(fd, S_IRUSR | S_IWUSR);

    if (::flock(fd, LOCK_EX | LOCK_NB) != 0) {
      const int code = errno;
      ::close(fd);
      if (code == EWOULDBLOCK || code == EAGAIN) {
        return true;
      }
      set_error(error, "cannot lock the single-instance file");
      return false;
    }

    lock_fd_ = fd;
    *primary = true;
    return true;
  }

  bool listen(std::function<void()> on_activate,
              std::string* error) override {
    if (lock_fd_ < 0) {
      set_error(error, "the single-instance lock was not acquired");
      return false;
    }
    if (listening_) {
      return true;
    }

    if (socket_path_.size() >= sizeof(sockaddr_un::sun_path)) {
      set_error(error, "the single-instance socket path is too long");
      return false;
    }

    // The lock is ours, so any existing socket belongs to a dead process.
    (void)::unlink(socket_path_.c_str());

    const int fd = ::socket(AF_UNIX, SOCK_STREAM, 0);
    if (fd < 0) {
      set_error(error, "cannot create the single-instance socket");
      return false;
    }
    (void)::fcntl(fd, F_SETFD, FD_CLOEXEC);

    sockaddr_un address{};
    address.sun_family = AF_UNIX;
    std::memcpy(address.sun_path, socket_path_.c_str(), socket_path_.size());
    address.sun_path[socket_path_.size()] = '\0';

    if (::bind(fd, reinterpret_cast<const sockaddr*>(&address),
               sizeof(address)) != 0) {
      ::close(fd);
      set_error(error, "cannot bind the single-instance socket");
      return false;
    }
    owns_socket_ = true;
    if (::chmod(socket_path_.c_str(), S_IRUSR | S_IWUSR) != 0) {
      ::close(fd);
      (void)::unlink(socket_path_.c_str());
      owns_socket_ = false;
      set_error(error, "cannot restrict the single-instance socket");
      return false;
    }

    if (::listen(fd, 4) != 0) {
      ::close(fd);
      (void)::unlink(socket_path_.c_str());
      owns_socket_ = false;
      set_error(error, "cannot listen on the single-instance socket");
      return false;
    }

    listen_fd_ = fd;
    on_activate_ = std::move(on_activate);
    stopping_.store(false, std::memory_order_release);
    try {
      thread_ = std::thread(&PosixPlatformInstance::listen_loop, this);
    } catch (...) {
      on_activate_ = nullptr;
      ::close(listen_fd_);
      listen_fd_ = -1;
      (void)::unlink(socket_path_.c_str());
      owns_socket_ = false;
      set_error(error, "cannot start the single-instance listener");
      return false;
    }
    listening_ = true;
    return true;
  }

  bool notify(std::string* error) override {
    if (socket_path_.size() >= sizeof(sockaddr_un::sun_path)) {
      set_error(error, "the single-instance socket path is too long");
      return false;
    }

    sockaddr_un address{};
    address.sun_family = AF_UNIX;
    std::memcpy(address.sun_path, socket_path_.c_str(), socket_path_.size());
    address.sun_path[socket_path_.size()] = '\0';

    const auto deadline =
        std::chrono::steady_clock::now() + kNotifyTimeout;
    for (;;) {
      const int fd = ::socket(AF_UNIX, SOCK_STREAM, 0);
      if (fd >= 0) {
        (void)::fcntl(fd, F_SETFD, FD_CLOEXEC);
#ifdef SO_NOSIGPIPE
        int enabled = 1;
        (void)::setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &enabled,
                           sizeof(enabled));
#endif
        if (::connect(fd, reinterpret_cast<const sockaddr*>(&address),
                      sizeof(address)) == 0) {
          int flags = 0;
#ifdef MSG_NOSIGNAL
          flags = MSG_NOSIGNAL;
#endif
          const ssize_t written =
              ::send(fd, kActivationMessage, std::strlen(kActivationMessage),
                     flags);
          ::close(fd);
          if (written ==
              static_cast<ssize_t>(std::strlen(kActivationMessage))) {
            return true;
          }
        } else {
          ::close(fd);
        }
      }

      if (std::chrono::steady_clock::now() >= deadline) {
        break;
      }
      std::this_thread::sleep_for(kNotifyRetryDelay);
    }

    set_error(error, "the primary instance did not accept activation");
    return false;
  }

  void stop() override {
    stopping_.store(true, std::memory_order_release);
    if (thread_.joinable()) {
      thread_.join();
    }
    listening_ = false;
    on_activate_ = nullptr;

    if (listen_fd_ >= 0) {
      ::close(listen_fd_);
      listen_fd_ = -1;
    }
    if (owns_socket_) {
      (void)::unlink(socket_path_.c_str());
      owns_socket_ = false;
    }
    if (lock_fd_ >= 0) {
      ::close(lock_fd_);
      lock_fd_ = -1;
    }
  }

 private:
  void listen_loop() noexcept {
    while (!stopping_.load(std::memory_order_acquire)) {
      pollfd descriptor{};
      descriptor.fd = listen_fd_;
      descriptor.events = POLLIN;

      const int ready = ::poll(&descriptor, 1, 250);
      if (ready < 0) {
        if (errno == EINTR) {
          continue;
        }
        break;
      }
      if (ready == 0) {
        continue;
      }
      if ((descriptor.revents & POLLIN) == 0) {
        if ((descriptor.revents & (POLLERR | POLLHUP | POLLNVAL)) != 0) {
          break;
        }
        continue;
      }

      const int client = ::accept(listen_fd_, nullptr, nullptr);
      if (client < 0) {
        if (errno == EINTR) {
          continue;
        }
        break;
      }
      (void)::fcntl(client, F_SETFD, FD_CLOEXEC);

      char buffer[64] = {};
      const ssize_t count = ::read(client, buffer, sizeof(buffer) - 1);
      ::close(client);
      if (count <= 0) {
        continue;
      }

      const std::string_view message(buffer, static_cast<size_t>(count));
      if (message.find(kActivationMessage) == std::string_view::npos) {
        continue;
      }

      try {
        if (on_activate_) {
          on_activate_();
        }
      } catch (...) {
        // A failed wake must not kill the listener thread.
      }
    }
  }

  static void set_error(std::string* error, const char* message) {
    if (error != nullptr) {
      *error = message;
    }
  }

  std::string lock_path_;
  std::string socket_path_;
  int lock_fd_ = -1;
  int listen_fd_ = -1;
  bool owns_socket_ = false;
  bool listening_ = false;
  std::atomic<bool> stopping_{false};
  std::thread thread_;
  std::function<void()> on_activate_;
};

}  // namespace

std::unique_ptr<PlatformInstance> create_platform_instance(
    const std::string& name) {
  return std::make_unique<PosixPlatformInstance>(name);
}

}  // namespace archoera
