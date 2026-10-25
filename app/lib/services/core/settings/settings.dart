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
  ///
  /// [wrapperCommand], [environment] and the three lifecycle commands use the
  /// merge-patch style also used by [setDownloadSettings]: leave them at
  /// [noChange] to keep the stored value, pass null to clear it, or pass a
  /// value to replace it. An empty wrapper or command string also means "none";
  /// an empty environment map clears it.
  ///
  /// The native-library paths follow the same tri-state rule and only take
  /// effect while [developerMode] is on. [maximized] and [fullscreen] are
  /// mutually exclusive; the caller writes both so the native owner can
  /// normalize the pair.
  Future<AppSettings> setRuntimeOptions({
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
        if (wrapperCommand != noChange)
          'wrapperCommand': wrapperCommand as String?,
        if (preLoadCommand != noChange)
          'preLoadCommand': preLoadCommand as String?,
        if (preLaunchCommand != noChange)
          'preLaunchCommand': preLaunchCommand as String?,
        if (postExitCommand != noChange)
          'postExitCommand': postExitCommand as String?,
        if (environment != noChange)
          'environment': _environmentPayload(environment),
        'windowWidth': ?windowWidth,
        'windowHeight': ?windowHeight,
        'fullscreen': ?fullscreen,
        'maximized': ?maximized,
        'enableGameMode': ?enableGameMode,
        'enableMangoHud': ?enableMangoHud,
        'useZink': ?useZink,
        'developerMode': ?developerMode,
        if (nativeGlfwPath != noChange)
          'nativeGlfwPath': nativeGlfwPath as String?,
        if (nativeOpenALPath != noChange)
          'nativeOpenALPath': nativeOpenALPath as String?,
        if (nativeSdlPath != noChange)
          'nativeSdlPath': nativeSdlPath as String?,
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

/// Accepts the typed map callers pass in production as well as an untyped or
/// empty literal from a caller, copying it into the wire shape.
Map<String, String>? _environmentPayload(Object? value) {
  if (value == null) return null;
  if (value is Map<String, String>) return value;
  return Map<String, String>.from(value as Map);
}

AppSettings _parseSettings(
  CoreTransport transport,
  String jsonText,
  String methodName,
) {
  final decoded = transport.decodeEnvelope(
    jsonText,
    fallbackMethod: methodName,
  );
  final data = transport.asMap(decoded['data'], methodName);
  final settings = AppSettings.tryParse(data['settings']);
  if (settings == null) {
    throw CoreTransport.invalidEnvelope(methodName, 'no settings object');
  }
  return settings;
}
