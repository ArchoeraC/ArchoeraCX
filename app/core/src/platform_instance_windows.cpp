/*
 * Windows single-instance lock and activation channel.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Lock: CreateMutexW("Local\\<app>.SingleInstance"). "Local\" is scoped to the
 * logon session, so two Windows users can each run one launcher while a second
 * launch in the same session fails with ERROR_ALREADY_EXISTS.
 *
 * Channel: a byte-mode named pipe with the session id in its name. The primary
 * listens with overlapped I/O so shutdown can cancel the pending accept/read
 * instead of waiting for a client. The secondary connects, writes one
 * "activate" message and exits.
 */

#include "platform_instance.h"

#include <windows.h>

// Present since Vista; keep the build working with older SDK headers while
// retaining the flag's value.
#ifndef PIPE_REJECT_REMOTE_CLIENTS
#  define PIPE_REJECT_REMOTE_CLIENTS 0x00000008
#endif

#include <atomic>
#include <chrono>
#include <cctype>
#include <cstring>
#include <string>
#include <string_view>
#include <thread>
#include <utility>

namespace archoera {
namespace {

constexpr char kActivationMessage[] = "activate";
constexpr DWORD kNotifyTimeoutMs = 1500;

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

std::wstring widen_ascii(const std::string& value) {
  return std::wstring(value.begin(), value.end());
}

std::wstring pipe_name_for(const std::wstring& base) {
  DWORD session = 0;
  (void)::ProcessIdToSessionId(::GetCurrentProcessId(), &session);
  return L"\\\\.\\pipe\\" + base + L"." + std::to_wstring(session) +
         L".activate";
}

class WindowsPlatformInstance final : public PlatformInstance {
 public:
  explicit WindowsPlatformInstance(std::string name)
      : base_name_(widen_ascii(sanitize_name(std::move(name)))),
        mutex_name_(L"Local\\" + base_name_ + L".SingleInstance"),
        pipe_name_(pipe_name_for(base_name_)) {}

  ~WindowsPlatformInstance() override { stop(); }

  bool acquire(bool* primary, std::string* error) override {
    if (primary == nullptr) {
      set_error(error, "primary output is null");
      return false;
    }
    *primary = false;

    if (mutex_ != nullptr) {
      *primary = true;
      return true;
    }

    ::SetLastError(ERROR_SUCCESS);
    HANDLE handle = ::CreateMutexW(nullptr, FALSE, mutex_name_.c_str());
    if (handle == nullptr) {
      set_error(error, "cannot create the single-instance mutex");
      return false;
    }

    if (::GetLastError() == ERROR_ALREADY_EXISTS) {
      ::CloseHandle(handle);
      return true;
    }

    mutex_ = handle;
    *primary = true;
    return true;
  }

  bool listen(std::function<void()> on_activate,
              std::string* error) override {
    if (mutex_ == nullptr) {
      set_error(error, "the single-instance mutex was not acquired");
      return false;
    }
    if (listening_) {
      return true;
    }

    if (stop_event_ != nullptr) {
      ::CloseHandle(stop_event_);
      stop_event_ = nullptr;
    }
    stop_event_ = ::CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (stop_event_ == nullptr) {
      set_error(error, "cannot create the single-instance stop event");
      return false;
    }

    on_activate_ = std::move(on_activate);
    stopping_.store(false, std::memory_order_release);
    try {
      thread_ = std::thread(&WindowsPlatformInstance::listen_loop, this);
    } catch (...) {
      on_activate_ = nullptr;
      ::CloseHandle(stop_event_);
      stop_event_ = nullptr;
      set_error(error, "cannot start the single-instance listener");
      return false;
    }
    listening_ = true;
    return true;
  }

  bool notify(std::string* error) override {
    const ULONGLONG deadline = ::GetTickCount64() + kNotifyTimeoutMs;
    const DWORD length = static_cast<DWORD>(std::strlen(kActivationMessage));

    for (;;) {
      HANDLE pipe = ::CreateFileW(pipe_name_.c_str(), GENERIC_WRITE, 0,
                                  nullptr, OPEN_EXISTING, 0, nullptr);
      if (pipe != INVALID_HANDLE_VALUE) {
        DWORD written = 0;
        const BOOL ok = ::WriteFile(pipe, kActivationMessage, length, &written,
                                    nullptr);
        if (ok && written == length) {
          (void)::FlushFileBuffers(pipe);
          ::CloseHandle(pipe);
          return true;
        }
        ::CloseHandle(pipe);
      } else if (::GetLastError() == ERROR_PIPE_BUSY) {
        (void)::WaitNamedPipeW(pipe_name_.c_str(), 50);
      } else {
        ::Sleep(20);
      }

      if (::GetTickCount64() >= deadline) {
        break;
      }
    }

    set_error(error, "the primary instance did not accept activation");
    return false;
  }

