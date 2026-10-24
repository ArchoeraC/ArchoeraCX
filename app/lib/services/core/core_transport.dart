import 'dart:async';
import 'dart:convert';
import 'dart:ffi' as ffi;
import 'dart:io';

import 'package:ffi/ffi.dart';
import 'package:flutter/foundation.dart' show kDebugMode;

import 'core_event.dart';
import 'event_pump.dart';
import 'downloads/task_update.dart';

/// Sentinel for optional fields where `null` is a meaningful value.
const Object noChange = Object();

/// Progress callback carrying the module-provided phase/current line.
typedef CoreDetailedProgress = void Function(
  int done,
  int total,
  String? message,
);

/// Symbolic names of the `archoera_status` values defined in
/// `contracts/abi/archoera_abi.h`.
///
/// Formerly the private `_ok` / `_eBuffer` / `_statusNames` constants of
/// `core_client.dart`; every shared-core file reads them from here so status
/// semantics live in one place (internal-transport API).
abstract final class CoreStatusCodes {
  /// Call succeeded.
  static const int ok = 0;

  /// The result buffer was too small (two-phase `archoera_result` read).
  static const int eBuffer = 9;

  static const Map<int, String> names = <int, String>{
    0: 'OK',
    1: 'E_INVALID',
    2: 'E_NOT_FOUND',
    3: 'E_AUTH',
    4: 'E_NETWORK',
    5: 'E_DISK',
    6: 'E_CANCELLED',
    7: 'E_UNSUPPORTED',
    8: 'E_INTERNAL',
    9: 'E_BUFFER',
    10: 'E_BUSY',
    11: 'E_POLICY',
  };

  /// Symbolic name of [status], or [fallback] when the value is unknown.
  static String nameOf(int status, {String fallback = 'E_UNKNOWN'}) =>
      names[status] ?? fallback;
}

/// The error structure returned by the native core ABI.
///
/// Mirrors `archoera_error` in `contracts/abi/archoera_abi.h`.
final class _ArchoeraErrorNative extends ffi.Struct {
  @ffi.Int32()
  external int code;

  external ffi.Pointer<Utf8> message;

  external ffi.Pointer<Utf8> detail;
}

typedef _AbiVersionNative = ffi.Uint32 Function();
typedef _AbiVersionDart = int Function();

typedef _BuildInfoNative = ffi.Pointer<Utf8> Function();
typedef _BuildInfoDart = ffi.Pointer<Utf8> Function();

typedef _InitNative = ffi.Int32 Function(
  ffi.Pointer<Utf8>,
  ffi.Pointer<ffi.Pointer<ffi.Void>>,
  ffi.Pointer<_ArchoeraErrorNative>,
);
typedef _InitDart = int Function(
  ffi.Pointer<Utf8>,
  ffi.Pointer<ffi.Pointer<ffi.Void>>,
  ffi.Pointer<_ArchoeraErrorNative>,
);

typedef _ShutdownNative = ffi.Void Function(ffi.Pointer<ffi.Void>);
typedef _ShutdownDart = void Function(ffi.Pointer<ffi.Void>);

typedef _CallNative = ffi.Int32 Function(
  ffi.Pointer<ffi.Void>,
  ffi.Pointer<Utf8>,
  ffi.Pointer<Utf8>,
  ffi.Pointer<ffi.Uint64>,
  ffi.Pointer<_ArchoeraErrorNative>,
);
typedef _CallDart = int Function(
  ffi.Pointer<ffi.Void>,
  ffi.Pointer<Utf8>,
  ffi.Pointer<Utf8>,
  ffi.Pointer<ffi.Uint64>,
  ffi.Pointer<_ArchoeraErrorNative>,
);

