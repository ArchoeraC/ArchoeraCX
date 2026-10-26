/*
 * Platform single-instance smoke test.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * Exercises the native lock + local activation channel without the core or the
 * Flutter message loop. A unique name keeps the test from contending with a
 * launcher that is already open on the developer machine.
 */

#include "platform_instance.h"

#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <mutex>
#include <string>

#if defined(_WIN32)
#  include <process.h>
#else
#  include <unistd.h>
#endif

namespace {

int process_id() {
#if defined(_WIN32)
  return _getpid();
#else
  return static_cast<int>(::getpid());
#endif
}

#define CHECK(cond)                                                       \
  do {                                                                    \
    if (!(cond)) {                                                        \
      std::fprintf(stderr, "FAILED %s:%d: %s\n", __FILE__, __LINE__,      \
                   #cond);                                                \
      return 1;                                                           \
    }                                                                     \
  } while (0)

}  // namespace

int main() {
  const std::string name =
      "archoera-cx-smoke-" + std::to_string(process_id());
  std::string error;

  auto primary = archoera::create_platform_instance(name);
  CHECK(primary != nullptr);
  bool primary_flag = false;
  CHECK(primary->acquire(&primary_flag, &error));
  CHECK(primary_flag);

  std::mutex wake_mutex;
  std::condition_variable wake_cv;
  int activations = 0;
  CHECK(primary->listen(
      [&] {
        std::lock_guard<std::mutex> lock(wake_mutex);
        ++activations;
        wake_cv.notify_all();
      },
      &error));

  auto secondary = archoera::create_platform_instance(name);
  CHECK(secondary != nullptr);
  bool secondary_primary = true;
  CHECK(secondary->acquire(&secondary_primary, &error));
  CHECK(!secondary_primary);
  CHECK(secondary->notify(&error));

  {
    std::unique_lock<std::mutex> lock(wake_mutex);
    CHECK(wake_cv.wait_for(lock, std::chrono::seconds(3),
                           [&] { return activations == 1; }));
  }

  // stop() on a secondary must not unlink the primary's socket or release its
  // lock. A later secondary must still reach the primary.
  secondary->stop();
  auto later = archoera::create_platform_instance(name);
  CHECK(later != nullptr);
  bool later_primary = true;
  CHECK(later->acquire(&later_primary, &error));
  CHECK(!later_primary);
  CHECK(later->notify(&error));
  {
    std::unique_lock<std::mutex> lock(wake_mutex);
    CHECK(wake_cv.wait_for(lock, std::chrono::seconds(3),
                           [&] { return activations == 2; }));
  }
  later->stop();
  primary->stop();

  // The lock must be released by stop(), so a fresh process-like instance can
  // become primary again. This catches an accidental process-lifetime handle.
  auto next = archoera::create_platform_instance(name);
  CHECK(next != nullptr);
  bool next_primary = false;
  CHECK(next->acquire(&next_primary, &error));
  CHECK(next_primary);
  next->stop();

  std::puts("single_instance_smoke: OK");
  return 0;
}
