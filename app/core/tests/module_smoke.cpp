/*
 * Module host smoke test for ArchoeraCX ABI v1.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Uses a fake native module (libarchoera_fake_module.so) as the oracle:
 *   - java.scan is routed to the module on a core worker thread
 *   - the synchronous done callback completes the task and publishes an event
 *   - the result envelope survives the done callback lifetime
 *   - cancel reaches the module and yields CANCELLED
 *   - shutdown joins module callbacks before deleting the core
 *
 * No sleeps or polling loops: the harness waits on a condition variable that
 * the core's wake callback signals.
 */

#include "archoera_abi.h"

#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>

#ifndef ARCHOERACX_FAKE_MODULE_PATH
#  error "ARCHOERACX_FAKE_MODULE_PATH must be defined by the build"
#endif

namespace {

struct WakeState {
  std::mutex mutex;
  std::condition_variable cv;
  unsigned counter = 0;
};

struct CoreGuard {
  archoera_core* core = nullptr;

  ~CoreGuard() {
    if (core != nullptr) {
      archoera_shutdown(core);
    }
  }
};

bool wait_for_next_wake(WakeState& state,
                        unsigned& consumed,
                        std::chrono::milliseconds timeout) {
  std::unique_lock<std::mutex> lock(state.mutex);
  const bool woken = state.cv.wait_for(lock, timeout, [&] {
    return state.counter > consumed;
  });
  if (woken) {
    consumed = state.counter;
  }
  return woken;
}

bool set_module_env(const char* path) {
#if defined(_WIN32)
  return _putenv_s("ARCHOERACX_CS_MODULE", path) == 0;
#else
  return setenv("ARCHOERACX_CS_MODULE", path, 1) == 0;
#endif
}

}  // namespace

extern "C" void module_smoke_wake(void* user) {
  auto* state = static_cast<WakeState*>(user);
  if (state == nullptr) {
    return;
  }
  {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->counter;
  }
  state->cv.notify_all();
}

