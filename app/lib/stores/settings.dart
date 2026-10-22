import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/app_settings.dart';
import '../services/core/core_client.dart';
import '../utils/app_paths.dart';
import 'providers.dart';

/// C#-owned settings, with a development fallback when the native module is
/// unavailable. AsyncNotifier keeps the settings page and create dialog in
/// sync after a directory is picked.
final appSettingsProvider =
    AsyncNotifierProvider<AppSettingsNotifier, AppSettings>(
      AppSettingsNotifier.new,
    );

class AppSettingsNotifier extends AsyncNotifier<AppSettings> {
  @override
  Future<AppSettings> build() async {
    final client = ref.read(coreClientProvider);
    if (client != null) {
      try {
        return await client.getSettings();
      } catch (_) {
        // Fall through to the portable fallback; the settings page still works.
      }
    }

    return _fallback;
  }

  Future<void> setInstancesRoots(List<String> roots) async {
    final normalized = _normalizeRoots(roots);
    if (normalized.isEmpty) {
      throw ArgumentError('at least one instances root is required');
    }

    final client = ref.read(coreClientProvider);
    if (client == null) {
      state = AsyncData(
        AppSettings(
          instancesRoot: normalized.first,
          instancesRoots: normalized,
          dataRoot: _fallback.dataRoot,
          settingsPath: '',
        ),
      );
      return;
    }

    final updated = await client.setInstanceRoots(normalized);
    state = AsyncData(updated);
  }

  Future<void> setInstancesRoot(String path) => setInstancesRoots([path]);

  Future<void> setJavaSelection({String? javaPath, bool? javaAuto}) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      final current = state.value ?? _fallback;
      state = AsyncData(
        AppSettings(
          instancesRoot: current.instancesRoot,
          instancesRoots: current.instancesRoots,
          dataRoot: current.dataRoot,
          settingsPath: current.settingsPath,
          javaPath: javaPath,
          javaAuto: javaAuto ?? current.javaAuto,
        ),
      );
      return;
    }

    state = AsyncData(
      await client.setJavaSelection(javaPath: javaPath, javaAuto: javaAuto),
    );
  }

  Future<void> addInstanceRoot(String path) async {
    final current = state.value ?? _fallback;
    await setInstancesRoots([...current.roots, path]);
  }

  Future<void> removeInstanceRoot(String path) async {
    final current = state.value ?? _fallback;
    final roots = current.roots.where((root) => root != path).toList();
    if (roots.isEmpty) return;
    await setInstancesRoots(roots);
  }

  Future<void> setRuntimeOptions({
    int? memoryMinMb,
    int? memoryMaxMb,
    String? jvmArgs,
    String? gcMode,
    String? gameArgs,
  }) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      final current = state.value ?? _fallback;
      state = AsyncData(
        AppSettings(
          instancesRoot: current.instancesRoot,
          instancesRoots: current.instancesRoots,
          dataRoot: current.dataRoot,
          settingsPath: current.settingsPath,
          javaPath: current.javaPath,
          javaAuto: current.javaAuto,
          memoryMinMb: memoryMinMb ?? current.memoryMinMb,
          memoryMaxMb: memoryMaxMb ?? current.memoryMaxMb,
          jvmArgs: jvmArgs ?? current.jvmArgs,
          gcMode: gcMode ?? current.gcMode,
          gameArgs: gameArgs ?? current.gameArgs,
        ),
      );
      return;
    }

    state = AsyncData(
      await client.setRuntimeOptions(
        memoryMinMb: memoryMinMb,
        memoryMaxMb: memoryMaxMb,
        jvmArgs: jvmArgs,
        gcMode: gcMode,
        gameArgs: gameArgs,
      ),
    );
  }

  Future<void> setDownloadPreferences({
    String? downloadSource,
    int? downloadConcurrency,
    int? downloadPerHost,
    Object? downloadProxy = noChange,
  }) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      final current = state.value ?? _fallback;
      state = AsyncData(
        AppSettings(
          instancesRoot: current.instancesRoot,
          instancesRoots: current.instancesRoots,
          dataRoot: current.dataRoot,
          settingsPath: current.settingsPath,
          downloadSource: downloadSource ?? current.downloadSource,
          downloadConcurrency:
              downloadConcurrency ?? current.downloadConcurrency,
          downloadPerHost: downloadPerHost ?? current.downloadPerHost,
          downloadProxy: downloadProxy is String?
              ? downloadProxy
              : current.downloadProxy,
        ),
      );
      return;
    }

    state = AsyncData(
      await client.setDownloadSettings(
        downloadSource: downloadSource,
        downloadConcurrency: downloadConcurrency,
        downloadPerHost: downloadPerHost,
        downloadProxy: downloadProxy,
      ),
    );
  }

  Future<void> setDefaultInstanceRoot(String path) async {
    final current = state.value ?? _fallback;
    if (!current.roots.contains(path)) return;
    await setInstancesRoots([
      path,
      ...current.roots.where((root) => root != path),
    ]);
  }

  static List<String> _normalizeRoots(Iterable<String> roots) {
    final seen = <String>{};
    final result = <String>[];
    for (final raw in roots) {
      final root = raw.trim();
      if (root.isEmpty || !seen.add(root)) continue;
      result.add(root);
    }
    return result;
  }

  AppSettings get _fallback {
    final dataRoot = defaultDataRoot();
    final root = fallbackInstancesRoot();
    return AppSettings(
      instancesRoot: root,
      instancesRoots: <String>[root],
      dataRoot: dataRoot,
      settingsPath: '',
    );
  }
}