typedef _ResultNative = ffi.Int32 Function(
  ffi.Pointer<ffi.Void>,
  ffi.Uint64,
  ffi.Pointer<Utf8>,
  ffi.IntPtr,
  ffi.Pointer<ffi.IntPtr>,
  ffi.Pointer<_ArchoeraErrorNative>,
);
typedef _ResultDart = int Function(
  ffi.Pointer<ffi.Void>,
  int,
  ffi.Pointer<Utf8>,
  int,
  ffi.Pointer<ffi.IntPtr>,
  ffi.Pointer<_ArchoeraErrorNative>,
);

typedef _ReleaseNative = ffi.Void Function(ffi.Pointer<ffi.Void>, ffi.Uint64);
typedef _ReleaseDart = void Function(ffi.Pointer<ffi.Void>, int);

typedef _CancelNative = ffi.Int32 Function(ffi.Pointer<ffi.Void>, ffi.Uint64);
typedef _CancelDart = int Function(ffi.Pointer<ffi.Void>, int);

/// A structured error reported by the native core.
final class CoreException implements Exception {
  const CoreException({
    required this.status,
    required this.code,
    required this.message,
    this.detail,
    this.taskId,
  });

  /// Raw `archoera_status` value (0 for errors carried inside a JSON envelope).
  final int status;

  /// Symbolic status/domain code, for example `E_INVALID` or `RATE_LIMITED`.
  final String code;

  final String message;
  final String? detail;
  final int? taskId;

  @override
  String toString() {
    final suffix = detail == null || detail!.isEmpty ? '' : ' — $detail';
    return 'CoreException($code, status=$status): $message$suffix';
  }
}

/// Raised when `archoera_core` cannot be located, loaded, or initialized.
final class CoreUnavailableException implements Exception {
  CoreUnavailableException(
    this.message, {
    List<String> searchedPaths = const [],
  }) : searchedPaths = List.unmodifiable(searchedPaths);

  final String message;
  final List<String> searchedPaths;

  /// Human-readable message including every path that was probed.
  String get summary => searchedPaths.isEmpty
      ? message
      : '$message\n\nSearched:\n${searchedPaths.join('\n')}';

  @override
  String toString() => 'CoreUnavailableException: $summary';
}

/// Engine, FFI binding and decoding infrastructure of the `archoera_core`
/// Dart binding.
///
/// [CoreClient] (in `core_client.dart`) extends this class and composes its
/// domain members from the sibling mixin files, so every method stays a real
/// instance member of [CoreClient]. Everything marked *internal transport
/// API* is only meant to be called from `lib/services/core/**`; UI code uses
/// the typed domain methods inherited through [CoreClient] instead.
class CoreTransport {
  /// Internal transport API. Binds the ABI entry points of [library].
  ///
  /// Call [initialize] exactly once right after construction.
  CoreTransport(ffi.DynamicLibrary library) {
    _abiVersionFn = library.lookupFunction<_AbiVersionNative, _AbiVersionDart>(
      'archoera_abi_version',
    );
    _buildInfoFn = library.lookupFunction<_BuildInfoNative, _BuildInfoDart>(
      'archoera_build_info',
    );
    _initFn = library.lookupFunction<_InitNative, _InitDart>('archoera_init');
    _shutdownFn = library.lookupFunction<_ShutdownNative, _ShutdownDart>(
      'archoera_shutdown',
    );
    _callFn = library.lookupFunction<_CallNative, _CallDart>('archoera_call');
    _resultFn = library.lookupFunction<_ResultNative, _ResultDart>(
      'archoera_result',
    );
    _releaseFn = library.lookupFunction<_ReleaseNative, _ReleaseDart>(
      'archoera_release',
    );
    _pollFn = library.lookupFunction<CorePollNative, CorePoll>('archoera_poll');
    _setWakeupFn = library.lookupFunction<CoreSetWakeupNative, CoreSetWakeup>(
      'archoera_set_wakeup',
    );
    _cancelFn = library.lookupFunction<_CancelNative, _CancelDart>(
      'archoera_cancel',
    );
  }

  /// ABI version this binding was generated against.
  static const int expectedAbiVersion = 1;

  final _taskUpdates = StreamController<TaskUpdate>.broadcast();

