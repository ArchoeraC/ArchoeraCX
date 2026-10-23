import 'package:flutter/foundation.dart';

/// Loader families currently represented by the UI shell.
///
/// Fabric/Quilt/Forge/NeoForge are display values only for now; installation
/// and launch still go through the native core in later slices.
enum GameLoader {
  vanilla(''),
  fabric('0.16.10'),
  quilt('0.27.0'),
  forge('47.2.0'),
  neoforge('21.1.209');

  const GameLoader(this.defaultVersion);

  final String defaultVersion;
}

/// Per-instance launch overrides.
///
/// Every field is optional: a null field means "follow the global setting",
/// which is exactly what an absent key means on the wire. The C# owner applies
/// this as a merge patch, so `null` clears an override and a value sets it.
@immutable
class InstanceOverrides {
  const InstanceOverrides({
    this.memoryMinMb,
    this.memoryMaxMb,
    this.jvmArgs,
    this.gcMode,
    this.gameArgs,
    this.javaPath,
    this.windowWidth,
    this.windowHeight,
    this.fullscreen,
  });

  /// Supported garbage collectors, mirroring the global settings contract.
  static const gcModes = <String>[
    'auto',
    'g1gc',
    'zgc',
    'parallel',
    'serial',
    'shenandoah',
    'epsilon',
  ];

  final int? memoryMinMb;
  final int? memoryMaxMb;
  final String? jvmArgs;
  final String? gcMode;
  final String? gameArgs;

  /// Absolute path to a `java` executable.
  final String? javaPath;
  final int? windowWidth;
  final int? windowHeight;
  final bool? fullscreen;

  bool get isEmpty =>
      memoryMinMb == null &&
      memoryMaxMb == null &&
      jvmArgs == null &&
      gcMode == null &&
      gameArgs == null &&
      javaPath == null &&
      windowWidth == null &&
      windowHeight == null &&
      fullscreen == null;

  /// Parse the `overrides` object of an instance payload. Returns null when the
  /// object is absent or carries no override at all.
  static InstanceOverrides? tryParse(Object? value) {
    if (value is! Map) return null;
    String? text(Object? raw) {
      final value = raw?.toString().trim();
      return value == null || value.isEmpty ? null : value;
    }

    final gcMode = text(value['gcMode']);
    final overrides = InstanceOverrides(
      memoryMinMb: GameInstance._int(value['memoryMinMb']),
      memoryMaxMb: GameInstance._int(value['memoryMaxMb']),
      jvmArgs: text(value['jvmArgs']),
      gcMode: gcMode != null && gcModes.contains(gcMode) ? gcMode : null,
      gameArgs: text(value['gameArgs']),
      javaPath: text(value['javaPath']),
      windowWidth: GameInstance._int(value['windowWidth']),
      windowHeight: GameInstance._int(value['windowHeight']),
      fullscreen: value['fullscreen'] is bool
          ? value['fullscreen'] as bool
          : null,
    );
    return overrides.isEmpty ? null : overrides;
  }

  InstanceOverrides copyWith({
    int? memoryMinMb,
    int? memoryMaxMb,
    String? jvmArgs,
    String? gcMode,
    String? gameArgs,
    String? javaPath,
    int? windowWidth,
    int? windowHeight,
    bool? fullscreen,
  }) {
    return InstanceOverrides(
      memoryMinMb: memoryMinMb ?? this.memoryMinMb,
      memoryMaxMb: memoryMaxMb ?? this.memoryMaxMb,
      jvmArgs: jvmArgs ?? this.jvmArgs,
      gcMode: gcMode ?? this.gcMode,
      gameArgs: gameArgs ?? this.gameArgs,
      javaPath: javaPath ?? this.javaPath,
      windowWidth: windowWidth ?? this.windowWidth,
      windowHeight: windowHeight ?? this.windowHeight,
      fullscreen: fullscreen ?? this.fullscreen,
    );
  }
}

/// A UI-facing instance summary. This is intentionally not a persistence
/// model: the C#/SQLite owner will replace the mock provider later.
@immutable
class GameInstance {
  const GameInstance({
    required this.id,
    required this.name,
    required this.gameVersion,
    required this.loader,
    this.loaderVersion = '',
    this.modCount = 0,
    this.lastPlayed,
    this.favorite = false,
    this.rootPath = '',
    this.instanceRoot,
    this.custom = false,
    this.missing = false,
    this.overrides,
  });

