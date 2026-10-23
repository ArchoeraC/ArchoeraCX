import 'dart:async';
import 'dart:convert';

import '../core_transport.dart';
import 'launch_log.dart';
import 'launch_memory.dart';
import 'launch_preparation.dart';
import 'launch_result.dart';

/// Launch domain binding for the native core: `launch.prepare/start/memory`
/// plus the `launch.log*` family.
mixin CoreClientLaunch on CoreTransport {
  /// Ask the native module whether a launch can start in [mode].
  ///
  /// `mode=online` requires an active Microsoft account with a stored
  /// Minecraft session; the C# module may refresh it before launching.
  Future<LaunchPreparation> prepareLaunch(
    String instanceId, {
    String mode = 'offline',
    bool requirePremium = false,
    bool requireMicrosoft = false,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'launch.prepare';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'mode': mode,
        'requirePremium': requirePremium,
        'requireMicrosoft': requireMicrosoft,
      }),
      onProgress: onProgress,
      label: label,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final preparation = LaunchPreparation.tryParse(data);
    if (preparation == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'an invalid preparation');
    }
    return preparation;
  }

  /// Start a basic offline launch through the C# module.
  Future<LaunchResult> launchInstance(
    String instanceId, {
    String mode = 'offline',
    bool requirePremium = false,
    bool requireMicrosoft = false,
    Map<String, Object?>? hardware,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'launch.start';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'mode': mode,
        'requirePremium': requirePremium,
        'requireMicrosoft': requireMicrosoft,
        'hardware': ?hardware,
      }),
      onProgress: onProgress,
      label: label,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final pid = asInt(data['pid']);
    if (pid == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'no pid');
    }
    return LaunchResult(
      pid: pid,
      instanceId: asString(data['instanceId']) ?? instanceId,
      javaPath: asString(data['javaPath']) ?? '',
      javaVersion: asString(data['javaVersion']),
      accountName: asString(data['accountName']),
      logPath: asString(data['logPath']),
    );
  }

  /// Force-terminates the game process started for [instanceId].
  ///
  /// The pid is resolved by the C# module from the launch it started, so the
  /// UI can never target an arbitrary process. Returns `stopped: false` when
  /// the game already exited; callers refresh the running badge either way.
  Future<LaunchStopResult> stopLaunch(String instanceId) async {
    const methodName = 'launch.stop';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'instanceId': instanceId}),
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    return LaunchStopResult.fromNative(asMap(decoded['data'], methodName));
  }

  /// Heap the launcher would use for this instance, plus a warning code when
  /// the user forced a value that is too small for the game.
  ///
  /// The native module owns the sizing algorithm: version baseline + loader +
  /// enabled mods, capped by installed RAM.
  Future<LaunchMemoryPlan?> getLaunchMemory(
    String instanceId, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'launch.memory';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'instanceId': instanceId}),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    return LaunchMemoryPlan.tryParse(decoded['data']);
  }

  /// List the recent launch logs of an instance.
  ///
  /// Logs live under `<instance>/.archoera/logs`; the UI never reads them
  /// directly, it only renders what the module returns.
  Future<LaunchLogList> listLaunchLogs(
    String instanceId, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'launch.logs';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'instanceId': instanceId}),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final logs = LaunchLogList.tryParse(asMap(decoded['data'], methodName));
    if (logs == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'an invalid log list');
    }
    return logs;
  }

  /// Deletes stored launch logs; a running instance keeps its active log.
  ///
  /// Pass [instanceId] for one instance or [all] to sweep every instance.
  Future<LaunchLogClearResult> clearLaunchLogs({
    String? instanceId,
    bool all = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'launch.logs.clear';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': ?instanceId,
        if (all) 'all': true,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    return LaunchLogClearResult.fromNative(decoded['data']);
  }

  /// Read the tail of one launch log. Omit [fileName] to use the running
  /// launch's log, or the newest one.
  Future<LaunchLogSnapshot> readLaunchLog({
    required String instanceId,
    String? fileName,
    int lines = 400,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'launch.log.read';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'fileName': ?fileName,
        'lines': lines,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final snapshot = LaunchLogSnapshot.tryParse(
      asMap(decoded['data'], methodName),
    );
    if (snapshot == null) {
      throw CoreTransport.invalidEnvelope(
        methodName,
        'an invalid log snapshot',
      );
    }
    return snapshot;
  }

  /// Follow a running launch's log.
  ///
  /// The C# module pushes appended lines through the ABI progress callback, so
  /// this stream is wake-driven: no Dart timer, no file access, no polling
  /// loop. [LaunchLogTail.stop] cancels the native task.
  Future<LaunchLogTail> tailLaunchLog({
    required String instanceId,
    String? fileName,
    int lines = 400,
  }) async {
    const methodName = 'launch.log.tail';
    final controller = StreamController<String>();
    final started = Completer<int>();
    final finished = Completer<void>();

    void finish() {
      if (!controller.isClosed) controller.close();
      if (!finished.isCompleted) finished.complete();
    }

    unawaited(
      runAsync(
        methodName,
        paramsJson: jsonEncode(<String, Object?>{
          'instanceId': instanceId,
          'fileName': ?fileName,
          'lines': lines,
          'follow': true,
        }),
        onTaskStarted: (taskId) {
          if (!started.isCompleted) started.complete(taskId);
        },
        onDetailedProgress: (_, _, message) {
          if (message == null || message.isEmpty) return;
          if (!controller.isClosed) controller.add(message);
        },
      ).then<void>(
        (_) => finish(),
        onError: (Object error, StackTrace stack) {
          // Cancelling the follow is a normal stop, not a stream failure.
          if (error is CoreException && error.code == 'E_CANCELLED') {
            finish();
            return;
          }
          if (!controller.isClosed) controller.addError(error, stack);
          finish();
        },
      ),
    );

    final taskId = await started.future;
    return LaunchLogTail(
      chunks: controller.stream,
      done: finished.future,
      stop: () {
        try {
          cancel(taskId);
        } catch (_) {
          // The task already finished or the core is gone; both are fine.
        }
      },
    );
  }
}