  /// Lifecycle/progress snapshots for the task center. Synthetic start/end
  /// updates come from [runAsync]; module progress comes from native events.
  Stream<TaskUpdate> get taskUpdates => _taskUpdates.stream;

  late final _AbiVersionDart _abiVersionFn;
  late final _BuildInfoDart _buildInfoFn;
  late final _InitDart _initFn;
  late final _ShutdownDart _shutdownFn;
  late final _CallDart _callFn;
  late final _ResultDart _resultFn;
  late final _ReleaseDart _releaseFn;
  late final CorePoll _pollFn;
  late final CoreSetWakeup _setWakeupFn;
  late final _CancelDart _cancelFn;

  ffi.Pointer<ffi.Void>? _core;
  EventPump? _pump;
  int _abiVersion = 0;
  String _buildInfo = '';
  bool _closed = false;

  /// Internal transport API: drives the `archoera_core` library lookup that
  /// `CoreClient.openDefault()` iterates over.
  static List<String> defaultLibraryCandidates() {
    final candidates = <String>[];
    // The environment is not a trusted code source in release builds; a
    // packaged app must resolve the core next to its executable. Debug builds
    // keep the override so tests and local runs can target build/native.
    if (kDebugMode) {
      final configured = Platform.environment['ARCHOERACX_CORE_LIB'];
      if (configured != null && configured.trim().isNotEmpty) {
        candidates.add(configured.trim());
      }
    }

    final name = _libraryFileName();
    final separator = Platform.pathSeparator;
    final executableDir = File(Platform.resolvedExecutable).parent.path;

    // Packaged app first: this must work even when the process CWD was
    // deleted or the app was launched from a desktop shortcut.
    candidates.addAll(<String>[
      '$executableDir$separator$name',
      '$executableDir${separator}lib$separator$name',
      '$executableDir$separator..${separator}lib$separator$name',
    ]);

    if (Platform.isMacOS) {
      candidates.add(
        '$executableDir$separator..${separator}Frameworks$separator$name',
      );
    }

    // Development layouts. CWD may be unavailable when the bundle directory
    // was replaced while the process was running; never let that abort the
    // entire lookup with PathNotFoundException.
    final cwd = _safeCurrentDirectory();
    if (cwd != null) {
      candidates.addAll(<String>[
        '$cwd${separator}build${separator}native$separator$name',
        '$cwd${separator}app${separator}build${separator}native$separator$name',
        '$cwd$separator..${separator}app${separator}build$separator'
            'native$separator$name',
        '$cwd${separator}build${separator}linux${separator}x64$separator'
            'release${separator}bundle${separator}lib$separator$name',
        '$cwd${separator}app${separator}build${separator}linux'
            '${separator}x64${separator}release${separator}bundle'
            '${separator}lib$separator$name',
      ]);

      if (Platform.isWindows) {
        candidates.add(
          '$cwd${separator}build${separator}windows${separator}x64'
          '${separator}runner${separator}Release$separator$name',
        );
      } else if (Platform.isMacOS) {
        candidates.add(
          '$cwd${separator}build${separator}macos${separator}Build'
          '${separator}Products${separator}Release$separator$name',
        );
      }
    }

    return candidates;
  }

  /// Internal transport API: turn [candidate] into an absolute path without
  /// ever throwing.
  static String? tryAbsolutePath(String candidate) {
    try {
      final file = File(candidate);
      return file.isAbsolute ? file.path : file.absolute.path;
    } catch (_) {
      return null;
    }
  }

  static String? _safeCurrentDirectory() {
    try {
      final value = Directory.current.path;
      return value.isEmpty ? null : value;
    } catch (_) {
      return null;
    }
  }

  static String _libraryFileName() {
    if (Platform.isWindows) return 'archoera_core.dll';
    if (Platform.isMacOS) return 'libarchoera_core.dylib';
    return 'libarchoera_core.so';
  }