namespace {

#define CHECK(cond)                                                       \
  do {                                                                    \
    if (!(cond)) {                                                        \
      std::fprintf(stderr, "FAILED %s:%d: %s\n", __FILE__, __LINE__,      \
                   #cond);                                                \
      return 1;                                                           \
    }                                                                     \
  } while (0)

int run_tests() {
  constexpr auto kWaitTimeout = std::chrono::seconds(3);

  CHECK(set_module_env(ARCHOERACX_FAKE_MODULE_PATH));

  WakeState wake;
  CoreGuard guard;
  archoera_core* core = nullptr;
  archoera_error err = {};

  CHECK(archoera_init("{\"safeMode\":true}", &core, &err) == ARCHOERA_OK);
  CHECK(core != nullptr);
  guard.core = core;

  CHECK(archoera_set_wakeup(core, &module_smoke_wake, &wake) == ARCHOERA_OK);

  // --- java.scan: synchronous module done, asynchronous core task ----------
  uint64_t task = 0;
  CHECK(archoera_call(core, "java.scan", "{\"roots\":[\"/opt/java\"]}", &task,
                      &err) == ARCHOERA_OK);
  CHECK(task != 0);

  bool saw_completed = false;
  unsigned consumed = 0;
  for (int iteration = 0; iteration < 16 && !saw_completed; ++iteration) {
    CHECK(wait_for_next_wake(wake, consumed, kWaitTimeout));

    archoera_event events[64];
    const int32_t count = archoera_poll(core, events, 64);
    CHECK(count >= 0);

    for (int32_t i = 0; i < count; ++i) {
      const archoera_event& event = events[i];
      if (event.task_id != task) {
        continue;
      }
      if (event.type == ARCHOERA_EV_TASK_STATE &&
          event.state == ARCHOERA_TASK_COMPLETED) {
        saw_completed = true;
        CHECK(event.json != nullptr);
        CHECK(std::strstr(event.json, "\"phase\":\"completed\"") != nullptr);
      }
    }
  }
  CHECK(saw_completed);

  size_t len = 0;
  CHECK(archoera_result(core, task, nullptr, 0, &len, &err) == ARCHOERA_OK);
  CHECK(len > 1);

  std::string result(len, '\0');
  CHECK(archoera_result(core, task, result.data(), result.size(), &len, &err) ==
        ARCHOERA_OK);
  CHECK(result.find("\"ok\":true") != std::string::npos);
  CHECK(result.find("/opt/java/fake-java/bin/java") != std::string::npos);
  CHECK(result.find("\"count\":1") != std::string::npos);
  archoera_release(core, task);

  // --- java.scan: cancel is forwarded to the module -----------------------
  uint64_t cancel_task = 0;
  CHECK(archoera_call(core, "java.scan", "{\"mode\":\"async\"}", &cancel_task,
                      &err) == ARCHOERA_OK);
  CHECK(cancel_task != 0);
  CHECK(archoera_cancel(core, cancel_task) == ARCHOERA_OK);

  bool saw_cancelled = false;
  for (int iteration = 0; iteration < 16 && !saw_cancelled; ++iteration) {
    CHECK(wait_for_next_wake(wake, consumed, kWaitTimeout));

    archoera_event events[64];
    const int32_t count = archoera_poll(core, events, 64);
    CHECK(count >= 0);

    for (int32_t i = 0; i < count; ++i) {
      const archoera_event& event = events[i];
      if (event.task_id != cancel_task) {
        continue;
      }
      if (event.type == ARCHOERA_EV_TASK_STATE &&
          event.state == ARCHOERA_TASK_CANCELLED) {
        saw_cancelled = true;
        CHECK(event.json != nullptr);
        CHECK(std::strstr(event.json, "\"phase\":\"cancelled\"") != nullptr);
      }
    }
  }
  CHECK(saw_cancelled);

  len = 0;
  CHECK(archoera_result(core, cancel_task, nullptr, 0, &len, &err) ==
        ARCHOERA_OK);
  CHECK(len == 1);
  archoera_release(core, cancel_task);

  // --- java.scan: module rejects the call, task fails without done ---------
  uint64_t reject_task = 0;
  CHECK(archoera_call(core, "java.scan", "{\"mode\":\"reject\"}", &reject_task,
                      &err) == ARCHOERA_OK);
  CHECK(reject_task != 0);

  bool saw_failed = false;
  for (int iteration = 0; iteration < 16 && !saw_failed; ++iteration) {
    CHECK(wait_for_next_wake(wake, consumed, kWaitTimeout));

    archoera_event events[64];
    const int32_t count = archoera_poll(core, events, 64);
    CHECK(count >= 0);

    for (int32_t i = 0; i < count; ++i) {
      const archoera_event& event = events[i];
      if (event.task_id != reject_task) {
        continue;
      }
      if (event.type == ARCHOERA_EV_TASK_STATE &&
          event.state == ARCHOERA_TASK_FAILED) {
        saw_failed = true;
        CHECK(event.code == ARCHOERA_E_INTERNAL);
      }
    }
  }
  CHECK(saw_failed);

  len = 0;
  CHECK(archoera_result(core, reject_task, nullptr, 0, &len, &err) ==
        ARCHOERA_OK);
  {
    std::string failed_result(len, '\0');
    CHECK(archoera_result(core, reject_task, failed_result.data(),
                          failed_result.size(), &len, &err) == ARCHOERA_OK);
    CHECK(failed_result.find("\"ok\":false") != std::string::npos);
    CHECK(failed_result.find("\"code\":8") != std::string::npos);
  }
  archoera_release(core, reject_task);

  // --- unknown methods still fail synchronously ---------------------------
  uint64_t unknown_task = 123;
  CHECK(archoera_call(core, "java.unknown", "{}", &unknown_task, &err) ==
        ARCHOERA_E_UNSUPPORTED);
  CHECK(unknown_task == 0);

  // --- shutdown with an in-flight module task -----------------------------
  uint64_t inflight_task = 0;
  CHECK(archoera_call(core, "java.scan", "{\"mode\":\"async\"}", &inflight_task,
                      &err) == ARCHOERA_OK);
  CHECK(inflight_task != 0);
  archoera_shutdown(core);
  guard.core = nullptr;

  return 0;
}

#undef CHECK

}  // namespace

int main() {
  const int rc = run_tests();
  if (rc == 0) {
    std::printf("module_smoke: OK\n");
  }
  return rc;
}
