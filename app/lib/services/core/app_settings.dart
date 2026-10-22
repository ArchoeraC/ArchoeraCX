/// Native settings document returned by `settings.get` / `settings.set`.
class AppSettings {
  const AppSettings({
    required this.instancesRoot,
    required this.dataRoot,
    required this.settingsPath,
    this.instancesRoots = const <String>[],
    this.javaPath,
    this.javaAuto = true,
    this.memoryMinMb = 512,
    this.memoryMaxMb = 2048,
    this.jvmArgs,
    this.gcMode = 'auto',
    this.gameArgs,
    this.downloadSource = 'auto',
    this.downloadConcurrency = 16,
    this.downloadPerHost = 16,
    this.downloadProxy,
  });

  /// Primary/legacy root. Always the first entry of [instancesRoots].
  final String instancesRoot;

  /// All configured instance roots; first is the default for new instances.
  final List<String> instancesRoots;

  final String dataRoot;
  final String settingsPath;

  /// Manually selected Java executable; null means automatic selection.
  final String? javaPath;

  /// True when launch chooses Java automatically for the game version.
  final bool javaAuto;

  final int memoryMinMb;
  final int memoryMaxMb;
  final String? jvmArgs;
  final String gcMode;
  final String? gameArgs;

  /// Download source preference: `auto`, `official` or `mirror`.
  final String downloadSource;

  /// Global parallel download streams (1..64).
  final int downloadConcurrency;

  /// Streams per host (0..64, 0 = unlimited).
  final int downloadPerHost;

  /// Manual download proxy URL; null follows the system proxy.
  final String? downloadProxy;

  /// Human readable source label for tooltips/diagnostics.
  String get downloadSourceLabel => switch (downloadSource) {
    'official' => 'official',
    'mirror' => 'mirror',
    _ => 'auto',
  };

  /// Normalized roots with a legacy fallback for older native modules.
  List<String> get roots =>
      instancesRoots.isEmpty ? <String>[instancesRoot] : instancesRoots;

  static AppSettings? tryParse(Object? value) {
    if (value is! Map) return null;
    final instancesRoot = _string(value['instancesRoot']);
    final dataRoot = _string(value['dataRoot']);
    if (instancesRoot == null || dataRoot == null) return null;

    final roots = <String>[];
    final rawRoots = value['instancesRoots'];
    if (rawRoots is List) {
      for (final item in rawRoots) {
        final root = _string(item);
        if (root != null && !roots.contains(root)) roots.add(root);
      }
    }
    if (roots.isEmpty) roots.add(instancesRoot);
    if (!roots.contains(instancesRoot)) roots.insert(0, instancesRoot);

    return AppSettings(
      instancesRoot: instancesRoot,
      instancesRoots: List<String>.unmodifiable(roots),
      dataRoot: dataRoot,
      settingsPath: _string(value['settingsPath']) ?? '',
      javaPath: _string(value['javaPath']),
      javaAuto: value['javaAuto'] != false,
      memoryMinMb: _int(value['memoryMinMb']) ?? 512,
      memoryMaxMb: _int(value['memoryMaxMb']) ?? 2048,
      jvmArgs: _string(value['jvmArgs']),
      gcMode: _gcMode(value['gcMode']),
      gameArgs: _string(value['gameArgs']),
      downloadSource: _downloadSource(value['downloadSource']),
      downloadConcurrency: (_int(value['downloadConcurrency']) ?? 16).clamp(
        1,
        64,
      ),
      downloadPerHost: (_int(value['downloadPerHost']) ?? 16).clamp(0, 64),
      downloadProxy: _string(value['downloadProxy']),
    );
  }

  static String _downloadSource(Object? value) {
    final source = _string(value);
    return switch (source) {
      'official' || 'mirror' => source!,
      _ => 'auto',
    };
  }

  static String _gcMode(Object? value) {
    final mode = _string(value);
    if (mode == null) return 'auto';
    return switch (mode) {
      'g1gc' ||
      'zgc' ||
      'parallel' ||
      'serial' ||
      'shenandoah' ||
      'epsilon' => mode,
      _ => 'auto',
    };
  }

  static int? _int(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    return int.tryParse(value?.toString() ?? '');
  }

  static String? _string(Object? value) {
    if (value == null) return null;
    final text = value.toString().trim();
    return text.isEmpty ? null : text;
  }
}
