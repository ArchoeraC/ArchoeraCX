/// Result of `java.runtime.plan`: the Mojang runtime download plan plus the
/// metadata the UI needs to describe it before the transfer starts.
class JavaRuntimePlan {
  const JavaRuntimePlan({
    required this.plan,
    required this.root,
    required this.major,
    required this.component,
    required this.version,
    this.fileCount = 0,
    this.totalBytes = 0,
  });

  /// Download plan to hand to `download.execute`.
  final Map<String, Object?> plan;

  /// Root that receives `runtime/java-<major>/`.
  final String root;

  final int major;

  /// Catalog component name, e.g. `java-runtime-delta`.
  final String component;

  /// Runtime version, e.g. `21.0.7`.
  final String version;

  final int fileCount;
  final int totalBytes;

  static JavaRuntimePlan? tryParse(Object? value) {
    if (value is! Map) return null;
    final plan = value['plan'];
    if (plan is! Map) return null;
    final root = value['root'];
    final major = value['major'];
    return JavaRuntimePlan(
      plan: Map<String, Object?>.from(plan),
      root: root is String ? root : '',
      major: major is num ? major.toInt() : 0,
      component: value['component']?.toString() ?? '',
      version: value['version']?.toString() ?? '',
      fileCount: value['fileCount'] is num
          ? (value['fileCount'] as num).toInt()
          : 0,
      totalBytes: value['totalBytes'] is num
          ? (value['totalBytes'] as num).toInt()
          : 0,
    );
  }
}