  /// Internal transport API: the single structured-envelope error builder.
  ///
  /// All `$methodName returned …` failures flow through here, so the message
  /// template exists in exactly one place.
  static CoreException invalidEnvelope(
    String methodName,
    String what, {
    String? detail,
  }) => CoreException(
    status: CoreStatusCodes.ok,
    code: 'E_INVALID',
    message: '$methodName returned $what.',
    detail: detail,
  );

  /// Internal transport API: verify the ABI, allocate the core handle and
  /// start the wake-driven event pump. Must be called exactly once.
  void initialize() {
    final abiVersion = _abiVersionFn();
    if (abiVersion != expectedAbiVersion) {
      throw CoreException(
        status: CoreStatusCodes.ok,
        code: 'E_UNSUPPORTED',
        message:
            'Unsupported archoera_core ABI v$abiVersion '
            '(expected v$expectedAbiVersion).',
      );
    }

    final buildInfoPtr = _buildInfoFn();
    _buildInfo = buildInfoPtr == ffi.nullptr ? '' : buildInfoPtr.toDartString();

    final config = '{}'.toNativeUtf8();
    final outCore = calloc<ffi.Pointer<ffi.Void>>();
    final errorPtr = calloc<_ArchoeraErrorNative>();

    try {
      final status = _initFn(config, outCore, errorPtr);
      if (status != CoreStatusCodes.ok) {
        throw _exceptionForStatus(status, errorPtr.ref);
      }

      final core = outCore.value;
      if (core == ffi.nullptr) {
        throw const CoreException(
          status: CoreStatusCodes.ok,
          code: 'E_INTERNAL',
          message: 'archoera_init returned OK but produced no core handle.',
        );
      }

      _core = core;
      _abiVersion = abiVersion;

      try {
        _pump = EventPump(core, _pollFn, _setWakeupFn);
      } catch (_) {
        // Do not leak a core that cannot deliver events.
        _core = null;
        _pump = null;
        try {
          _shutdownFn(core);
        } catch (_) {
          // Ignore cleanup failures and rethrow the pump error.
        }
        rethrow;
      }
    } finally {
      calloc.free(errorPtr);
      calloc.free(outCore);
      malloc.free(config);
    }
  }

  int get abiVersion => _abiVersion;

  /// Static build information supplied by the core, if available.
  String get buildInfo => _buildInfo;

  bool get isOpen => !_closed && _core != null;

  /// Events drained from the native core, in sequence order.
  Stream<CoreEvent> get events =>
      _pump?.events ?? const Stream<CoreEvent>.empty();

