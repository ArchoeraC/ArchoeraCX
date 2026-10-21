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
