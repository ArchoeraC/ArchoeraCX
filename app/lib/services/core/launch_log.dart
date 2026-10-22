/// One launch log file on disk, as listed by `launch.logs`.
class LaunchLogFile {
  const LaunchLogFile({
    required this.fileName,
    required this.size,
    this.modifiedAt,
    this.active = false,
  });

  final String fileName;
  final int size;
  final DateTime? modifiedAt;

  /// True for the log of the launch that is still running.
  final bool active;

  static LaunchLogFile? tryParse(Object? value) {
    if (value is! Map) return null;
    final fileName = value['fileName'];
    if (fileName is! String || fileName.isEmpty) return null;
    return LaunchLogFile(
      fileName: fileName,
      size: value['size'] is num ? (value['size'] as num).toInt() : 0,
      modifiedAt: DateTime.tryParse(value['modifiedAt']?.toString() ?? ''),
      active: value['active'] == true,
    );
  }
}

/// Result of `launch.logs`: the instance's recent launches.
class LaunchLogList {
  const LaunchLogList({
    required this.instanceId,
    required this.instanceName,
    required this.logDirectory,
    required this.running,
    required this.logs,
    this.pid,
    this.activeLog,
    this.totalSize = 0,
    this.retentionMaxFiles = 0,
    this.retentionMaxBytes = 0,
  });

  final String instanceId;
  final String instanceName;
  final String logDirectory;

  /// Whether a game process started by this launcher session is still alive.
  final bool running;
  final int? pid;
  final String? activeLog;
  final List<LaunchLogFile> logs;

  /// Bytes occupied by all launch logs on disk (not only the listed ones).
  final int totalSize;

  /// Retention applied on the next launch: newest files kept, byte budget.
  final int retentionMaxFiles;
  final int retentionMaxBytes;

  static LaunchLogList? tryParse(Object? value) {
    if (value is! Map) return null;
    final instanceId = value['instanceId'];
    if (instanceId is! String) return null;
    final logs = <LaunchLogFile>[];
    final raw = value['logs'];
    if (raw is List) {
      for (final item in raw) {
        final file = LaunchLogFile.tryParse(item);
        if (file != null) logs.add(file);
      }
    }

    return LaunchLogList(
      instanceId: instanceId,
      instanceName: value['instanceName']?.toString() ?? '',
      logDirectory: value['logDirectory']?.toString() ?? '',
      running: value['running'] == true,
      pid: value['pid'] is num ? (value['pid'] as num).toInt() : null,
      activeLog: value['activeLog']?.toString(),
      logs: List.unmodifiable(logs),
      totalSize: value['totalSize'] is num
          ? (value['totalSize'] as num).toInt()
          : 0,
      retentionMaxFiles: value['retentionMaxFiles'] is num
          ? (value['retentionMaxFiles'] as num).toInt()
          : 0,
      retentionMaxBytes: value['retentionMaxBytes'] is num
          ? (value['retentionMaxBytes'] as num).toInt()
          : 0,
    );
  }
}

/// Result of `launch.log.read`: the tail of one log file.
class LaunchLogSnapshot {
  const LaunchLogSnapshot({
    required this.instanceId,
    required this.instanceName,
    required this.logDirectory,
    required this.running,
    required this.truncated,
    required this.size,
    required this.lines,
    this.fileName,
  });

  final String instanceId;
  final String instanceName;
  final String logDirectory;
  final bool running;

  /// True when older lines were dropped because the log is larger than the
  /// read window.
  final bool truncated;
  final int size;
  final String? fileName;
  final List<String> lines;

  static LaunchLogSnapshot? tryParse(Object? value) {
    if (value is! Map) return null;
    final instanceId = value['instanceId'];
    if (instanceId is! String) return null;
    final lines = <String>[];
    final raw = value['lines'];
    if (raw is List) {
      for (final item in raw) {
        if (item is String) lines.add(item);
      }
    }

    return LaunchLogSnapshot(
      instanceId: instanceId,
      instanceName: value['instanceName']?.toString() ?? '',
      logDirectory: value['logDirectory']?.toString() ?? '',
      running: value['running'] == true,
      truncated: value['truncated'] == true,
      size: value['size'] is num ? (value['size'] as num).toInt() : 0,
      fileName: value['fileName']?.toString(),
      lines: List.unmodifiable(lines),
    );
  }
}

/// Live log stream returned by [CoreClient.tailLaunchLog].
///
/// [chunks] carries batches of appended lines (newline separated). The stream
/// ends when the game exits, when [stop] is called, or when the module hits its
/// follow limit.
class LaunchLogTail {
  LaunchLogTail({required this.chunks, required this.done, required this.stop});

  final Stream<String> chunks;
  final Future<void> done;
  final void Function() stop;
}
