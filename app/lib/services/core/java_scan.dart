/// Value types for the `java.scan` method result.
///
/// Parsing happens in `CoreClient`; these models contain only Dart values and
/// never hold native pointers. The shape mirrors the module result envelope:
///
/// ```json
/// {"ok":true,"data":{"items":[{"path":"/usr/bin/java","version":"21.0.4",
/// "major":21,"vendor":"Eclipse Adoptium","arch":"amd64","source":"PATH"}],
/// "count":1},"meta":{"schemaVersion":1}}
/// ```
library;

/// A single Java runtime reported by the C# business module.
final class JavaRuntime {
  const JavaRuntime({
    required this.path,
    this.version,
    this.major,
    this.vendor,
    this.arch,
    this.source,
  });

  /// Absolute executable path, for example `/usr/bin/java`.
  final String path;

  /// Full version string, for example `21.0.4`.
  final String? version;

  /// Major version, for example `21`.
  final int? major;

  /// Vendor string, for example `Eclipse Adoptium`.
  final String? vendor;

  /// Architecture string, for example `amd64`.
  final String? arch;

  /// Discovery source, for example `PATH` or `JAVA_HOME`.
  final String? source;

  /// Locale-independent version label used by the UI.
  ///
  /// Prefers [version] and falls back to the major version, or `unknown`.
  String get displayVersion {
    final full = version;
    if (full != null && full.isNotEmpty) return full;
    final majorVersion = major;
    if (majorVersion != null) return 'Java $majorVersion';
    return 'unknown';
  }

  /// Returns `null` when [value] is not a usable Java runtime entry.
  static JavaRuntime? tryParse(Object? value) {
    if (value is! Map) return null;

    final path = _asString(value['path']);
    if (path == null || path.isEmpty) return null;

    return JavaRuntime(
      path: path,
      version: _asString(value['version']),
      major: _asInt(value['major']),
      vendor: _asString(value['vendor']),
      arch: _asString(value['arch']),
      source: _asString(value['source']),
    );
  }

  static String? _asString(Object? value) {
    if (value == null) return null;
    final text = value.toString().trim();
    return text.isEmpty ? null : text;
  }

  static int? _asInt(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    if (value is String) return int.tryParse(value.trim());
    return null;
  }
}

/// The parsed result of a successful `java.scan` call.
final class JavaScanResult {
  const JavaScanResult({
    required this.items,
    required this.count,
    required this.rawJson,
    this.schemaVersion,
    this.requestId,
  });

  /// Valid runtimes, in the order reported by the module.
  final List<JavaRuntime> items;

  /// Count reported by the module; falls back to [items] length when absent.
  final int count;

  /// The complete decoded result envelope, kept for diagnostics.
  final Map<String, Object?> rawJson;

  final int? schemaVersion;
  final String? requestId;

  bool get isEmpty => items.isEmpty;
}
