/*
 * Event queue cap harness for ArchoeraCX ABI v1.
 * SPDX-License-Identifier: AGPL-3.0-only
 *
 * The producer side must never block, so a full queue sheds the oldest
 * droppable event (progress/domain/running) while terminal state events are
 * kept. This exercises the policy through the same inline helpers the core
 * uses, without constructing a core handle or any worker.
 */

#include "core_internal.h"

#include <cstdio>
#include <deque>
#include <string>

namespace {

using archoera::EventRecord;

#define CHECK(cond)                                                       \
  do {                                                                    \
    if (!(cond)) {                                                        \
      std::fprintf(stderr, "FAILED %s:%d: %s\n", __FILE__, __LINE__,      \
                   #cond);                                                \
      return 1;                                                           \
    }                                                                     \
  } while (0)

EventRecord progress_event() {
  EventRecord event;
  event.type = ARCHOERA_EV_TASK_PROGRESS;
  event.task_id = 7;
  return event;
}

EventRecord terminal_event(archoera_task_state state) {
  EventRecord event;
  event.type = ARCHOERA_EV_TASK_STATE;
  event.state = state;
  event.task_id = 7;
  return event;
}

int run_tests() {
  const std::size_t cap = archoera::kMaxPendingEvents;
  CHECK(cap == 4096);

  // --- full queue of progress events drops exactly one, the oldest ---------
  {
    std::deque<EventRecord> events;
    uint64_t next_seq = 1;
    for (std::size_t i = 0; i < cap; ++i) {
      archoera::enqueue_event(events, next_seq, progress_event());
    }
    CHECK(events.size() == cap);
    CHECK(events.front().seq == 1);

    archoera::enqueue_event(events, next_seq, progress_event());
    CHECK(events.size() == cap);
    CHECK(events.front().seq == 2);
    CHECK(events.back().seq == cap + 1);

    // Sequence numbers stay strictly increasing after the drop.
    uint64_t previous = 0;
    for (const EventRecord& event : events) {
      CHECK(event.seq > previous);
      previous = event.seq;
    }
  }

  // --- terminal events are never dropped, even past the cap -----------------
  {
    std::deque<EventRecord> events;
    uint64_t next_seq = 1;
    for (std::size_t i = 0; i < cap; ++i) {
      archoera::enqueue_event(events, next_seq,
                              terminal_event(ARCHOERA_TASK_COMPLETED));
    }
    archoera::enqueue_event(events, next_seq,
                            terminal_event(ARCHOERA_TASK_FAILED));
    archoera::enqueue_event(events, next_seq,
                            terminal_event(ARCHOERA_TASK_CANCELLED));

    CHECK(events.size() == cap + 2);
    CHECK(events.front().seq == 1);
    CHECK(events[cap - 1].state == ARCHOERA_TASK_COMPLETED);
    CHECK(events[cap].state == ARCHOERA_TASK_FAILED);
    CHECK(events[cap + 1].state == ARCHOERA_TASK_CANCELLED);
  }

  // --- with only terminals queued, a new progress event is still dropped ---
  {
    std::deque<EventRecord> events;
    uint64_t next_seq = 1;
    for (std::size_t i = 0; i < cap; ++i) {
      archoera::enqueue_event(events, next_seq,
                              terminal_event(ARCHOERA_TASK_COMPLETED));
    }
    archoera::enqueue_event(events, next_seq, progress_event());
    // Nothing is droppable, so the queue grows; the next enqueue sheds the
    // progress event that just arrived.
    CHECK(events.size() == cap + 1);
    archoera::enqueue_event(events, next_seq, progress_event());
    CHECK(events.size() == cap + 1);
    CHECK(events.back().seq == cap + 2);
    for (std::size_t i = 0; i < cap; ++i) {
      CHECK(events[i].state == ARCHOERA_TASK_COMPLETED);
    }
  }

  // --- running state events and domain events are droppable ----------------
  {
    std::deque<EventRecord> events;
    uint64_t next_seq = 1;
    for (std::size_t i = 0; i < cap; ++i) {
      EventRecord event;
      event.type = ARCHOERA_EV_TASK_STATE;
      event.state = ARCHOERA_TASK_RUNNING;
      archoera::enqueue_event(events, next_seq, std::move(event));
    }
    archoera::enqueue_event(events, next_seq, progress_event());
    CHECK(events.size() == cap);
    CHECK(events.front().seq == 2);
    CHECK(events.back().type == ARCHOERA_EV_TASK_PROGRESS);
  }
  {
    std::deque<EventRecord> events;
    uint64_t next_seq = 1;
    for (std::size_t i = 0; i < cap; ++i) {
      EventRecord event;
      event.type = ARCHOERA_EV_DOMAIN;
      archoera::enqueue_event(events, next_seq, std::move(event));
    }
    archoera::enqueue_event(events, next_seq, progress_event());
    CHECK(events.size() == cap);
    CHECK(events.front().seq == 2);
    CHECK(events.back().type == ARCHOERA_EV_TASK_PROGRESS);
  }

  return 0;
}

#undef CHECK

}  // namespace

int main() {
  const int rc = run_tests();
  if (rc == 0) {
    std::printf("event_queue_smoke: OK\n");
  }
  return rc;
}
