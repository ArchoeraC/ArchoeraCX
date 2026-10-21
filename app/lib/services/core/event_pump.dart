import 'dart:async';
import 'dart:collection';
import 'dart:convert';
import 'dart:ffi' as ffi;

import 'package:ffi/ffi.dart';

import 'core_event.dart';

/// Mirrors `archoera_event` in `contracts/abi/archoera_abi.h`.
final class _ArchoeraEventNative extends ffi.Struct {
  @ffi.Int32()
  external int type;

  @ffi.Int32()
  external int state;

  @ffi.Int32()
  external int code;

  @ffi.Uint32()
  external int flags;

  @ffi.Uint64()
  external int seq;

  @ffi.Uint64()
  external int taskId;

  @ffi.Int64()
  external int done;

  @ffi.Int64()
  external int total;

  @ffi.Int64()
  external int bytesPerSec;

  external ffi.Pointer<Utf8> json;
}

/// `archoera_poll` with an opaque event array pointer.
///
/// [EventPump] owns the typed array and casts it at the call site, so the
/// native struct stays private to this library.
typedef CorePollNative = ffi.Int32 Function(
  ffi.Pointer<ffi.Void>,
  ffi.Pointer<ffi.Void>,
  ffi.Int32,
);
typedef CorePoll = int Function(
  ffi.Pointer<ffi.Void>,
  ffi.Pointer<ffi.Void>,
  int,
);

/// `archoera_set_wakeup`; the callback takes one ignored user pointer.
typedef CoreSetWakeupNative = ffi.Int32 Function(
  ffi.Pointer<ffi.Void>,
  ffi.Pointer<ffi.NativeFunction<ffi.Void Function(ffi.Pointer<ffi.Void>)>>,
  ffi.Pointer<ffi.Void>,
);
typedef CoreSetWakeup = int Function(
  ffi.Pointer<ffi.Void>,
  ffi.Pointer<ffi.NativeFunction<ffi.Void Function(ffi.Pointer<ffi.Void>)>>,
  ffi.Pointer<ffi.Void>,
);

/// Called for every event belonging to a tracked task.
typedef CoreTaskEventListener = void Function(CoreEvent event);

/// Drains the native core event queue in response to native wakeups.
///
/// The only thing that starts a drain is [NativeCallable.listener] invoking
/// [_onWake] when the core calls its wake callback. There is no timer, no
/// polling loop and no sleep anywhere in this class.
final class EventPump {
  EventPump(this._core, this._poll, this._setWakeup, {int batchSize = 64})
    : _batchSize = batchSize.clamp(1, 256) {
    _events = calloc<_ArchoeraEventNative>(_batchSize);

    // Keep the NativeCallable alive for the lifetime of the pump: the native
    // core holds this function pointer and will call it from arbitrary threads.
    _wake =
        ffi.NativeCallable<ffi.Void Function(ffi.Pointer<ffi.Void>)>.listener(
          _onWake,
        );

    final status = _setWakeup(_core, _wake.nativeFunction, ffi.nullptr);
    if (status != 0) {
      _wake.close();
      calloc.free(_events);
      _events = ffi.nullptr;
      throw StateError(
        'archoera_set_wakeup failed with status $status; '
        'the core event pump cannot start.',
      );
    }
  }

  static const int _maxBatchesPerDrain = 8;
  static const int _maxBufferedTerminals = 64;

  final ffi.Pointer<ffi.Void> _core;
  final CorePoll _poll;
  final CoreSetWakeup _setWakeup;
  final int _batchSize;

  final StreamController<CoreEvent> _controller =
      StreamController<CoreEvent>.broadcast(sync: true);

  /// Terminal events that arrived before a caller started tracking their task.
  final LinkedHashMap<int, CoreEvent> _bufferedTerminals =
      LinkedHashMap<int, CoreEvent>();

  final Map<int, _TaskWaiter> _waiters = <int, _TaskWaiter>{};

  late ffi.Pointer<_ArchoeraEventNative> _events;
  late final ffi.NativeCallable<ffi.Void Function(ffi.Pointer<ffi.Void>)> _wake;

  bool _disposed = false;
  bool _detached = false;
  bool _drainScheduled = false;

  /// Every event drained from the core, in sequence order.
  Stream<CoreEvent> get events => _controller.stream;

  bool get isDisposed => _disposed;

  /// Wait for the terminal state event of [taskId].
  ///
  /// [onEvent] receives every event for the task while it is in flight,
  /// including progress and the terminal event. The returned future completes
  /// exactly once, driven by the event drain.
  Future<CoreEvent> waitForTerminal(
    int taskId, {
    CoreTaskEventListener? onEvent,
  }) {
    if (_disposed) {
      return Future<CoreEvent>.error(const CoreEventPumpClosed());
    }

    final buffered = _bufferedTerminals.remove(taskId);
    if (buffered != null) {
      onEvent?.call(buffered);
      return Future<CoreEvent>.value(buffered);
    }

    if (_waiters.containsKey(taskId)) {
      return Future<CoreEvent>.error(
        StateError('Task $taskId is already being tracked.'),
      );
    }

    final completer = Completer<CoreEvent>();
    _waiters[taskId] = _TaskWaiter(completer, onEvent);
    return completer.future;
  }