  /// Internal transport API: the shared async-task path used by every domain
  /// method (`pingAsync`, `scanJava`, loader calls, …).
  ///
  /// Starts a native task with the supplied [paramsJson], forwards progress
  /// events and waits for the terminal task-state event. The raw result JSON
  /// is returned; each caller owns envelope validation and parsing.
  Future<String> runAsync(
    String methodName, {
    void Function(int done, int total)? onProgress,
    CoreDetailedProgress? onDetailedProgress,
    void Function(int taskId)? onTaskStarted,
    String paramsJson = '{}',
    String? label,
  }) async {
    final core = _requireCore();
    final pump = _pump;
    if (pump == null) {
      throw const CoreException(
        status: CoreStatusCodes.ok,
        code: 'E_INVALID',
        message: 'The core event pump is not running.',
      );
    }

    var taskId = 0;
    // Releasing is only safe once a terminal state is known. Releasing a
    // running task would detach it from cancel and from its module callback;
    // the core keeps it alive and reclaims it after the result TTL instead.
    var terminalKnown = false;
    try {
      taskId = _startTask(core, methodName, paramsJson: paramsJson);
      onTaskStarted?.call(taskId);
      _emitTask(
        TaskUpdate(
          taskId: taskId,
          method: methodName,
          state: TaskUpdateState.running,
          label: label,
        ),
      );

      final CoreEvent terminal;
      try {
        terminal = await pump.waitForTerminal(
          taskId,
          onEvent: (event) {
            if (event.type == CoreEventType.taskProgress) {
              final line =
                  event.payload['current']?.toString() ?? event.message;
              onProgress?.call(event.done, event.total);
              onDetailedProgress?.call(event.done, event.total, line);
              _emitTask(
                TaskUpdate(
                  taskId: taskId,
                  method: methodName,
                  state: TaskUpdateState.running,
                  label: label,
                  done: event.done,
                  total: event.total,
                  bytesPerSecond: event.bytesPerSec,
                  message:
                      event.payload['current']?.toString() ?? event.message,
                ),
              );
            }
          },
        );
        terminalKnown = true;
      } on CoreEventPumpClosed catch (error) {
        _emitTask(
          TaskUpdate(
            taskId: taskId,
            method: methodName,
            state: TaskUpdateState.cancelled,
            label: label,
            message: error.message,
          ),
        );
        throw CoreException(
          status: CoreStatusCodes.ok,
          code: 'E_CANCELLED',
          message: error.message,
          taskId: taskId,
        );
      }

      if (terminal.state == CoreTaskState.completed) {
        // The card may have been disposed (and the core shut down) while this
        // future was waiting on the event pump. Never touch a freed handle.
        if (!identical(_core, core)) {
          _emitTask(
            TaskUpdate(
              taskId: taskId,
              method: methodName,
              state: TaskUpdateState.cancelled,
              label: label,
            ),
          );
          throw CoreException(
            status: CoreStatusCodes.ok,
            code: 'E_CANCELLED',
            message: 'The core client was closed before the result was read.',
            taskId: taskId,
          );
        }
        final result = _readResult(core, taskId);
        _emitTask(
          TaskUpdate(
            taskId: taskId,
            method: methodName,
            state: TaskUpdateState.completed,
            label: label,
          ),
        );
        return result;
      }

      _emitTask(
        TaskUpdate(
          taskId: taskId,
          method: methodName,
          state: terminal.state == CoreTaskState.cancelled
              ? TaskUpdateState.cancelled
              : TaskUpdateState.failed,
          label: label,
          message: terminal.message,
        ),
      );
      throw _taskException(terminal, taskId);
    } finally {
      if (taskId != 0) {
        pump.forgetTask(taskId);
        // `close()` may have shut the core down while this future was pending.
        // A non-terminal task (pump closed, callback threw) is left to the
        // core so cancel still works and the TTL sweep reclaims it later.
        if (terminalKnown && identical(_core, core)) {
          try {
            _releaseFn(core, taskId);
          } catch (_) {
            // Release is best-effort; the core also expires terminal tasks.
          }
        }
      }
    }
  }

  /// Internal transport API: best-effort system call shared by the methods
  /// that swallow every failure and answer `false` (`system.openUrl`,
  /// `system.openPath`, `system.notify`, `system.status.watch`).
  ///
  /// Submits [methodName], decodes the envelope and reports the truthiness of
  /// [dataKey]. Any failure — core unavailable, shutdown race, invalid
  /// envelope — collapses into `false` so callers degrade gracefully.
  Future<bool> tryRunBool(
    String methodName, {
    String paramsJson = '{}',
    required String dataKey,
    void Function(int done, int total)? onProgress,
  }) async {
    try {
      final jsonText = await runAsync(
        methodName,
        paramsJson: paramsJson,
        onProgress: onProgress,
      );
      final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
      return asMap(decoded['data'], methodName)[dataKey] == true;
    } catch (_) {
      return false;
    }
  }

  /// Ask the core to cancel [taskId].
  ///
  /// Cancellation itself is asynchronous: the pump observes the cancelled
  /// task-state event and completes the pending future with [CoreException].
  void cancel(int taskId) {
    final core = _requireCore();
    final status = _cancelFn(core, taskId);
    if (status != CoreStatusCodes.ok) {
      throw CoreException(
        status: status,
        code: CoreStatusCodes.nameOf(status),
        message: 'archoera_cancel failed for task $taskId.',
        taskId: taskId,
      );
    }
  }

