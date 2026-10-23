/// Result of `launch.prepare`.
///
/// Online launch is intentionally reserved: the native module reports whether
/// the account and feature are ready without starting a process.
final class LaunchPreparation {
  const LaunchPreparation({
    required this.instanceId,
    required this.mode,
    required this.ready,
    this.reason,
    this.requiresEntitlement = false,
    this.requiresCertificates = false,
    this.filesReady = true,
    this.fileIssue,
    this.autoRepairAvailable = false,
    this.accountKind,
  });

  final String instanceId;
  final String mode;
  final bool ready;
  final String? reason;
  final bool requiresEntitlement;
  final bool requiresCertificates;

  /// False when libraries/client jar/asset index are missing on disk.
  final bool filesReady;

  /// Stable code such as `LIBRARIES_MISSING`, or null when files are ready.
  final String? fileIssue;

  /// Forge/NeoForge instances can rebuild missing files and processors.
  final bool autoRepairAvailable;

  final String? accountKind;

  static LaunchPreparation? tryParse(Object? value) {
    if (value is! Map) return null;
    final instanceId = value['instanceId']?.toString();
    final mode = value['mode']?.toString();
    if (instanceId == null || mode == null) return null;
    return LaunchPreparation(
      instanceId: instanceId,
      mode: mode,
      ready: value['ready'] == true,
      reason: value['reason']?.toString(),
      requiresEntitlement: value['requiresEntitlement'] == true,
      requiresCertificates: value['requiresCertificates'] == true,
      filesReady: value['filesReady'] != false,
      fileIssue: value['fileIssue']?.toString(),
      autoRepairAvailable: value['autoRepairAvailable'] == true,
      accountKind: value['accountKind']?.toString(),
    );
  }
}
