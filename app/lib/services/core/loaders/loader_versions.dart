/// Loader build metadata returned by `loader.versions`.
final class LoaderVersionInfo {
  const LoaderVersionInfo({
    required this.version,
    this.stable = false,
    this.latest = false,
    this.recommended = false,
  });

  final String version;
  final bool stable;
  final bool latest;
  final bool recommended;

  static LoaderVersionInfo? tryParse(Object? value) {
    if (value is! Map) return null;
    final version = value['version']?.toString().trim();
    if (version == null || version.isEmpty) return null;
    return LoaderVersionInfo(
      version: version,
      stable: value['stable'] == true,
      latest: value['latest'] == true,
      recommended: value['recommended'] == true,
    );
  }
}
