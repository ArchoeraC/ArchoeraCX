/// Result of building a loader install plan (Forge/NeoForge).
class LoaderPlanResult {
  const LoaderPlanResult({
    required this.plan,
    required this.versionId,
    required this.versionJson,
    required this.requiresJava,
    this.installerPath,
    this.installRoot = '',
    this.shareLoaderFiles = true,
  });

  final Map<String, Object?> plan;
  final String versionId;
  final String versionJson;

  /// Forge/NeoForge processors need a JVM. The download/commit phase itself
  /// does not invoke Java.
  final bool requiresJava;

  /// Instance-relative installer file written by the plan.
  final String? installerPath;

  /// Root that owns `libraries/` and `versions/<id>/` for this install: the
  /// shared game root, or the instance directory when the user turned sharing
  /// off. Commit and repair must target this root.
  final String installRoot;

  /// True when the files are shared with other instances of the same loader
  /// build.
  final bool shareLoaderFiles;
}

class LoaderCommitResult {
  const LoaderCommitResult({
    required this.versionId,
    required this.versionPath,
  });

  final String versionId;
  final String versionPath;
}
