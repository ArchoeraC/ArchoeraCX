/// Result of `maintenance.cleanPartials`.
class PartialCleanupResult {
  const PartialCleanupResult({
    required this.deletedFiles,
    required this.freedBytes,
    this.scannedFiles = 0,
  });

  final int deletedFiles;
  final int freedBytes;

  /// Regular files visited while scanning; useful for diagnostics.
  final int scannedFiles;

  static PartialCleanupResult fromNative(Object? value) {
    if (value is! Map) {
      return const PartialCleanupResult(deletedFiles: 0, freedBytes: 0);
    }
    int read(String key) => value[key] is num
        ? (value[key] as num).toInt()
        : 0;
    return PartialCleanupResult(
      deletedFiles: read('deletedFiles'),
      freedBytes: read('freedBytes'),
      scannedFiles: read('scannedFiles'),
    );
  }
}

/// One deletable group (`libraries` or `assets`) of an unused-dependency scan.
class UnusedDependencyGroupPlan {
  const UnusedDependencyGroupPlan({
    required this.kind,
    required this.files,
    required this.bytes,
    required this.samples,
  });

  final String kind;
  final int files;
  final int bytes;
  final List<String> samples;

  static UnusedDependencyGroupPlan? fromNative(String kind, Object? value) {
    if (value is! Map) return null;
    int read(String key) => value[key] is num ? (value[key] as num).toInt() : 0;
    return UnusedDependencyGroupPlan(
      kind: kind,
      files: read('files'),
      bytes: read('bytes'),
      samples: (value['samples'] as List?)?.cast<String>() ?? const [],
    );
  }
}

/// Scan outcome for one configured instance root.
class UnusedDependencyRootPlan {
  const UnusedDependencyRootPlan({
    required this.root,
    required this.status,
    this.reason,
    this.instanceCount = 0,
    this.libraries,
    this.assets,
  });

  final String root;
  final String status;
  final String? reason;
  final int instanceCount;
  final UnusedDependencyGroupPlan? libraries;
  final UnusedDependencyGroupPlan? assets;

  bool get skipped => status == 'skipped';

  static UnusedDependencyRootPlan fromNative(Object? value) {
    if (value is! Map) {
      return const UnusedDependencyRootPlan(root: '', status: 'skipped');
    }
    return UnusedDependencyRootPlan(
      root: value['root'] is String ? value['root'] as String : '',
      status: value['status'] is String ? value['status'] as String : 'skipped',
      reason: value['reason'] is String ? value['reason'] as String : null,
      instanceCount: value['instanceCount'] is num
          ? (value['instanceCount'] as num).toInt()
          : 0,
      libraries: UnusedDependencyGroupPlan.fromNative('libraries', value['libraries']),
      assets: UnusedDependencyGroupPlan.fromNative('assets', value['assets']),
    );
  }
}

/// Result of `maintenance.planUnusedDependencies` and
/// `maintenance.deleteUnusedDependencies`. For a plan the totals describe
/// what would be freed; after a delete they describe what was removed.
class UnusedDependencyPlan {
  const UnusedDependencyPlan({
    required this.totalFiles,
    required this.totalBytes,
    required this.limitReached,
    required this.roots,
  });

  final int totalFiles;
  final int totalBytes;
  final bool limitReached;
  final List<UnusedDependencyRootPlan> roots;

  static UnusedDependencyPlan fromNative(Object? value) {
    if (value is! Map) {
      return const UnusedDependencyPlan(
        totalFiles: 0,
        totalBytes: 0,
        limitReached: true,
        roots: [],
      );
    }
    int read(String key) => value[key] is num ? (value[key] as num).toInt() : 0;
    return UnusedDependencyPlan(
      totalFiles: read('totalFiles'),
      totalBytes: read('totalBytes'),
      limitReached: value['limitReached'] == true,
      roots: (value['roots'] as List? ?? const [])
          .map(UnusedDependencyRootPlan.fromNative)
          .toList(),
    );
  }
}
