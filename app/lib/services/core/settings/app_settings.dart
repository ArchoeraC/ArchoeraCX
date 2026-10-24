import '../../../models/launch_options.dart';

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
    this.wrapperCommand,
    this.preLoadCommand,
    this.preLaunchCommand,
    this.postExitCommand,
    this.environment = const <String, String>{},
    this.downloadSource = 'auto',
    this.downloadConcurrency = 16,
    this.downloadPerHost = 8,
    this.downloadProxy,
    this.windowWidth = 854,
    this.windowHeight = 480,
    this.fullscreen = false,
    this.downloadChunkSizeKb = 4096,
    this.downloadRangeThresholdKb = 8192,
    this.logRetentionFiles = 10,
    this.logRetentionMegabytes = 32,
    this.shareLoaderFiles = true,
    this.memoryMode = 'auto',
    this.gameOptimization = true,
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

  /// Process wrapper prepended to the Java command; null means none.
  final String? wrapperCommand;

  /// Command run before the instance is prepared. Runs as the current user
  /// without a shell in the instance folder; null means none.
  final String? preLoadCommand;

  /// Command run after preparation and immediately before Java starts. A
  /// failure aborts the launch.
  final String? preLaunchCommand;

  /// Command run after the game process exits. A failure is only logged.
  final String? postExitCommand;

  /// Extra environment variables for the game process. Values are never
  /// written to logs.
  final Map<String, String> environment;

  /// Download source preference: `auto`, `official` or `mirror`.
  final String downloadSource;

  /// Global parallel download streams (1..64).
  final int downloadConcurrency;

  /// Streams per host (0..64, 0 = unlimited).
  final int downloadPerHost;

  /// Manual download proxy URL; null follows the system proxy.
  final String? downloadProxy;

  /// Game window size used for `${resolution_width}` / `${resolution_height}`.
  final int windowWidth;
  final int windowHeight;

  /// Adds `--fullscreen` to the game arguments.
  final bool fullscreen;

  /// Human label for the window preset, e.g. `1280 × 720`.
  String get windowLabel => '$windowWidth × $windowHeight';

  /// Ranged-download chunk size in KiB (128..8192).
  final int downloadChunkSizeKb;

  /// Files above this size use ranged chunks; 0 disables chunking.
  final int downloadRangeThresholdKb;

  /// Launch logs kept per instance (2..50).
  final int logRetentionFiles;

  /// Total launch-log budget per instance in MiB (8..512).
  final int logRetentionMegabytes;

  /// Installs Forge/NeoForge libraries and the merged loader profile once in
  /// the game root, shared by every instance of that loader build. When false
  /// each instance keeps its own `libraries/` and `versions/<id>/`.
  final bool shareLoaderFiles;

  /// `auto` derives the heap from the version, loader, mod count and installed
  /// RAM; `manual` uses [memoryMinMb] / [memoryMaxMb] and only warns when they
  /// are too small.
  final String memoryMode;

  /// Lets the launcher add JVM tuning and prefer the discrete GPU.
  final bool gameOptimization;

  /// True when the launcher picks the heap itself.
  bool get memoryAuto => memoryMode == 'auto';

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
      wrapperCommand: _wrapperCommand(value['wrapperCommand']),
      preLoadCommand: _hookCommand(value['preLoadCommand']),
      preLaunchCommand: _hookCommand(value['preLaunchCommand']),
      postExitCommand: _hookCommand(value['postExitCommand']),
      environment:
          parseLaunchEnvironmentWire(value['environment']) ??
          const <String, String>{},
      downloadSource: _downloadSource(value['downloadSource']),
      downloadConcurrency: (_int(value['downloadConcurrency']) ?? 16).clamp(
        1,
        64,
      ),
      downloadPerHost: (_int(value['downloadPerHost']) ?? 8).clamp(0, 64),
      downloadProxy: _string(value['downloadProxy']),
      windowWidth: (_int(value['windowWidth']) ?? 854).clamp(640, 7680),
      windowHeight: (_int(value['windowHeight']) ?? 480).clamp(480, 4320),
      fullscreen: value['fullscreen'] == true,
      downloadChunkSizeKb: (_int(value['downloadChunkSizeKb']) ?? 4096).clamp(
        128,
        8192,
      ),
      downloadRangeThresholdKb:
          (_int(value['downloadRangeThresholdKb']) ?? 8192).clamp(0, 65536),
      logRetentionFiles: (_int(value['logRetentionFiles']) ?? 10).clamp(2, 50),
      logRetentionMegabytes: (_int(value['logRetentionMb']) ?? 32).clamp(
        8,
        512,
      ),
      shareLoaderFiles: value['shareLoaderFiles'] != false,
      memoryMode: value['memoryMode'] == 'manual' ? 'manual' : 'auto',
      gameOptimization: value['gameOptimization'] != false,
    );
  }

  static String? _wrapperCommand(Object? value) {
    if (value is! String) return null;
    final text = value.trim();
    return text.isEmpty ? null : text;
  }

  /// A global lifecycle command: an absent, non-string or blank value means
  /// "no command". Per-instance overrides keep an empty string instead.
  static String? _hookCommand(Object? value) => _wrapperCommand(value);

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
