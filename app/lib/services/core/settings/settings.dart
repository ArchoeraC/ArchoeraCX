import 'dart:convert';

import 'app_settings.dart';
import '../core_transport.dart';

/// Settings domain binding for the native core: `settings.get` and the
/// `settings.set` family. Every setter returns the persisted document so the
/// caller can reconcile its state with what the C# owner accepted.
mixin CoreClientSettings on CoreTransport {
  /// Read the C#-owned settings document.
  Future<AppSettings> getSettings({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'settings.get';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    return _parseSettings(this, jsonText, methodName);
  }

  /// Update all configured instance roots and persist them atomically.
  Future<AppSettings> setInstanceRoots(
    List<String> instancesRoots, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'settings.set';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instancesRoots': instancesRoots,
      }),
      onProgress: onProgress,
    );
    return _parseSettings(this, jsonText, methodName);
  }

  /// Backward-compatible single-root update.
  Future<AppSettings> setInstancesRoot(
    String instancesRoot, {
    void Function(int done, int total)? onProgress,
  }) => setInstanceRoots([instancesRoot], onProgress: onProgress);

  /// Persist the download preferences (source priority, stream counts).
  Future<AppSettings> setDownloadSettings({
    String? downloadSource,
    int? downloadConcurrency,
    int? downloadPerHost,
    Object? downloadProxy = noChange,
    int? downloadChunkSizeKb,
    int? downloadRangeThresholdKb,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'settings.set';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'downloadSource': ?downloadSource,
        'downloadConcurrency': ?downloadConcurrency,
        'downloadPerHost': ?downloadPerHost,
        if (downloadProxy != noChange)
          'downloadProxy': downloadProxy as String?,
        'downloadChunkSizeKb': ?downloadChunkSizeKb,
        'downloadRangeThresholdKb': ?downloadRangeThresholdKb,
      }),
      onProgress: onProgress,
    );
    return _parseSettings(this, jsonText, methodName);
  }

  /// Persist the manual Java selection or switch back to automatic.
  Future<AppSettings> setJavaSelection({
    String? javaPath,
    bool? javaAuto,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'settings.set';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'javaPath': javaPath,
        'javaAuto': ?javaAuto,
      }),
      onProgress: onProgress,
    );
    return _parseSettings(this, jsonText, methodName);
  }

  /// Persist memory and JVM options.
  Future<AppSettings> setRuntimeOptions({
    int? memoryMinMb,
    int? memoryMaxMb,
    String? jvmArgs,
    String? gcMode,
    String? gameArgs,
    int? windowWidth,
    int? windowHeight,
    bool? fullscreen,
    int? logRetentionFiles,
    int? logRetentionMegabytes,
    bool? shareLoaderFiles,
    String? memoryMode,
    bool? gameOptimization,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'settings.set';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'memoryMinMb': ?memoryMinMb,
        'memoryMaxMb': ?memoryMaxMb,
        'jvmArgs': ?jvmArgs,
        'gcMode': ?gcMode,
        'gameArgs': ?gameArgs,
        'windowWidth': ?windowWidth,
        'windowHeight': ?windowHeight,
        'fullscreen': ?fullscreen,
        'logRetentionFiles': ?logRetentionFiles,
        'logRetentionMb': ?logRetentionMegabytes,
        'shareLoaderFiles': ?shareLoaderFiles,
        'memoryMode': ?memoryMode,
        'gameOptimization': ?gameOptimization,
      }),
      onProgress: onProgress,
    );
    return _parseSettings(this, jsonText, methodName);
  }
}

AppSettings _parseSettings(
  CoreTransport transport,
  String jsonText,
  String methodName,
) {
  final decoded = transport.decodeEnvelope(jsonText, fallbackMethod: methodName);
  final data = transport.asMap(decoded['data'], methodName);
  final settings = AppSettings.tryParse(data['settings']);
  if (settings == null) {
    throw CoreTransport.invalidEnvelope(methodName, 'no settings object');
  }
  return settings;
}
