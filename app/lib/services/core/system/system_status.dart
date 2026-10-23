/// Machine load reported by `system.status`.
///
/// Every metric is optional: a platform may not expose CPU sampling, load
/// average, or uptime, and the page hides missing rows instead of showing a
/// fake zero.
final class SystemStatus {
  const SystemStatus({
    required this.cores,
    this.platform,
    this.os,
    this.cpuPercent,
    this.loadAverage1m,
    this.memoryTotalMb,
    this.memoryAvailableMb,
    this.memoryPercent,
    this.diskPath,
    this.diskTotalBytes,
    this.diskFreeBytes,
    this.diskPercent,
    this.launcherWorkingSetMb,
    this.launcherManagedMb,
    this.launcherUptimeSeconds,
    this.systemUptimeSeconds,
  });

  final int cores;
  final String? platform;
  final String? os;

  final double? cpuPercent;
  final double? loadAverage1m;

  final int? memoryTotalMb;
  final int? memoryAvailableMb;
  final double? memoryPercent;

  final String? diskPath;
  final int? diskTotalBytes;
  final int? diskFreeBytes;
  final double? diskPercent;

  final int? launcherWorkingSetMb;
  final int? launcherManagedMb;
  final double? launcherUptimeSeconds;
  final double? systemUptimeSeconds;

  bool get hasMemory => (memoryTotalMb ?? 0) > 0;
  bool get hasDisk => (diskTotalBytes ?? 0) > 0;

  static SystemStatus? tryParse(Object? value) {
    if (value is! Map) return null;
    final cpu = value['cpu'];
    final memory = value['memory'];
    final disk = value['disk'];
    final process = value['process'];
    final system = value['system'];

    return SystemStatus(
      cores: _int(value['cores']) ?? 0,
      platform: value['platform'] as String?,
      os: value['os'] as String?,
      cpuPercent: _double(cpu is Map ? cpu['usagePercent'] : null),
      loadAverage1m: _double(cpu is Map ? cpu['loadAverage1m'] : null),
      memoryTotalMb: _int(memory is Map ? memory['totalMb'] : null),
      memoryAvailableMb: _int(memory is Map ? memory['availableMb'] : null),
      memoryPercent: _double(memory is Map ? memory['usagePercent'] : null),
      diskPath: disk is Map ? disk['path'] as String? : null,
      diskTotalBytes: _int(disk is Map ? disk['totalBytes'] : null),
      diskFreeBytes: _int(disk is Map ? disk['freeBytes'] : null),
      diskPercent: _double(disk is Map ? disk['usagePercent'] : null),
      launcherWorkingSetMb: _int(process is Map ? process['workingSetMb'] : null),
      launcherManagedMb: _int(process is Map ? process['managedMb'] : null),
      launcherUptimeSeconds: _double(
        process is Map ? process['uptimeSeconds'] : null,
      ),
      systemUptimeSeconds: _double(
        system is Map ? system['uptimeSeconds'] : null,
      ),
    );
  }

  static int? _int(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    return null;
  }

  static double? _double(Object? value) {
    if (value is num) return value.toDouble();
    return null;
  }
}
