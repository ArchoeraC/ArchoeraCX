/*
 * C harness for the ArchoeraCX ABI v1 vertical slice.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#define _POSIX_C_SOURCE 200809L

#include "archoera_abi.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  include <windows.h>
#else
#  include <time.h>
#endif

/*
 * The smoke harness polls for one asynchronous completion. Windows has no
 * nanosleep, so keep the wait in one helper instead of dropping the check.
 */
static void sleep_millis(long millis) {
#if defined(_WIN32)
  Sleep((DWORD)millis);
#else
  struct timespec pause = {0, millis * 1000L * 1000L};
  nanosleep(&pause, NULL);
#endif
}

#define CHECK(cond)                                                        \
  do {                                                                     \
    if (!(cond)) {                                                         \
      fprintf(stderr, "FAILED %s:%d: %s\n", __FILE__, __LINE__, #cond);    \
      rc = 1;                                                              \
      goto cleanup;                                                        \
    }                                                                      \
  } while (0)

int main(void) {
  int rc = 0;
  archoera_core* core = NULL;
  archoera_error err = {0};
  archoera_event events[64];
  uint64_t ping_task = 0;
  uint64_t info_task = 0;
  uint64_t modules_task = 0;
  uint64_t gpu_task = 0;
  uint64_t appearance_task = 0;
  uint64_t rejected_task = 123;
  uint64_t unknown_task = 123;
  uint64_t first_seq = 0;
  char* json = NULL;
  char tiny[4];
  size_t len = 0;
  int32_t count = 0;

  CHECK(archoera_abi_version() == ARCHOERA_ABI_VERSION);
  CHECK(archoera_build_info() != NULL);
  CHECK(archoera_build_info()[0] == '{');

  CHECK(archoera_init("{\"safeMode\":true}", &core, &err) == ARCHOERA_OK);
  CHECK(core != NULL);

  /* system.ping completes synchronously but still emits a TASK_STATE event. */
  CHECK(archoera_call(core, "system.ping", "{}", &ping_task, &err) ==
        ARCHOERA_OK);
  CHECK(ping_task != 0);

  count = archoera_poll(core, events, 64);
  CHECK(count == 1);
  CHECK(events[0].type == ARCHOERA_EV_TASK_STATE);
  CHECK(events[0].state == ARCHOERA_TASK_COMPLETED);
  CHECK(events[0].task_id == ping_task);
  CHECK(events[0].seq > 0);
  CHECK(events[0].json != NULL);
  first_seq = events[0].seq;

  /* Two-phase result read. */
  CHECK(archoera_result(core, ping_task, NULL, 0, &len, &err) == ARCHOERA_OK);
  CHECK(len > 1);
  json = (char*)malloc(len);
  CHECK(json != NULL);
  CHECK(archoera_result(core, ping_task, json, len, &len, &err) ==
        ARCHOERA_OK);
  CHECK(strstr(json, "\"pong\":true") != NULL);
  CHECK(strstr(json, "\"method\":\"system.ping\"") != NULL);
  CHECK(json[len - 1] == '\0');

  /* A too-small buffer reports E_BUFFER and the required size. */
  memset(tiny, 'X', sizeof(tiny));
  len = 0;
  CHECK(archoera_result(core, ping_task, tiny, sizeof(tiny), &len, &err) ==
        ARCHOERA_E_BUFFER);
  CHECK(len > sizeof(tiny));
  CHECK(tiny[0] == 'X');

  /* system.info is the other built-in method. */
  CHECK(archoera_call(core, "system.info", "{}", &info_task, &err) ==
        ARCHOERA_OK);
  CHECK(info_task != 0);
  CHECK(info_task != ping_task);

  count = archoera_poll(core, events, 64);
  CHECK(count == 1);
  CHECK(events[0].task_id == info_task);
  CHECK(events[0].state == ARCHOERA_TASK_COMPLETED);
  CHECK(events[0].seq > first_seq);

  free(json);
  json = NULL;
  len = 0;
  CHECK(archoera_result(core, info_task, NULL, 0, &len, &err) == ARCHOERA_OK);
  json = (char*)malloc(len);
  CHECK(json != NULL);
  CHECK(archoera_result(core, info_task, json, len, &len, &err) ==
        ARCHOERA_OK);
  CHECK(strstr(json, "\"data\":{\"abiVersion\":1") != NULL);
  CHECK(strstr(json, "\"modules\":[") != NULL);

  /* system.modules is resolved through the same explicit method registry. */
  CHECK(archoera_call(core, "system.modules", "{}", &modules_task, &err) ==
        ARCHOERA_OK);  CHECK(modules_task != 0);
  count = archoera_poll(core, events, 64);
  CHECK(count == 1);
  CHECK(events[0].task_id == modules_task);
  CHECK(events[0].state == ARCHOERA_TASK_COMPLETED);

  free(json);
  json = NULL;
  len = 0;
  CHECK(archoera_result(core, modules_task, NULL, 0, &len, &err) ==
        ARCHOERA_OK);
  json = (char*)malloc(len);
  CHECK(json != NULL);
  CHECK(archoera_result(core, modules_task, json, len, &len, &err) ==
        ARCHOERA_OK);
  CHECK(strstr(json, "\"system.ping\"") != NULL);
  CHECK(strstr(json, "\"system.modules\"") != NULL);
  archoera_release(core, modules_task);

  /* system.gpu reports display adapters for launch optimization. The payload
   * must stay stable even on machines without a usable adapter. */
  CHECK(archoera_call(core, "system.gpu", "{}", &gpu_task, &err) ==
        ARCHOERA_OK);
  CHECK(gpu_task != 0);
  count = archoera_poll(core, events, 64);
  CHECK(count == 1);
  CHECK(events[0].task_id == gpu_task);
  CHECK(events[0].state == ARCHOERA_TASK_COMPLETED);

  free(json);
  json = NULL;
  len = 0;
  CHECK(archoera_result(core, gpu_task, NULL, 0, &len, &err) == ARCHOERA_OK);
  json = (char*)malloc(len);
  CHECK(json != NULL);
  CHECK(archoera_result(core, gpu_task, json, len, &len, &err) ==
        ARCHOERA_OK);
  CHECK(strstr(json, "\"platform\":\"") != NULL);
  CHECK(strstr(json, "\"gpus\":[") != NULL);
  CHECK(strstr(json, "\"hybrid\":") != NULL);
  archoera_release(core, gpu_task);

  /* system.appearance is a core-owned, synchronous method. A native watcher
   * may also enqueue domain events at any time, so scan by task id. */
  CHECK(archoera_call(core, "system.appearance", "{}", &appearance_task,
                      &err) == ARCHOERA_OK);
  CHECK(appearance_task != 0);
  {
    int found = 0;
    for (int attempt = 0; attempt < 8 && !found; ++attempt) {
      count = archoera_poll(core, events, 64);
      if (count < 0) {
        break;
      }
      for (int index = 0; index < count; ++index) {
        if (events[index].task_id == appearance_task) {
          CHECK(events[index].state == ARCHOERA_TASK_COMPLETED);
          found = 1;
        }
      }
      if (!found && count == 0) {
        break;
      }
    }
    CHECK(found);
  }

  free(json);
  json = NULL;
  len = 0;
  CHECK(archoera_result(core, appearance_task, NULL, 0, &len, &err) ==
        ARCHOERA_OK);
  json = (char*)malloc(len);
  CHECK(json != NULL);
  CHECK(archoera_result(core, appearance_task, json, len, &len, &err) ==
        ARCHOERA_OK);
  CHECK(strstr(json, "\"theme\":") != NULL);
  CHECK(strstr(json, "\"source\":") != NULL);
  archoera_release(core, appearance_task);

  /* system.status runs on a core worker (CPU is sampled over ~140 ms), so it
   * completes asynchronously like the other native calls. */
  {
    uint64_t status_task = 0;
    CHECK(archoera_call(core, "system.status", "{}", &status_task, &err) ==
          ARCHOERA_OK);
    CHECK(status_task != 0);
    int found = 0;
    for (int attempt = 0; attempt < 200 && !found; ++attempt) {
      count = archoera_poll(core, events, 64);
      if (count < 0) {
        break;
      }
      for (int index = 0; index < count; ++index) {
        if (events[index].task_id == status_task &&
            events[index].state == ARCHOERA_TASK_COMPLETED) {
          found = 1;
        }
      }
      if (!found) {
        sleep_millis(10);
      }
    }
    CHECK(found);
    free(json);
    json = NULL;
    len = 0;
    CHECK(archoera_result(core, status_task, NULL, 0, &len, &err) ==
          ARCHOERA_OK);
    json = (char*)malloc(len);
    CHECK(json != NULL);
    CHECK(archoera_result(core, status_task, json, len, &len, &err) ==
          ARCHOERA_OK);
    CHECK(strstr(json, "\"platform\":") != NULL);
    CHECK(strstr(json, "\"memory\":") != NULL);
    CHECK(strstr(json, "\"disk\":") != NULL);
    CHECK(strstr(json, "\"process\":") != NULL);
    archoera_release(core, status_task);

    /* The push watcher is core-owned and can be stopped again. */
    uint64_t watch_task = 0;
    CHECK(archoera_call(core, "system.status.watch",
                        "{\"intervalMs\":1000}", &watch_task, &err) ==
          ARCHOERA_OK);
    CHECK(watch_task != 0);
    archoera_release(core, watch_task);

    uint64_t unwatch_task = 0;
    CHECK(archoera_call(core, "system.status.unwatch", "{}", &unwatch_task,
                        &err) == ARCHOERA_OK);
    CHECK(unwatch_task != 0);
    archoera_release(core, unwatch_task);
  }

  /* Native system actions reject non-http(s) URLs before spawning work. */
  rejected_task = 123;
  CHECK(archoera_call(core, "system.openUrl", "{\"url\":\"file:///etc/passwd\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);
  rejected_task = 123;
  CHECK(archoera_call(core, "system.openUrl", "{\"url\":\"javascript:alert(1)\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);

  /*
   * system.openPath is directory only: relative, empty, missing and
   * non-existent paths are rejected before any work is spawned. A real
   * directory is never opened here, that would touch the host desktop.
   */
  rejected_task = 123;
  CHECK(archoera_call(core, "system.openPath", "{\"path\":\"relative/dir\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);
  rejected_task = 123;
  CHECK(archoera_call(core, "system.openPath", "{\"path\":\"\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);
  rejected_task = 123;
  CHECK(archoera_call(core, "system.openPath", "{}", &rejected_task, &err) ==
        ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);
  rejected_task = 123;
  CHECK(archoera_call(core, "system.openPath",
                      "{\"path\":\"/archoera-cx/no-such-directory\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);
  rejected_task = 123;
  CHECK(archoera_call(core, "system.openPath", "{\"path\":\"/tmp\\u0001bad\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);

  /* system.revealPath uses the same directory-only contract. */
  rejected_task = 123;
  CHECK(archoera_call(core, "system.revealPath", "{\"path\":\"relative/dir\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);
  rejected_task = 123;
  CHECK(archoera_call(core, "system.revealPath",
                      "{\"path\":\"/archoera-cx/no-such-directory\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);
  rejected_task = 123;
  CHECK(archoera_call(core, "system.revealPath", "{}", &rejected_task, &err) ==
        ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);

  /* Cancel is idempotent for terminal tasks. */
  CHECK(archoera_cancel(core, ping_task) == ARCHOERA_OK);
  CHECK(archoera_cancel(core, ping_task) == ARCHOERA_OK);
  CHECK(archoera_cancel(core, 0) == ARCHOERA_E_NOT_FOUND);
  CHECK(archoera_cancel(core, 999999) == ARCHOERA_E_NOT_FOUND);

  /* Unknown methods fail immediately and do not allocate a task. */
  unknown_task = 123;
  CHECK(archoera_call(core, "system.unknown", "{}", &unknown_task, &err) ==
        ARCHOERA_E_UNSUPPORTED);
  CHECK(unknown_task == 0);

  /* Released tasks look like unknown tasks. */
  archoera_release(core, ping_task);
  len = 0;
  CHECK(archoera_result(core, ping_task, NULL, 0, &len, &err) ==
        ARCHOERA_E_NOT_FOUND);

  /* Release is idempotent. */
  archoera_release(core, ping_task);

cleanup:
  free(json);
  if (core != NULL) {
    archoera_shutdown(core);
  }
  if (rc == 0) {
    printf("abi_smoke: OK\n");
  }
  return rc;
}