  /// Shut down the core. Safe to call more than once.
  void close() {
    if (_closed) return;
    _closed = true;

    // Stop new wakeups, then let the core join its workers. The NativeCallable
    // must stay alive until shutdown returns: a worker may already hold the
    // wake target and call it while joining.
    final pump = _pump;
    _pump = null;
    pump?.detach();

    final core = _core;
    _core = null;
    try {
      if (core != null) {
        _shutdownFn(core);
      }
    } catch (_) {
      // A shutdown failure must not crash widget disposal.
    } finally {
      pump?.dispose();
      _taskUpdates.close();
    }
  }

  void _emitTask(TaskUpdate update) {
    if (_closed || _taskUpdates.isClosed) return;
    _taskUpdates.add(update);
  }

  int _startTask(
    ffi.Pointer<ffi.Void> core,
    String methodName, {
    String paramsJson = '{}',
  }) {
    final method = methodName.toNativeUtf8();
    final params = paramsJson.toNativeUtf8();
    final taskIdPtr = calloc<ffi.Uint64>();
    final errorPtr = calloc<_ArchoeraErrorNative>();

    try {
      final status = _callFn(core, method, params, taskIdPtr, errorPtr);
      if (status != CoreStatusCodes.ok) {
        throw _exceptionForStatus(status, errorPtr.ref);
      }

      final id = taskIdPtr.value;
      if (id == 0) {
        throw CoreException(
          status: CoreStatusCodes.ok,
          code: 'E_INTERNAL',
          message:
              'archoera_call($methodName) returned OK but the task id was 0.',
        );
      }
      return id;
    } finally {
      calloc.free(errorPtr);
      calloc.free(taskIdPtr);
      malloc.free(params);
      malloc.free(method);
    }
  }

  CoreException _taskException(CoreEvent event, int taskId) {
    final state = event.state?.name ?? 'unknown';
    final fallback = event.state == CoreTaskState.cancelled
        ? 'E_CANCELLED'
        : 'E_INTERNAL';
    final message = event.message;

    return CoreException(
      status: event.code,
      code: CoreStatusCodes.nameOf(event.code, fallback: fallback),
      message: message == null || message.isEmpty
          ? 'Task $taskId ended in state $state.'
          : message,
      taskId: taskId,
    );
  }

  String _readResult(ffi.Pointer<ffi.Void> core, int taskId) {
    final outLength = calloc<ffi.IntPtr>();
    final errorPtr = calloc<_ArchoeraErrorNative>();
    ffi.Pointer<Utf8> buffer = ffi.nullptr;

    try {
      // Phase 1: ask for the required size.
      var status = _resultFn(core, taskId, ffi.nullptr, 0, outLength, errorPtr);
      if (status != CoreStatusCodes.ok && status != CoreStatusCodes.eBuffer) {
        throw _exceptionForStatus(status, errorPtr.ref, taskId: taskId);
      }

      var length = outLength.value;
      if (length <= 0) {
        throw CoreException(
          status: status,
          code: 'E_INTERNAL',
          message:
              'archoera_result reported an invalid buffer length ($length).',
          taskId: taskId,
        );
      }

      // Phase 2: read into a buffer of the reported size.
      buffer = malloc<ffi.Uint8>(length).cast<Utf8>();
      status = _resultFn(core, taskId, buffer, length, outLength, errorPtr);

      if (status == CoreStatusCodes.eBuffer) {
        final needed = outLength.value;
        if (needed <= length) {
          throw CoreException(
            status: status,
            code: 'E_BUFFER',
            message:
                'archoera_result requested a larger buffer but did not '
                'increase out_len.',
            taskId: taskId,
          );
        }

        malloc.free(buffer);
        buffer = malloc<ffi.Uint8>(needed).cast<Utf8>();
        length = needed;
        status = _resultFn(core, taskId, buffer, length, outLength, errorPtr);
      }

      if (status != CoreStatusCodes.ok) {
        throw _exceptionForStatus(status, errorPtr.ref, taskId: taskId);
      }

      return buffer.toDartString();
    } finally {
      if (buffer != ffi.nullptr) {
        malloc.free(buffer);
      }
      calloc.free(errorPtr);
      calloc.free(outLength);
    }
  }

