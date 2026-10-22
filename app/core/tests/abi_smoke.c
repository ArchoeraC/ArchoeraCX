/*
 * C harness for the ArchoeraCX ABI v1 vertical slice.
 * SPDX-License-Identifier: AGPL-3.0-only
 */

#include "archoera_abi.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

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
  uint64_t appearance_task = 0;
  uint64_t rejected_task = 123;
  uint64_t unknown_task = 123;
  uint64_t first_seq = 0;
  char* json = NULL;
  char small[4];
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
  memset(small, 'X', sizeof(small));
  len = 0;
  CHECK(archoera_result(core, ping_task, small, sizeof(small), &len, &err) ==
        ARCHOERA_E_BUFFER);
  CHECK(len > sizeof(small));
  CHECK(small[0] == 'X');

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

  /* Native system actions reject non-http(s) URLs before spawning work. */
  rejected_task = 123;
  CHECK(archoera_call(core, "system.openUrl", "{\"url\":\"file:///etc/passwd\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
  CHECK(rejected_task == 0);
  rejected_task = 123;
  CHECK(archoera_call(core, "system.openUrl", "{\"url\":\"javascript:alert(1)\"}",
                      &rejected_task, &err) == ARCHOERA_E_INVALID);
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
