/// Result of building a loader install plan (Forge/NeoForge).
class LoaderPlanResult {
  const LoaderPlanResult({
    required this.plan,
    required this.versionId,
    required this.versionJson,
    required this.requiresJava,
  });

  final Map<String, Object?> plan;
  final String versionId;
  final String versionJson;

  /// Forge/NeoForge processors need a JVM. The download/commit phase itself
  /// does not invoke Java.
  final bool requiresJava;
}

class LoaderCommitResult {
  const LoaderCommitResult({
    required this.versionId,
    required this.versionPath,
  });

  final String versionId;
  final String versionPath;
}