  final String id;
  final String name;
  final String gameVersion;
  final GameLoader loader;
  final String loaderVersion;
  final int modCount;
  final DateTime? lastPlayed;
  final bool favorite;

  /// Absolute instance directory. Empty means "derive from the global
  /// instances root and id" in the UI.
  final String rootPath;

  /// Configured root containing [rootPath], or null for a custom folder.
  final String? instanceRoot;

  /// True when the instance lives outside every configured instance root.
  final bool custom;

  /// True when [rootPath] no longer exists on disk.
  final bool missing;

  /// Per-instance launch overrides, or null when the instance follows the
  /// global settings.
  final InstanceOverrides? overrides;

  static GameLoader loaderFromWire(String? value) => switch (value) {
    'fabric' => GameLoader.fabric,
    'quilt' => GameLoader.quilt,
    'forge' => GameLoader.forge,
    'neoforge' => GameLoader.neoforge,
    _ => GameLoader.vanilla,
  };

  static GameInstance? fromNative(Object? value) {
    if (value is! Map) return null;
    final id = value['id']?.toString();
    final name = value['name']?.toString();
    final gameVersion = value['gameVersion']?.toString();
    if (id == null || name == null || gameVersion == null) return null;

    return GameInstance(
      id: id,
      name: name,
      gameVersion: gameVersion,
      loader: loaderFromWire(value['loader']?.toString()),
      loaderVersion: value['loaderVersion']?.toString() ?? '',
      modCount: _int(value['modCount']) ?? 0,
      lastPlayed: _date(value['lastPlayedAt']),
      favorite: value['favorite'] == true,
      rootPath: value['rootPath']?.toString() ?? '',
      instanceRoot: value['instanceRoot']?.toString(),
      custom: value['custom'] == true,
      missing: value['missing'] == true,
      overrides: InstanceOverrides.tryParse(value['overrides']),
    );
  }

  static int? _int(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    if (value is String) return int.tryParse(value);
    return null;
  }

  static DateTime? _date(Object? value) {
    if (value is! String) return null;
    return DateTime.tryParse(value);
  }

  GameInstance copyWith({
    String? name,
    String? gameVersion,
    GameLoader? loader,
    String? loaderVersion,
    int? modCount,
    DateTime? lastPlayed,
    bool? favorite,
    String? rootPath,
    String? instanceRoot,
    bool? custom,
    bool? missing,
    InstanceOverrides? overrides,
  }) {
    return GameInstance(
      id: id,
      name: name ?? this.name,
      gameVersion: gameVersion ?? this.gameVersion,
      loader: loader ?? this.loader,
      loaderVersion: loaderVersion ?? this.loaderVersion,
      modCount: modCount ?? this.modCount,
      lastPlayed: lastPlayed ?? this.lastPlayed,
      favorite: favorite ?? this.favorite,
      rootPath: rootPath ?? this.rootPath,
      instanceRoot: instanceRoot ?? this.instanceRoot,
      custom: custom ?? this.custom,
      missing: missing ?? this.missing,
      overrides: overrides ?? this.overrides,
    );
  }
}

/// Result of `instance.sync`.
@immutable
class InstanceSyncResult {
  const InstanceSyncResult({
    required this.items,
    this.adopted = const <String>[],
    this.updated = const <String>[],
    this.missing = const <String>[],
    this.roots = const <InstanceRootStatus>[],
  });

  final List<GameInstance> items;
  final List<String> adopted;
  final List<String> updated;
  final List<String> missing;
  final List<InstanceRootStatus> roots;
}

@immutable
class InstanceRootStatus {
  const InstanceRootStatus({
    required this.path,
    required this.exists,
    required this.instanceCount,
  });

  final String path;
  final bool exists;
  final int instanceCount;
}

/// Result of `instance.delete`. [filesSkippedReason] carries a stable reason
/// code when the caller asked to delete local files but the directory was kept.
@immutable
class InstanceDeleteOutcome {
  const InstanceDeleteOutcome({
    required this.deleted,
    this.filesDeleted = false,
    this.filesSkippedReason,
  });

  final bool deleted;
  final bool filesDeleted;
  final String? filesSkippedReason;
}
