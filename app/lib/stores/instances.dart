import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/game_instance.dart';
import '../utils/app_paths.dart';
import 'providers.dart';
import 'settings.dart';

/// Persisted instance list. The C# repository owns `instance.json` and the
/// registry; the Dart provider only mirrors it for the UI.
final instancesProvider =
    AsyncNotifierProvider<InstancesNotifier, List<GameInstance>>(
      InstancesNotifier.new,
    );

class InstancesNotifier extends AsyncNotifier<List<GameInstance>> {
  @override
  Future<List<GameInstance>> build() async {
    final client = ref.read(coreClientProvider);
    if (client == null) return _seedInstances();

    try {
      return await client.listInstances();
    } catch (_) {
      // Keep the shell usable if the native module is temporarily missing.
      return _seedInstances();
    }
  }

  Future<InstanceSyncResult?> sync() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      await reload();
      return null;
    }

    final result = await client.syncInstances();
    state = AsyncData(result.items);
    final selected = ref.read(selectedInstanceIdProvider);
    if (selected != null && !result.items.any((item) => item.id == selected)) {
      ref
          .read(selectedInstanceIdProvider.notifier)
          .select(result.items.isEmpty ? null : result.items.first.id);
    }
    return result;
  }

  Future<void> reload() async {
    state = AsyncData(await build());
  }

  Future<GameInstance> create({
    required String name,
    required String gameVersion,
    required GameLoader loader,
    String? loaderVersion,
    String rootPath = '',
  }) async {
    if (name.trim().isEmpty) {
      throw ArgumentError.value(name, 'name', 'instance name is required');
    }

    final client = ref.read(coreClientProvider);
    final current = state.value ?? const <GameInstance>[];

    GameInstance created;
    if (client != null) {
      created = await client.createInstance(
        name: name,
        gameVersion: gameVersion,
        loader: loader.name,
        loaderVersion: loaderVersion ?? loader.defaultVersion,
        rootPath: rootPath.isEmpty ? null : rootPath,
      );
    } else {
      final settings = ref.read(appSettingsProvider).value;
      final root = rootPath.isEmpty
          ? joinPath(
              settings?.roots.first ?? fallbackInstancesRoot(),
              slugForInstance(name),
            )
          : rootPath;
      created = GameInstance(
        id: 'instance-${DateTime.now().microsecondsSinceEpoch}',
        name: name.trim(),
        gameVersion: gameVersion,
        loader: loader,
        loaderVersion: loaderVersion ?? loader.defaultVersion,
        rootPath: root,
      );
    }

    state = AsyncData([created, ...current]);
    ref.read(selectedInstanceIdProvider.notifier).select(created.id);
    return created;
  }

  /// Download and commit the vanilla game files for [instance].
  Future<void> installVanilla(
    GameInstance instance, {
    void Function(int done, int total)? onProgress,
  }) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      throw StateError('The native core is not connected.');
    }

    final plan = await client.planVanillaInstall(
      root: instance.rootPath,
      versionId: instance.gameVersion,
      label: instance.gameVersion,
    );
    final result = await client.executeDownload(
      plan.plan,
      onProgress: onProgress,
      label: '${instance.name} · ${instance.gameVersion}',
    );
    if (!result.ok) {
      final detail = result.stderr?.trim();
      throw StateError(
        detail == null || detail.isEmpty
            ? 'Game download failed (${result.status}).'
            : detail,
      );
    }

    await client.commitVanillaVersion(
      root: instance.rootPath,
      versionId: plan.versionId,
      versionJson: plan.versionJson,
      label: instance.gameVersion,
    );
    await reload();
  }

  Future<void> delete(String id) async {
    final client = ref.read(coreClientProvider);
    if (client != null) {
      try {
        await client.deleteInstance(id);
      } catch (_) {
        // Keep the local mirror consistent even if the registry is busy.
      }
    }

    final remaining = (state.value ?? const <GameInstance>[])
        .where((instance) => instance.id != id)
        .toList(growable: false);
    state = AsyncData(remaining);
    if (ref.read(selectedInstanceIdProvider) == id) {
      ref
          .read(selectedInstanceIdProvider.notifier)
          .select(remaining.isEmpty ? null : remaining.first.id);
    }
  }

  Future<void> markPlayed(String id) async {
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    try {
      final updated = await client.updateInstance(
        id: id,
        lastPlayedAt: DateTime.now(),
      );
      final current = state.value ?? const <GameInstance>[];
      state = AsyncData([
        for (final instance in current)
          if (instance.id == id) updated else instance,
      ]);
    } catch (_) {
      // Best effort; a failed last-played write must not block launching.
    }
  }
}

/// Currently selected instance. Kept separate from the list so screens can
/// observe selection without rebuilding on every list mutation.
final selectedInstanceIdProvider =
    NotifierProvider<SelectedInstanceIdNotifier, String?>(
      SelectedInstanceIdNotifier.new,
    );

class SelectedInstanceIdNotifier extends Notifier<String?> {
  @override
  String? build() => null;

  void select(String? id) => state = id;
}

final selectedInstanceProvider = Provider<GameInstance?>((ref) {
  final instances =
      ref.watch(instancesProvider).value ?? const <GameInstance>[];
  final selectedId = ref.watch(selectedInstanceIdProvider);

  for (final instance in instances) {
    if (instance.id == selectedId) return instance;
  }

  return instances.isEmpty ? null : instances.first;
});

List<GameInstance> _seedInstances() {
  final now = DateTime.now();
  return [
    GameInstance(
      id: 'fabric-1.21.4',
      name: '1.21.4 · Fabric',
      gameVersion: '1.21.4',
      loader: GameLoader.fabric,
      loaderVersion: GameLoader.fabric.defaultVersion,
      modCount: 12,
      lastPlayed: now,
      favorite: true,
    ),
    GameInstance(
      id: 'forge-1.20.1',
      name: '1.20.1 · Forge',
      gameVersion: '1.20.1',
      loader: GameLoader.forge,
      loaderVersion: GameLoader.forge.defaultVersion,
      modCount: 8,
      lastPlayed: now.subtract(const Duration(days: 1)),
    ),
    GameInstance(
      id: 'vanilla-1.21.1',
      name: '1.21.1 · Vanilla',
      gameVersion: '1.21.1',
      loader: GameLoader.vanilla,
      lastPlayed: now.subtract(const Duration(days: 1)),
    ),
  ];
}