  void stop() override {
    stopping_.store(true, std::memory_order_release);
    if (stop_event_ != nullptr) {
      (void)::SetEvent(stop_event_);
    }
    if (thread_.joinable()) {
      thread_.join();
    }

    listening_ = false;
    on_activate_ = nullptr;
    if (stop_event_ != nullptr) {
      ::CloseHandle(stop_event_);
      stop_event_ = nullptr;
    }
    if (mutex_ != nullptr) {
      ::CloseHandle(mutex_);
      mutex_ = nullptr;
    }
  }

 private:
  void listen_loop() noexcept {
    for (;;) {
      if (stopping_.load(std::memory_order_acquire)) {
        break;
      }

      HANDLE pipe = ::CreateNamedPipeW(
          pipe_name_.c_str(),
          PIPE_ACCESS_INBOUND | FILE_FLAG_OVERLAPPED |
              PIPE_REJECT_REMOTE_CLIENTS,
          PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1, 0, 256, 0,
          nullptr);
      if (pipe == INVALID_HANDLE_VALUE) {
        if (wait_for_stop(50)) {
          break;
        }
        continue;
      }

      HANDLE io_event = ::CreateEventW(nullptr, TRUE, FALSE, nullptr);
      if (io_event == nullptr) {
        ::CloseHandle(pipe);
        if (wait_for_stop(50)) {
          break;
        }
        continue;
      }

      OVERLAPPED overlapped{};
      overlapped.hEvent = io_event;
      bool connected = false;

      (void)::ResetEvent(io_event);
      if (::ConnectNamedPipe(pipe, &overlapped)) {
        connected = true;
      } else {
        const DWORD code = ::GetLastError();
        if (code == ERROR_PIPE_CONNECTED) {
          connected = true;
        } else if (code == ERROR_IO_PENDING) {
          connected = wait_for_io(pipe, &overlapped, nullptr);
        }
      }

      bool activated = false;
      if (connected) {
        char buffer[64] = {};
        DWORD read = 0;
        (void)::ResetEvent(io_event);
        const BOOL read_ok =
            ::ReadFile(pipe, buffer, sizeof(buffer) - 1, &read, &overlapped);
        if (!read_ok && ::GetLastError() == ERROR_IO_PENDING) {
          DWORD transferred = 0;
          if (wait_for_io(pipe, &overlapped, &transferred)) {
            read = transferred;
          } else {
            read = 0;
          }
        }

        if (read > 0) {
          const std::string_view message(buffer, static_cast<size_t>(read));
          activated =
              message.find(kActivationMessage) != std::string_view::npos;
        }
      }

      (void)::DisconnectNamedPipe(pipe);
      ::CloseHandle(io_event);
      ::CloseHandle(pipe);

      if (activated) {
        try {
          if (on_activate_) {
            on_activate_();
          }
        } catch (...) {
          // A failed wake must not kill the listener thread.
        }
      }
    }
  }

  bool wait_for_stop(DWORD millis) const {
    return stop_event_ != nullptr &&
           ::WaitForSingleObject(stop_event_, millis) == WAIT_OBJECT_0;
  }

  bool wait_for_io(HANDLE pipe,
                   OVERLAPPED* overlapped,
                   DWORD* transferred) {
    HANDLE handles[2] = {overlapped->hEvent, stop_event_};
    const DWORD ready =
        ::WaitForMultipleObjects(2, handles, FALSE, INFINITE);
    if (ready == WAIT_OBJECT_0) {
      DWORD count = 0;
      if (::GetOverlappedResult(pipe, overlapped, &count, FALSE) == 0) {
        return false;
      }
      if (transferred != nullptr) {
        *transferred = count;
      }
      return true;
    }

    // Shutdown won the race: cancel the pending accept/read and drain the
    // completion so the pipe handle can be closed safely.
    (void)::CancelIoEx(pipe, overlapped);
    DWORD count = 0;
    (void)::GetOverlappedResult(pipe, overlapped, &count, TRUE);
    if (transferred != nullptr) {
      *transferred = 0;
    }
    return false;
  }

  static void set_error(std::string* error, const char* message) {
    if (error != nullptr) {
      *error = message;
    }
  }

  std::wstring base_name_;
  std::wstring mutex_name_;
  std::wstring pipe_name_;
  HANDLE mutex_ = nullptr;
  HANDLE stop_event_ = nullptr;
  bool listening_ = false;
  std::atomic<bool> stopping_{false};
  std::thread thread_;
  std::function<void()> on_activate_;
};

}  // namespace

std::unique_ptr<PlatformInstance> create_platform_instance(
    const std::string& name) {
  return std::make_unique<WindowsPlatformInstance>(name);
}

}  // namespace archoera
