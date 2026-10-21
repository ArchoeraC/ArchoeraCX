/*
 * Event-driven async harness for ArchoeraCX ABI v1.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Verifies system.pingAsync end to end:
 *   - worker-thread progress events (1/3, 2/3, 3/3)
 *   - COMPLETED state and the synchronous ping result envelope
 *   - the registered wake callback is actually used
 *   - cooperative cancellation produces CANCELLED
 *   - shutdown joins the worker before deleting the core
 *
 * The harness never sleeps or spins waiting for events: it waits on a
 * condition variable signalled by the core's wake callback.
 */

#include "archoera_abi.h"

#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <thread>

namespace {

struct WakeState {
  std::mutex mutex;
  std::condition_variable cv;
  std::thread::id main_thread;
  unsigned counter = 0;
  bool saw_other_thread = false;
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

}  // namespace

extern "C" void async_smoke_wake(void* user) {
  auto* state = static_cast<WakeState*>(user);
  if (state == nullptr) {
    return;
  }

  {
    std::lock_guard<std::mutex> lock(state->mutex);
    ++state->counter;
    if (std::this_thread::get_id() != state->main_thread) {
      state->saw_other_thread = true;
    }
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
  constexpr auto kWaitTimeout = std::chrono::seconds(2);

  WakeState wake;
  wake.main_thread = std::this_thread::get_id();

  CoreGuard guard;
  archoera_core* core = nullptr;
  archoera_error err = {};

  CHECK(archoera_abi_version() == ARCHOERA_ABI_VERSION);
  CHECK(archoera_init("{\"safeMode\":true}", &core, &err) == ARCHOERA_OK);
  CHECK(core != nullptr);
  guard.core = core;

  CHECK(archoera_set_wakeup(core, &async_smoke_wake, &wake) == ARCHOERA_OK);

  uint64_t task = 0;
  CHECK(archoera_call(core, "system.pingAsync", "{}", &task, &err) ==
        ARCHOERA_OK);
  CHECK(task != 0);

  bool saw_progress[4] = {false, false, false, false};
  int progress_count = 0;
  bool saw_completed = false;
  uint64_t last_seq = 0;
  unsigned consumed = 0;

  // The worker emits exactly three progress events and one terminal state.
  // Each enqueue wakes us; we wait on the condition variable, then drain the
  // queue. This is event-driven, not a sleep/poll loop.
  for (int iteration = 0; iteration < 64 && !saw_completed; ++iteration) {
    CHECK(wait_for_next_wake(wake, consumed, kWaitTimeout));

    archoera_event events[64];
    const int32_t count = archoera_poll(core, events, 64);
    CHECK(count >= 0);

    for (int32_t i = 0; i < count; ++i) {
      const archoera_event& event = events[i];
      CHECK(event.task_id == task);
      CHECK(event.seq > last_seq);
      last_seq = event.seq;

      if (event.type == ARCHOERA_EV_TASK_PROGRESS) {
        ++progress_count;
        CHECK(event.total == 3);
        CHECK(event.done >= 1 && event.done <= 3);
        CHECK(event.json != nullptr);
        CHECK(std::strstr(event.json, "\"phase\":\"probe\"") != nullptr);
        saw_progress[event.done] = true;
      } else if (event.type == ARCHOERA_EV_TASK_STATE &&
                 event.state == ARCHOERA_TASK_COMPLETED) {
        saw_completed = true;
        CHECK(event.json != nullptr);
        CHECK(std::strstr(event.json, "\"phase\":\"completed\"") != nullptr);
      }
    }
  }

  CHECK(saw_completed);
  CHECK(progress_count >= 3);
  CHECK(saw_progress[1]);
  CHECK(saw_progress[2]);
  CHECK(saw_progress[3]);

  // The async state must have been published by a thread other than main.
  {
    std::lock_guard<std::mutex> lock(wake.mutex);
    CHECK(wake.saw_other_thread);
  }

  // Final result uses the same envelope as system.ping.
  size_t len = 0;
  CHECK(archoera_result(core, task, nullptr, 0, &len, &err) == ARCHOERA_OK);
  CHECK(len > 1);

  std::string result(len, '\0');
  CHECK(archoera_result(core, task, result.data(), result.size(), &len,
                        &err) == ARCHOERA_OK);
  CHECK(result.find("\"pong\":true") != std::string::npos);
  CHECK(result.find("\"method\":\"system.pingAsync\"") != std::string::npos);

  // Cooperative cancellation: the worker owns the CANCELLED transition.
  uint64_t cancel_task = 0;
  CHECK(archoera_call(core, "system.pingAsync", "{}", &cancel_task, &err) ==
        ARCHOERA_OK);
  CHECK(cancel_task != 0);
  CHECK(archoera_cancel(core, cancel_task) == ARCHOERA_OK);

  bool saw_cancelled = false;
  for (int iteration = 0; iteration < 64 && !saw_cancelled; ++iteration) {
    CHECK(wait_for_next_wake(wake, consumed, kWaitTimeout));

    archoera_event events[64];
    const int32_t count = archoera_poll(core, events, 64);
    CHECK(count >= 0);

    for (int32_t i = 0; i < count; ++i) {
      const archoera_event& event = events[i];
      CHECK(event.task_id == cancel_task);
      if (event.type == ARCHOERA_EV_TASK_STATE &&
          event.state == ARCHOERA_TASK_CANCELLED) {
        saw_cancelled = true;
        CHECK(event.json != nullptr);
        CHECK(std::strstr(event.json, "\"phase\":\"cancelled\"") != nullptr);
      }
    }
  }

  CHECK(saw_cancelled);

  // Cancelled tasks keep an empty result ("\0"), not the ping envelope.
  len = 0;
  CHECK(archoera_result(core, cancel_task, nullptr, 0, &len, &err) ==
        ARCHOERA_OK);
  CHECK(len == 1);

  archoera_release(core, task);
  archoera_release(core, cancel_task);

  // Shutdown must join a worker that is still mid-flight. The next progress
  // event is at least 25 ms away, so this exercises the cancel/join path
  // without waiting for the task to finish.
  uint64_t inflight_task = 0;
  CHECK(archoera_call(core, "system.pingAsync", "{}", &inflight_task, &err) ==
        ARCHOERA_OK);
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
    std::printf("async_smoke: OK\n");
  }
  return rc;
}
