class LaunchResult {
  const LaunchResult({
    required this.pid,
    required this.instanceId,
    required this.javaPath,
    this.javaVersion,
    this.accountName,
    this.logPath,
  });

  final int pid;
  final String instanceId;
  final String javaPath;
  final String? javaVersion;
  final String? accountName;
  final String? logPath;
}

/// Result of `launch.wait`: whether the JVM is still running and its code.
class LaunchExit {
  const LaunchExit({
    required this.instanceId,
    required this.running,
    this.exitCode,
    this.logPath,
  });

  final String instanceId;
  final bool running;
  final int? exitCode;
  final String? logPath;

  static LaunchExit fromNative(String fallbackInstanceId, Object? value) {
    final data = value is Map ? value : const <Object?, Object?>{};
    return LaunchExit(
      instanceId: data['instanceId']?.toString() ?? fallbackInstanceId,
      running: data['running'] == true,
      exitCode: data['exitCode'] is num
          ? (data['exitCode'] as num).toInt()
          : null,
      logPath: data['logPath']?.toString(),
    );
  }
}

/// Result of `launch.stop`: the game process that was force-terminated.
///
/// [stopped] is false when nothing was running any more (the game exited on
/// its own between the confirmation and the call); the UI still refreshes the
/// status instead of reporting an error.
class LaunchStopResult {
  const LaunchStopResult({
    required this.instanceId,
    required this.stopped,
    this.pid,
  });

  final String instanceId;
  final bool stopped;
  final int? pid;

  static LaunchStopResult fromNative(Object? value) {
    final data = value is Map ? value : const <Object?, Object?>{};
    return LaunchStopResult(
      instanceId: data['instanceId']?.toString() ?? '',
      stopped: data['stopped'] == true,
      pid: data['pid'] is num ? (data['pid'] as num).toInt() : null,
    );
  }
}
