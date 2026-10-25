import 'dart:convert';

import '../../../models/game_instance.dart';
import '../core_transport.dart';
import 'installed_mod.dart';

/// Instance domain binding for the native core: the `instance.*` registry
/// methods (`list/sync/create/update/delete/duplicate`) and the `instance.mods*`
/// file helpers.
mixin CoreClientInstances on CoreTransport {
  /// List persisted instances through the C# repository.
  Future<List<GameInstance>> listInstances({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.list';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return List<GameInstance>.unmodifiable(
      asList(data['items'])
          .map(GameInstance.fromNative)
          .whereType<GameInstance>(),
    );
  }

  /// Reconcile configured roots with the C# instance index.
  Future<InstanceSyncResult> syncInstances({
    List<String> roots = const <String>[],
    bool dryRun = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.sync';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        if (roots.isNotEmpty) 'roots': roots,
        'dryRun': dryRun,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final items = asList(data['items'])
        .map(GameInstance.fromNative)
        .whereType<GameInstance>()
        .toList(growable: false);
    final rootsStatus = <InstanceRootStatus>[];
    for (final value in asList(data['roots'])) {
      if (value is! Map) continue;
      rootsStatus.add(
        InstanceRootStatus(
          path: value['path']?.toString() ?? '',
          exists: value['exists'] == true,
          instanceCount: asInt(value['instanceCount']) ?? 0,
        ),
      );
    }
    return InstanceSyncResult(
      items: List<GameInstance>.unmodifiable(items),
      adopted: asList(data['adopted'])
          .map((value) => value.toString())
          .toList(),
      updated: asList(data['updated'])
          .map((value) => value.toString())
          .toList(),
      missing: asList(data['missing'])
          .whereType<Map<Object?, Object?>>()
          .map((value) => value['id']?.toString() ?? '')
          .where((value) => value.isNotEmpty)
          .toList(),
      roots: List<InstanceRootStatus>.unmodifiable(rootsStatus),
    );
  }

  /// Create an instance and its per-instance metadata file.
  Future<GameInstance> createInstance({
    required String name,
    required String gameVersion,
    required String loader,
    required String loaderVersion,
    String? rootPath,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.create';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'name': name,
        'gameVersion': gameVersion,
        'loader': loader,
        'loaderVersion': loaderVersion,
        'rootPath': ?rootPath,
      }),
      onProgress: onProgress,
    );
    return _parseInstance(this, jsonText, methodName);
  }

  /// Update the editable instance fields.
  Future<GameInstance> updateInstance({
    required String id,
    String? name,
    bool? favorite,
    DateTime? lastPlayedAt,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.update';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'id': id,
        'name': ?name,
        'favorite': ?favorite,
        'lastPlayedAt': ?lastPlayedAt?.toUtc().toIso8601String(),
      }),
      onProgress: onProgress,
    );
    return _parseInstance(this, jsonText, methodName);
  }

  /// Copy an instance (files + metadata) under a new name.
  Future<GameInstance> duplicateInstance({
    required String id,
    required String name,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'instance.duplicate';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'id': id, 'name': name}),
      onProgress: onProgress,
      label: label,
    );
    return _parseInstance(this, jsonText, methodName);
  }

  /// Set, change or clear the per-instance launch overrides.
  ///
  /// Every key is always sent: `null` clears the override (the instance then
  /// follows the global setting) while a value sets it. The C# owner treats
  /// this as a merge patch, so omitted keys would stay untouched.
  ///
  /// [wrapperCommand] and the lifecycle commands keep an empty string as
  /// "explicitly none", and [environment] keeps an empty map as "explicitly
  /// clear the global map". [name] renames the launcher metadata; the instance
  /// directory is not moved and an omitted name leaves it unchanged.
  Future<GameInstance> updateInstanceOverrides({
    required String instanceId,
    String? name,
    int? memoryMinMb,
    int? memoryMaxMb,
    String? jvmArgs,
    String? gcMode,
    String? gameArgs,
    String? wrapperCommand,
    String? preLoadCommand,
    String? preLaunchCommand,
    String? postExitCommand,
    Map<String, String>? environment,
    bool? joinServerOnLaunch,
    String? joinServerAddress,
    String? joinWorldName,
    String? javaPath,
    int? windowWidth,
    int? windowHeight,
    bool? fullscreen,
    bool? maximized,
    bool? enableGameMode,
    bool? enableMangoHud,
    bool? useZink,
  }) async {
    const methodName = 'instance.update';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'id': instanceId,
        'name': ?name,
        'overrides': <String, Object?>{
          'memoryMinMb': memoryMinMb,
          'memoryMaxMb': memoryMaxMb,
          'jvmArgs': jvmArgs,
          'gcMode': gcMode,
          'gameArgs': gameArgs,
          'wrapperCommand': wrapperCommand,
          'preLoadCommand': preLoadCommand,
          'preLaunchCommand': preLaunchCommand,
          'postExitCommand': postExitCommand,
          'environment': environment,
          'joinServerOnLaunch': joinServerOnLaunch,
          'joinServerAddress': joinServerAddress,
          'joinWorldName': joinWorldName,
          'javaPath': javaPath,
          'windowWidth': windowWidth,
          'windowHeight': windowHeight,
          'fullscreen': fullscreen,
          'maximized': maximized,
          'enableGameMode': enableGameMode,
          'enableMangoHud': enableMangoHud,
          'useZink': useZink,
        },
      }),
    );
    return _parseInstance(this, jsonText, methodName);
  }

  /// Remove the registry entry. Pass [deleteFiles] to also remove the
  /// instance's own game directory; the C# owner refuses when the directory is
  /// shared with a configured root or another instance.
  Future<InstanceDeleteOutcome> deleteInstance(
    String id, {
    bool deleteFiles = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.delete';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'id': id,
        'deleteFiles': deleteFiles,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return InstanceDeleteOutcome(
      deleted: data['deleted'] == true,
      filesDeleted: data['filesDeleted'] == true,
      filesSkippedReason: data['filesSkippedReason']?.toString(),
    );
  }

  /// List mod files in an instance's `mods` directory.
  Future<List<InstalledMod>> listInstanceMods(
    String instanceId, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.mods.list';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'instanceId': instanceId}),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return List<InstalledMod>.unmodifiable(
      asList(data['items'])
          .map(InstalledMod.tryParse)
          .whereType<InstalledMod>(),
    );
  }

  /// Enable or disable one mod by renaming `.jar` ↔ `.jar.disabled`.
  Future<InstalledMod?> setInstanceModEnabled({
    required String instanceId,
    required String fileName,
    required bool enabled,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.mods.setEnabled';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'fileName': fileName,
        'enabled': enabled,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return InstalledMod.tryParse(data['item']);
  }

  /// Delete one mod file from an instance.
  Future<bool> removeInstanceMod({
    required String instanceId,
    required String fileName,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.mods.remove';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'fileName': fileName,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return data['removed'] == true;
  }
}

GameInstance _parseInstance(
  CoreTransport transport,
  String jsonText,
  String methodName,
) {
  final decoded = transport.decodeEnvelope(
    jsonText,
    fallbackMethod: methodName,
  );
  final data = transport.asMap(decoded['data'], methodName);
  final instance = GameInstance.fromNative(data['instance'] ?? data);
  if (instance == null) {
    throw CoreTransport.invalidEnvelope(methodName, 'no instance object');
  }
  return instance;
}