  /// Internal transport API: decode an object envelope without treating
  /// `ok: false` as an exception.
  Map<String, Object?> decodeRawEnvelope(
    String jsonText, {
    required String fallbackMethod,
  }) {
    Object? decoded;
    try {
      decoded = jsonDecode(jsonText);
    } on FormatException catch (error) {
      throw invalidEnvelope(fallbackMethod, 'invalid JSON', detail: error.message);
    }

    if (decoded is! Map<String, Object?>) {
      throw invalidEnvelope(fallbackMethod, 'a non-object JSON result');
    }

    return decoded;
  }

  /// Internal transport API: decode a method result envelope and surface
  /// `ok: false` as an exception.
  Map<String, Object?> decodeEnvelope(
    String jsonText, {
    required String fallbackMethod,
  }) {
    final decoded = decodeRawEnvelope(jsonText, fallbackMethod: fallbackMethod);

    if (decoded['ok'] == false) {
      final error = decoded['error'];
      if (error is Map) {
        throw CoreException(
          status: CoreStatusCodes.ok,
          code: error['code']?.toString() ?? 'ERROR',
          message:
              error['message']?.toString() ??
              '$fallbackMethod reported failure.',
          detail: error['details']?.toString(),
        );
      }
      throw CoreException(
        status: CoreStatusCodes.ok,
        code: 'ERROR',
        message: '$fallbackMethod reported failure without an error object.',
      );
    }

    return decoded;
  }

  /// Internal transport API: coerce an envelope entry to a `Map`, or throw.
  Map<String, Object?> asMap(Object? value, String method) {
    if (value is Map<String, Object?>) return value;
    if (value is Map) {
      return value.map((key, value) => MapEntry(key.toString(), value));
    }
    throw invalidEnvelope(method, 'no object');
  }

  /// Internal transport API: coerce an envelope entry to a list (or `[]`).
  List<Object?> asList(Object? value) =>
      value is List ? value.cast<Object?>() : const <Object?>[];

  /// Internal transport API: trim an envelope entry to text (or `null`).
  String? asString(Object? value) {
    if (value == null) return null;
    final text = value.toString().trim();
    return text.isEmpty ? null : text;
  }

  /// Internal transport API: coerce an envelope entry to `int` (or `null`).
  int? asInt(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    if (value is String) return int.tryParse(value.trim());
    return null;
  }

  ffi.Pointer<ffi.Void> _requireCore() {
    if (_closed) {
      throw const CoreException(
        status: CoreStatusCodes.ok,
        code: 'E_INVALID',
        message: 'The core client has already been closed.',
      );
    }

    final core = _core;
    if (core == null || core == ffi.nullptr) {
      throw const CoreException(
        status: CoreStatusCodes.ok,
        code: 'E_INVALID',
        message: 'The core client is not initialized.',
      );
    }
    return core;
  }

  CoreException _exceptionForStatus(
    int status,
    _ArchoeraErrorNative error, {
    int? taskId,
  }) {
    final statusCode = CoreStatusCodes.nameOf(status);
    final errorCode = CoreStatusCodes.names[error.code];
    final message = _readNativeString(error.message);
    final detail = _readNativeString(error.detail);

    return CoreException(
      status: status,
      code: errorCode ?? statusCode,
      message: message == null || message.isEmpty
          ? 'archoera_core call failed (status $statusCode).'
          : message,
      detail: detail,
      taskId: taskId,
    );
  }

  static String? _readNativeString(ffi.Pointer<Utf8> pointer) {
    if (pointer == ffi.nullptr) return null;
    try {
      return pointer.toDartString();
    } catch (_) {
      return null;
    }
  }
}
