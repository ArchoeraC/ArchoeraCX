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
