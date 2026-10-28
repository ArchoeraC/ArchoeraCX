/// Plan returned by `version.plan`.
final class VanillaInstallPlan {
  const VanillaInstallPlan({
    required this.plan,
    required this.versionId,
    required this.versionJson,
    required this.installRoot,
    this.profilePath = '',
  });

  final Map<String, Object?> plan;
  final String versionId;
  final String versionJson;
  final String installRoot;

  /// Absolute `<instance>/<instance>.json` for the merged launch profile.
  final String profilePath;
}
