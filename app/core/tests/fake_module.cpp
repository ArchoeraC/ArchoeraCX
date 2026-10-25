/*
 * Fake native module used by the C++ module host smoke test.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Implements the frozen module ABI only. It never touches the real business
 * layer: java.scan returns one synthetic Java entry so the host can be tested
 * without the C# module being present.
 */

#include "archoera_module_abi.h"

#include <condition_variable>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

namespace {

constexpr const char* kScanResult =
    "{\"ok\":true,\"data\":{\"entries\":[{\"path\":\"/opt/java/fake-java/bin/"
    "java\",\"home\":\"/opt/java/fake-java\",\"version\":\"17.0.9\","
    "\"vendor\":\"FakeCorp\",\"major\":17}],\"count\":1},"
    "\"meta\":{\"schemaVersion\":1}}";

constexpr const char* kCancelledResult =
    "{\"ok\":false,\"error\":{\"code\":6,\"message\":\"cancelled\"},"
    "\"meta\":{\"schemaVersion\":1}}";

struct FakeSelf {
  std::mutex mutex;
  std::condition_variable cv;
  std::unordered_map<uint64_t, bool> cancel_requested;
  std::vector<std::thread> threads;
  bool shutting_down = false;
};

bool wants_async(const char* params_json) {
  if (params_json == nullptr) {
    return false;
  }
  return std::strstr(params_json, "\"mode\":\"async\"") != nullptr;
}

int32_t fake_init(const char* config_json, void** out_self,
                  archoera_error* err) {
  (void)config_json;
  if (out_self == nullptr) {
    return ARCHOERA_E_INVALID;
  }
  *out_self = nullptr;
  try {
    *out_self = new FakeSelf();
  } catch (...) {
    if (err != nullptr) {
      err->code = ARCHOERA_E_INTERNAL;
      err->message = "fake module init failed";
      err->detail = nullptr;
    }
    return ARCHOERA_E_INTERNAL;
  }
  if (err != nullptr) {
    err->code = ARCHOERA_OK;
    err->message = nullptr;
    err->detail = nullptr;
  }
  return ARCHOERA_OK;
}

int32_t fake_call(void* self,
                  uint64_t task_id,
                  const char* method,
                  const char* params_json,
                  archoera_module_done_fn done,
                  archoera_module_progress_fn progress,
                  void* user) {
  (void)progress;
  auto* state = static_cast<FakeSelf*>(self);
  if (state == nullptr || method == nullptr || done == nullptr) {
    return ARCHOERA_E_INVALID;
  }
  if (std::strcmp(method, "java.scan") != 0) {
    return ARCHOERA_E_UNSUPPORTED;
  }

  if (params_json != nullptr &&
      std::strstr(params_json, "\"mode\":\"reject\"") != nullptr) {
    // Accepted as a valid rejection probe: no done callback follows.
    return ARCHOERA_E_INTERNAL;
  }

  try {
    if (!wants_async(params_json)) {
      // Synchronous callback: still valid, `call` may invoke done before it
      // returns.
      done(user, ARCHOERA_OK, kScanResult);
      return ARCHOERA_OK;
    }

    {
      std::lock_guard<std::mutex> lock(state->mutex);
      state->cancel_requested[task_id] = false;
      state->threads.emplace_back([state, task_id, done, user] {
        std::unique_lock<std::mutex> lock(state->mutex);
        state->cv.wait(lock, [&] {
          if (state->shutting_down) {
            return true;
          }
          const auto it = state->cancel_requested.find(task_id);
          return it != state->cancel_requested.end() && it->second;
        });

        const bool shutting_down = state->shutting_down;
        const auto it = state->cancel_requested.find(task_id);
        const bool cancelled =
            it != state->cancel_requested.end() && it->second;
        state->cancel_requested.erase(task_id);
        lock.unlock();

        // No callback may follow shutdown; dropping the result is enough
        // because the core ignores events while it is shutting down.
        if (shutting_down) {
          return;
        }
        if (cancelled) {
          done(user, ARCHOERA_E_CANCELLED, kCancelledResult);
        }
      });
    }
    return ARCHOERA_OK;
  } catch (...) {
    return ARCHOERA_E_INTERNAL;
  }
}

int32_t fake_cancel(void* self, uint64_t task_id) {
  auto* state = static_cast<FakeSelf*>(self);
  if (state == nullptr) {
    return ARCHOERA_OK;
  }
  try {
    std::lock_guard<std::mutex> lock(state->mutex);
    const auto it = state->cancel_requested.find(task_id);
    if (it != state->cancel_requested.end()) {
      it->second = true;
      state->cv.notify_all();
    }
    return ARCHOERA_OK;
  } catch (...) {
    return ARCHOERA_E_INTERNAL;
  }
}

void fake_shutdown(void* self) {
  auto* state = static_cast<FakeSelf*>(self);
  if (state == nullptr) {
    return;
  }

  std::vector<std::thread> threads;
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    state->shutting_down = true;
    state->cv.notify_all();
    threads.swap(state->threads);
  }

  for (std::thread& thread : threads) {
    if (thread.joinable()) {
      thread.join();
    }
  }

  delete state;
}

const archoera_module_v1 kModule = {
    ARCHOERA_MODULE_ABI_VERSION,
    "archoera_fake",
    "0.0.1+1",
    &fake_init,
    &fake_call,
    &fake_cancel,
    &fake_shutdown,
};

}  // namespace

extern "C" ARCHOERACX_MODULE_API const archoera_module_v1*
archoera_module_entry(void) {
  return &kModule;
}
