import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/settings/app_settings.dart';
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
    Object? wrapperCommand = noChange,
    Object? preLoadCommand = noChange,
    Object? preLaunchCommand = noChange,
    Object? postExitCommand = noChange,
    Object? environment = noChange,
    int? windowWidth,
    int? windowHeight,
    bool? fullscreen,
    bool? maximized,
    bool? enableGameMode,
    bool? enableMangoHud,
    bool? useZink,
    bool? developerMode,
    Object? nativeGlfwPath = noChange,
    Object? nativeOpenALPath = noChange,
    Object? nativeSdlPath = noChange,
    int? logRetentionFiles,
    int? logRetentionMegabytes,
    bool? shareLoaderFiles,
    String? memoryMode,
    bool? gameOptimization,
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
          wrapperCommand: wrapperCommand == noChange
              ? current.wrapperCommand
              : wrapperCommand as String?,
          preLoadCommand: preLoadCommand == noChange
              ? current.preLoadCommand
              : preLoadCommand as String?,
          preLaunchCommand: preLaunchCommand == noChange
              ? current.preLaunchCommand
              : preLaunchCommand as String?,
          postExitCommand: postExitCommand == noChange
              ? current.postExitCommand
              : postExitCommand as String?,
          environment: environment == noChange
              ? current.environment
              : environment == null
              ? const <String, String>{}
              : Map<String, String>.from(environment as Map),
          windowWidth: windowWidth ?? current.windowWidth,
          windowHeight: windowHeight ?? current.windowHeight,
          fullscreen: fullscreen ?? current.fullscreen,
          maximized: maximized ?? current.maximized,
          enableGameMode: enableGameMode ?? current.enableGameMode,
          enableMangoHud: enableMangoHud ?? current.enableMangoHud,
          useZink: useZink ?? current.useZink,
          developerMode: developerMode ?? current.developerMode,
          nativeGlfwPath: nativeGlfwPath == noChange
              ? current.nativeGlfwPath
              : nativeGlfwPath as String?,
          nativeOpenALPath: nativeOpenALPath == noChange
              ? current.nativeOpenALPath
              : nativeOpenALPath as String?,
          nativeSdlPath: nativeSdlPath == noChange
              ? current.nativeSdlPath
              : nativeSdlPath as String?,
          logRetentionFiles: logRetentionFiles ?? current.logRetentionFiles,
          logRetentionMegabytes:
              logRetentionMegabytes ?? current.logRetentionMegabytes,
          shareLoaderFiles: shareLoaderFiles ?? current.shareLoaderFiles,
          memoryMode: memoryMode ?? current.memoryMode,
          gameOptimization: gameOptimization ?? current.gameOptimization,
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
        wrapperCommand: wrapperCommand,
        preLoadCommand: preLoadCommand,
        preLaunchCommand: preLaunchCommand,
        postExitCommand: postExitCommand,
        environment: environment,
        windowWidth: windowWidth,
        windowHeight: windowHeight,
        fullscreen: fullscreen,
        maximized: maximized,
        enableGameMode: enableGameMode,
        enableMangoHud: enableMangoHud,
        useZink: useZink,
        developerMode: developerMode,
        nativeGlfwPath: nativeGlfwPath,
        nativeOpenALPath: nativeOpenALPath,
        nativeSdlPath: nativeSdlPath,
        logRetentionFiles: logRetentionFiles,
        logRetentionMegabytes: logRetentionMegabytes,
        shareLoaderFiles: shareLoaderFiles,
        memoryMode: memoryMode,
        gameOptimization: gameOptimization,
      ),
    );
  }

  Future<void> setDownloadPreferences({
    String? downloadSource,
    int? downloadConcurrency,
    int? downloadPerHost,
    Object? downloadProxy = noChange,
    int? downloadChunkSizeKb,
    int? downloadRangeThresholdKb,
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
          downloadChunkSizeKb:
              downloadChunkSizeKb ?? current.downloadChunkSizeKb,
          downloadRangeThresholdKb:
              downloadRangeThresholdKb ?? current.downloadRangeThresholdKb,
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
        downloadChunkSizeKb: downloadChunkSizeKb,
        downloadRangeThresholdKb: downloadRangeThresholdKb,
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
