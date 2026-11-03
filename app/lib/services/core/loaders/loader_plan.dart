/// Result of building a loader install plan (Forge/NeoForge).
class LoaderPlanResult {
  const LoaderPlanResult({
    required this.plan,
    required this.versionId,
    required this.versionJson,
    required this.requiresJava,
    this.javaRequiredSteps = const [],
    this.installerPath,
    this.installRoot = '',
    this.profilePath = '',
    this.shareLoaderFiles = true,
  });

  final Map<String, Object?> plan;
  final String versionId;
  final String versionJson;

  /// True when at least one client processor has no native implementation.
  /// Fabric/Quilt are always false; this is about the installer, not about
  /// launching the game (which always needs a Java runtime).
  final bool requiresJava;

  /// Processor steps without a native implementation, for example
  /// `1: net.neoforged.installertools.ConsoleTool`.
  final List<String> javaRequiredSteps;

  /// Instance-relative installer file written by the plan.
  final String? installerPath;

  /// Root that owns `libraries/` and the loader's download cache for this
  /// install: the shared game root, or the instance directory when the user
  /// turned sharing off. Commit and repair must target this root.
  final String installRoot;

  /// Absolute path of the instance-owned `<instance>.json`. The merged
  /// profile is written here, never to the shared `versions/` folder.
  final String profilePath;

  /// True when libraries and processor output are shared with other instances
  /// of the same loader build. The launch profile is always per-instance.
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