  /// Drop any bookkeeping for [taskId] without completing a pending future.
  ///
  /// Callers use this after they have already received the terminal event.
  void forgetTask(int taskId) {
    _waiters.remove(taskId);
    _bufferedTerminals.remove(taskId);
  }

  /// Stop native wakeups without closing the [NativeCallable].
  ///
  /// Call this before `archoera_shutdown`: the core joins worker threads
  /// during shutdown, and a wake callback that was already captured may still
  /// run until shutdown returns. The callable must stay alive until then.
  void detach() {
    if (_disposed || _detached) return;
    _detached = true;

    try {
      _setWakeup(_core, ffi.nullptr, ffi.nullptr);
    } catch (_) {
      // Best effort: the core may already be shutting down.
    }
  }

  /// Release every Dart-side resource. The core handle is not touched unless
  /// [detach] was not called first.
  void dispose() {
    if (_disposed) return;
    if (!_detached) {
      detach();
    }
    _disposed = true;

    try {
      _wake.close();
    } catch (_) {
      // Closing twice is harmless; never throw during widget disposal.
    }

    for (final waiter in _waiters.values) {
      if (!waiter.completer.isCompleted) {
        waiter.completer.completeError(const CoreEventPumpClosed());
      }
    }
    _waiters.clear();
    _bufferedTerminals.clear();

    if (!_controller.isClosed) {
      unawaited(_controller.close());
    }

    if (_events != ffi.nullptr) {
      calloc.free(_events);
      _events = ffi.nullptr;
    }
  }

  /// Native wake callback. Runs on the UI isolate via [NativeCallable.listener].
  ///
  /// It only schedules a microtask; the actual `archoera_poll` happens in
  /// [_drain], so a burst of wakeups coalesces into one drain.
  void _onWake(ffi.Pointer<ffi.Void> _) {
    if (_disposed || _detached || _drainScheduled) return;
    _drainScheduled = true;
    scheduleMicrotask(_drain);
  }

  void _scheduleDrain() {
    if (_disposed || _detached || _drainScheduled) return;
    _drainScheduled = true;
    scheduleMicrotask(_drain);
  }

  void _drain() {
    _drainScheduled = false;
    if (_disposed || _detached) return;

    for (var batch = 0; batch < _maxBatchesPerDrain; batch++) {
      if (_disposed || _detached) return;

      final count = _poll(_core, _events.cast<ffi.Void>(), _batchSize);
      if (count <= 0) return;

      for (var i = 0; i < count; i++) {
        _dispatch(_copyEvent(_events[i]));
        // A stream listener may have disposed the pump synchronously; the core
        // and the event buffer must not be touched after that.
        if (_disposed) return;
      }

      if (count < _batchSize) return;
    }

    // The queue was still full when the batch budget ran out. Yield to the
    // event loop and continue from another wake-driven microtask.
    _scheduleDrain();
  }

  /// Copy everything out of [native] before the next poll invalidates it.
  CoreEvent _copyEvent(_ArchoeraEventNative native) {
    final jsonPointer = native.json;
    final Map<String, Object?> payload = jsonPointer == ffi.nullptr
        ? const <String, Object?>{}
        : _decodePayload(jsonPointer.toDartString());

    return CoreEvent(
      seq: native.seq,
      type: CoreEventType.fromCode(native.type),
      taskId: native.taskId,
      state: CoreTaskState.fromCode(native.state),
      code: native.code,
      flags: native.flags,
      done: native.done,
      total: native.total,
      bytesPerSec: native.bytesPerSec,
      payload: payload,
    );
  }

  void _dispatch(CoreEvent event) {
    if (!_controller.isClosed) {
      _controller.add(event);
    }

    final waiter = _waiters[event.taskId];
    if (waiter != null) {
      waiter.onEvent?.call(event);
      if (event.isTerminal) {
        _waiters.remove(event.taskId);
        if (!waiter.completer.isCompleted) {
          waiter.completer.complete(event);
        }
      }
      return;
    }

    // The task may complete before its caller installs a waiter (for example a
    // synchronous native method). Buffer a bounded number of terminal events.
    if (event.isTerminal) {
      _bufferedTerminals[event.taskId] = event;
      while (_bufferedTerminals.length > _maxBufferedTerminals) {
        _bufferedTerminals.remove(_bufferedTerminals.keys.first);
      }
    }
  }

  static Map<String, Object?> _decodePayload(String text) {
    if (text.isEmpty) return const <String, Object?>{};
    try {
      final decoded = jsonDecode(text);
      if (decoded is Map<String, Object?>) return decoded;
      return <String, Object?>{'value': decoded};
    } on FormatException {
      return <String, Object?>{'raw': text};
    }
  }
}

final class _TaskWaiter {
  _TaskWaiter(this.completer, this.onEvent);

  final Completer<CoreEvent> completer;
  final CoreTaskEventListener? onEvent;
}
