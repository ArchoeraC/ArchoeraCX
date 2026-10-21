/// Types and payloads for events drained from the native core queue.
///
/// The numeric codes mirror `archoera_event_type` and `archoera_task_state` in
/// `contracts/abi/archoera_abi.h`. Nothing in this file touches FFI: an
/// [CoreEvent] owns only Dart values, never native pointers.
library;

/// Kind of event produced by the native core.
enum CoreEventType {
  /// Unknown or future event code; the raw payload is still available.
  unknown(0),

  /// A task changed state (queued, running, waiting or terminal).
  taskState(1),

  /// A task reported progress (`done` / `total` / `bytesPerSec`).
  taskProgress(2),

  /// A log line; details live in the JSON payload.
  log(3),

  /// A domain-level event forwarded by a module.
  domain(4),

  /// A module lifecycle event.
  module(5);

  const CoreEventType(this.code);

  /// `archoera_event_type` value.
  final int code;

  static CoreEventType fromCode(int code) => switch (code) {
    1 => CoreEventType.taskState,
    2 => CoreEventType.taskProgress,
    3 => CoreEventType.log,
    4 => CoreEventType.domain,
    5 => CoreEventType.module,
    _ => CoreEventType.unknown,
  };
}

/// Lifecycle state of a native task.
enum CoreTaskState {
  queued(0),
  running(1),
  waiting(2),
  completed(3),
  failed(4),
  cancelled(5);

  const CoreTaskState(this.code);

  /// `archoera_task_state` value.
  final int code;

  /// Whether no further events are expected for the task.
  bool get isTerminal =>
      this == CoreTaskState.completed ||
      this == CoreTaskState.failed ||
      this == CoreTaskState.cancelled;

  static CoreTaskState? fromCode(int code) => switch (code) {
    0 => CoreTaskState.queued,
    1 => CoreTaskState.running,
    2 => CoreTaskState.waiting,
    3 => CoreTaskState.completed,
    4 => CoreTaskState.failed,
    5 => CoreTaskState.cancelled,
    _ => null,
  };
}

/// A single immutable event copied out of the native queue.
final class CoreEvent {
  const CoreEvent({
    required this.seq,
    required this.type,
    required this.taskId,
    this.state,
    this.code = 0,
    this.flags = 0,
    this.done = 0,
    this.total = 0,
    this.bytesPerSec = 0,
    this.payload = const <String, Object?>{},
  });

  /// Monotonic sequence assigned by the native core.
  final int seq;

  final CoreEventType type;

  /// Owning task id, or `0` for events that are not task-scoped.
  final int taskId;

  /// Task state for [CoreEventType.taskState] events.
  final CoreTaskState? state;

  /// Status/domain code; usually an `archoera_status` value.
  final int code;

  final int flags;

  /// Work completed so far, in units reported by the task.
  final int done;

  /// Total work, or `0` when unknown.
  final int total;

  /// Transfer rate in bytes per second, or `0` when not applicable.
  final int bytesPerSec;

  /// Decoded JSON payload. The string was copied during the drain, so this map
  /// stays valid after the core reuses its native buffers.
  final Map<String, Object?> payload;

  /// Whether this event is a terminal task state.
  bool get isTerminal => state?.isTerminal ?? false;

  /// Progress in `[0, 1]`, or `null` when the total is unknown.
  double? get progress {
    if (total <= 0) return null;
    return (done / total).clamp(0.0, 1.0);
  }

  /// Human-readable message from the JSON payload, when present.
  String? get message => payload['message']?.toString();

  @override
  String toString() =>
      'CoreEvent(seq=$seq, type=${type.name}, task=$taskId, '
      'state=${state?.name}, done=$done/$total)';
}

/// Raised by the event pump when it is disposed while a caller is waiting.
final class CoreEventPumpClosed implements Exception {
  const CoreEventPumpClosed([this.message = 'The core event pump is closed.']);

  final String message;

  @override
  String toString() => 'CoreEventPumpClosed: $message';
}
