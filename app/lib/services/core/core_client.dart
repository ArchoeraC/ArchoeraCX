import 'dart:async';
import 'dart:convert';
import 'dart:ffi' as ffi;
import 'dart:io';

import 'package:ffi/ffi.dart';

import '../../models/account.dart';
import '../../models/game_instance.dart';
import 'app_settings.dart';
import 'build_policy.dart';
import 'core_event.dart';
import 'event_pump.dart';
import 'installed_mod.dart';
import 'java_scan.dart';
import 'launch_preparation.dart';
import 'launch_result.dart';
import 'loader_versions.dart';
import 'loader_plan.dart';
import 'version_models.dart';
import 'resource_models.dart';
import 'system_memory.dart';
import 'task_update.dart';
import 'vanilla_install_plan.dart';

/// The error structure returned by the native core ABI.
///
/// Mirrors `archoera_error` in `contracts/abi/archoera_abi.h`.
final class _ArchoeraErrorNative extends ffi.Struct {
  @ffi.Int32()
  external int code;

  external ffi.Pointer<Utf8> message;

  external ffi.Pointer<Utf8> detail;
}

typedef _AbiVersionNative = ffi.Uint32 Function();
typedef _AbiVersionDart = int Function();

typedef _BuildInfoNative = ffi.Pointer<Utf8> Function();
typedef _BuildInfoDart = ffi.Pointer<Utf8> Function();

typedef _InitNative = ffi.Int32 Function(
  ffi.Pointer<Utf8>,
  ffi.Pointer<ffi.Pointer<ffi.Void>>,
  ffi.Pointer<_ArchoeraErrorNative>,
);
typedef _InitDart = int Function(
  ffi.Pointer<Utf8>,
  ffi.Pointer<ffi.Pointer<ffi.Void>>,
  ffi.Pointer<_ArchoeraErrorNative>,
);

typedef _ShutdownNative = ffi.Void Function(ffi.Pointer<ffi.Void>);
typedef _ShutdownDart = void Function(ffi.Pointer<ffi.Void>);

typedef _CallNative = ffi.Int32 Function(
  ffi.Pointer<ffi.Void>,
  ffi.Pointer<Utf8>,
  ffi.Pointer<Utf8>,
  ffi.Pointer<ffi.Uint64>,
  ffi.Pointer<_ArchoeraErrorNative>,
);
typedef _CallDart = int Function(
  ffi.Pointer<ffi.Void>,
  ffi.Pointer<Utf8>,
  ffi.Pointer<Utf8>,
  ffi.Pointer<ffi.Uint64>,
  ffi.Pointer<_ArchoeraErrorNative>,
);

typedef _ResultNative = ffi.Int32 Function(
  ffi.Pointer<ffi.Void>,
  ffi.Uint64,
  ffi.Pointer<Utf8>,
  ffi.IntPtr,
  ffi.Pointer<ffi.IntPtr>,
  ffi.Pointer<_ArchoeraErrorNative>,
);
typedef _ResultDart = int Function(
  ffi.Pointer<ffi.Void>,
  int,
  ffi.Pointer<Utf8>,
  int,
  ffi.Pointer<ffi.IntPtr>,
  ffi.Pointer<_ArchoeraErrorNative>,
);

typedef _ReleaseNative = ffi.Void Function(ffi.Pointer<ffi.Void>, ffi.Uint64);
typedef _ReleaseDart = void Function(ffi.Pointer<ffi.Void>, int);

typedef _CancelNative = ffi.Int32 Function(ffi.Pointer<ffi.Void>, ffi.Uint64);
typedef _CancelDart = int Function(ffi.Pointer<ffi.Void>, int);

/// A structured error reported by the native core.
final class CoreException implements Exception {
  const CoreException({
    required this.status,
    required this.code,
    required this.message,
    this.detail,
    this.taskId,
  });

  /// Raw `archoera_status` value (0 for errors carried inside a JSON envelope).
  final int status;

  /// Symbolic status/domain code, for example `E_INVALID` or `RATE_LIMITED`.
  final String code;

  final String message;
  final String? detail;
  final int? taskId;

  @override
  String toString() {
    final suffix = detail == null || detail!.isEmpty ? '' : ' — $detail';
    return 'CoreException($code, status=$status): $message$suffix';
  }
}

/// Raised when `archoera_core` cannot be located, loaded, or initialized.
final class CoreUnavailableException implements Exception {
  CoreUnavailableException(
    this.message, {
    List<String> searchedPaths = const [],
  }) : searchedPaths = List.unmodifiable(searchedPaths);

  final String message;
  final List<String> searchedPaths;

  /// Human-readable message including every path that was probed.
  String get summary => searchedPaths.isEmpty
      ? message
      : '$message\n\nSearched:\n${searchedPaths.join('\n')}';

  @override
  String toString() => 'CoreUnavailableException: $summary';
}

/// The result of a successful `system.pingAsync` call.
final class PingResult {
  const PingResult({
    required this.pong,
    required this.method,
    required this.rawJson,
    this.data = const <String, Object?>{},
    this.schemaVersion,
    this.requestId,
  });

  /// Value of `data.pong` (or top-level `pong` when the envelope is flat).
  final Object? pong;

  final String method;

  /// The complete decoded result envelope, kept for diagnostics.
  final Map<String, Object?> rawJson;

  final Map<String, Object?> data;
  final int? schemaVersion;
  final String? requestId;

  /// A short, locale-independent representation of [pong].
  String get summary {
    final value = pong;
    if (value == null) return method;
    if (value is String) return value;
    if (value is num || value is bool) return value.toString();
    return jsonEncode(value);
  }
}

/// Event-driven Dart binding for the minimal `archoera_core` ABI.
///
/// Lifecycle, call/result and event-queue functions are bound here. The UI
/// calls [pingAsync] and receives its result from a task-state event; there is
/// no timer or polling loop. Native wakeups are turned into microtasks by
/// [EventPump], which drains `archoera_poll` in batches.
final class CoreClient {
  CoreClient._(this._library) {
    _abiVersionFn = _library.lookupFunction<_AbiVersionNative, _AbiVersionDart>(
      'archoera_abi_version',
    );
    _buildInfoFn = _library.lookupFunction<_BuildInfoNative, _BuildInfoDart>(
      'archoera_build_info',
    );
    _initFn = _library.lookupFunction<_InitNative, _InitDart>('archoera_init');
    _shutdownFn = _library.lookupFunction<_ShutdownNative, _ShutdownDart>(
      'archoera_shutdown',
    );
    _callFn = _library.lookupFunction<_CallNative, _CallDart>('archoera_call');
    _resultFn = _library.lookupFunction<_ResultNative, _ResultDart>(
      'archoera_result',
    );
    _releaseFn = _library.lookupFunction<_ReleaseNative, _ReleaseDart>(
      'archoera_release',
    );
    _pollFn = _library.lookupFunction<CorePollNative, CorePoll>(
      'archoera_poll',
    );
    _setWakeupFn = _library.lookupFunction<CoreSetWakeupNative, CoreSetWakeup>(
      'archoera_set_wakeup',
    );
    _cancelFn = _library.lookupFunction<_CancelNative, _CancelDart>(
      'archoera_cancel',
    );
  }

  static const int expectedAbiVersion = 1;

  final _taskUpdates = StreamController<TaskUpdate>.broadcast();

  /// Lifecycle/progress snapshots for the task center. Synthetic start/end
  /// updates come from [_runAsync]; module progress comes from native events.
  Stream<TaskUpdate> get taskUpdates => _taskUpdates.stream;

  static const int _ok = 0;
  static const int _eBuffer = 9;

  static const Map<int, String> _statusNames = <int, String>{
    0: 'OK',
    1: 'E_INVALID',
    2: 'E_NOT_FOUND',
    3: 'E_AUTH',
    4: 'E_NETWORK',
    5: 'E_DISK',
    6: 'E_CANCELLED',
    7: 'E_UNSUPPORTED',
    8: 'E_INTERNAL',
    9: 'E_BUFFER',
    10: 'E_BUSY',
    11: 'E_POLICY',
  };

  final ffi.DynamicLibrary _library;

  late final _AbiVersionDart _abiVersionFn;
  late final _BuildInfoDart _buildInfoFn;
  late final _InitDart _initFn;
  late final _ShutdownDart _shutdownFn;
  late final _CallDart _callFn;
  late final _ResultDart _resultFn;
  late final _ReleaseDart _releaseFn;
  late final CorePoll _pollFn;
  late final CoreSetWakeup _setWakeupFn;
  late final _CancelDart _cancelFn;

  ffi.Pointer<ffi.Void>? _core;
  EventPump? _pump;
  int _abiVersion = 0;
  String _buildInfo = '';
  bool _closed = false;

  /// Load `archoera_core`, verify the ABI, and initialize it with an empty
  /// configuration.
  ///
  /// Resolution order:
  /// 1. `ARCHOERACX_CORE_LIB`.
  /// 2. `build/native/<library>` relative to the current directory.
  /// 3. `../app/build/native/<library>` (when launched from the repo root).
  /// 4. The Linux release bundle path; platform bundle paths are also probed.
  static CoreClient openDefault() {
    final searchedPaths = <String>[];
    String? lastLoadError;

    for (final candidate in _defaultLibraryCandidates()) {
      final absolutePath = _tryAbsolutePath(candidate);
      if (absolutePath == null) continue;
      final file = File(absolutePath);
      searchedPaths.add(absolutePath);

      if (!file.existsSync()) continue;

      ffi.DynamicLibrary library;
      try {
        library = ffi.DynamicLibrary.open(absolutePath);
      } catch (error) {
        lastLoadError = '$absolutePath: $error';
        continue;
      }

      try {
        final client = CoreClient._(library);
        client._initialize();
        return client;
      } on CoreException {
        rethrow;
      } on CoreUnavailableException {
        rethrow;
      } catch (error) {
        throw CoreUnavailableException(
          'Failed to use archoera_core from $absolutePath: $error',
          searchedPaths: searchedPaths,
        );
      }
    }

    if (lastLoadError != null) {
      throw CoreUnavailableException(
        'Failed to load libarchoera_core ($lastLoadError).',
        searchedPaths: searchedPaths,
      );
    }

    throw CoreUnavailableException(
      'libarchoera_core not found (searched ${searchedPaths.length} locations).',
      searchedPaths: searchedPaths,
    );
  }

  int get abiVersion => _abiVersion;

  /// Static build information supplied by the core, if available.
  String get buildInfo => _buildInfo;

  bool get isOpen => !_closed && _core != null;

  /// Events drained from the native core, in sequence order.
  Stream<CoreEvent> get events =>
      _pump?.events ?? const Stream<CoreEvent>.empty();

  /// Submit `system.pingAsync` and wait for its terminal task-state event.
  ///
  /// The future is completed by [EventPump] when the native core emits the
  /// completed, failed or cancelled state. Progress events are forwarded to
  /// [onProgress]. Nothing here schedules a timer or retries in a loop.
  Future<PingResult> pingAsync({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'system.pingAsync';
    final jsonText = await _runAsync(methodName, onProgress: onProgress);
    return _parsePing(jsonText, fallbackMethod: methodName);
  }

  /// Submit `java.scan` and wait for its terminal task-state event.
  ///
  /// [roots] and [executables] are optional manual paths forwarded to the
  /// business module; [includeDefaults] controls whether its built-in
  /// discovery locations are also probed. The core routes the call to the C#
  /// business module. An absent or unsupported module surfaces as
  /// [CoreException] with `E_UNSUPPORTED`, `E_NOT_FOUND` or a module-specific
  /// code; callers can render that gracefully. Like [pingAsync], completion is
  /// driven only by the event pump. No timer or polling loop is involved.
  Future<JavaScanResult> scanJava({
    List<String> roots = const <String>[],
    List<String> executables = const <String>[],
    bool includeDefaults = true,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'java.scan';
    final paramsJson = jsonEncode(<String, Object?>{
      'roots': roots,
      'executables': executables,
      'includeDefaults': includeDefaults,
    });
    final jsonText = await _runAsync(
      methodName,
      paramsJson: paramsJson,
      onProgress: onProgress,
    );
    return _parseJavaScan(jsonText, fallbackMethod: methodName);
  }

  /// Ask C# to select a Java runtime for a game version. The settings decide
  /// whether the manual path or automatic scan is used.
  Future<JavaRuntime> selectJava({
    String? gameVersion,
    int? requiredMajor,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'java.select';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'gameVersion': ?gameVersion,
        'requiredMajor': ?requiredMajor,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final runtime = JavaRuntime.tryParse(data);
    if (runtime == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned no Java runtime.',
      );
    }
    return runtime;
  }

  /// Verify every generated/extracted artifact of a Forge/NeoForge install.
  ///
  /// The result is the raw `loader.verify` envelope. Unlike [scanJava], an
  /// envelope with `ok: false` is returned rather than thrown: missing
  /// artifacts are a normal diagnostic result, not a transport failure.
  Future<Map<String, Object?>> verifyLoader({
    required String installerPath,
    required String root,
    required String kind,
    String mode = 'full',
    String librariesPath = 'libraries',
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'loader.verify';
    final paramsJson = jsonEncode(<String, Object?>{
      'installerPath': installerPath,
      'root': root,
      'librariesPath': librariesPath,
      'kind': kind,
      'mode': mode,
    });
    final jsonText = await _runAsync(
      methodName,
      paramsJson: paramsJson,
      onProgress: onProgress,
    );
    return _decodeRawEnvelope(jsonText, fallbackMethod: methodName);
  }

  /// Ask the native module whether a launch can start in [mode].
  ///
  /// Online launch is a reserved contract: it currently returns
  /// `ready:false` with `ONLINE_LAUNCH_NOT_READY` until entitlement,
  /// certificates and online launch wiring are completed.
  Future<LaunchPreparation> prepareLaunch(
    String instanceId, {
    String mode = 'offline',
    bool requirePremium = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'launch.prepare';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'mode': mode,
        'requirePremium': requirePremium,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final preparation = LaunchPreparation.tryParse(data);
    if (preparation == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned an invalid preparation.',
      );
    }
    return preparation;
  }

  /// Start a basic offline launch through the C# module.
  Future<LaunchResult> launchInstance(
    String instanceId, {
    String mode = 'offline',
    bool requirePremium = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'launch.start';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'mode': mode,
        'requirePremium': requirePremium,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final pid = _asInt(data['pid']);
    if (pid == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned no pid.',
      );
    }
    return LaunchResult(
      pid: pid,
      instanceId: _asString(data['instanceId']) ?? instanceId,
      javaPath: _asString(data['javaPath']) ?? '',
      javaVersion: _asString(data['javaVersion']),
      accountName: _asString(data['accountName']),
      logPath: _asString(data['logPath']),
    );
  }

  /// List loader builds for a game version through the C# module.
  Future<List<LoaderVersionInfo>> getLoaderVersions({
    required String kind,
    required String gameVersion,
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'loader.versions';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'kind': kind,
        'gameVersion': gameVersion,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return List<LoaderVersionInfo>.unmodifiable(
      _asList(data['items'])
          .map(LoaderVersionInfo.tryParse)
          .whereType<LoaderVersionInfo>(),
    );
  }

  /// Build a Forge/NeoForge install plan from a local installer.
  ///
  /// The plan covers vanilla + loader libraries + the installer. Downloading
  /// and committing do not invoke Java; processors still require a JVM.
  Future<LoaderPlanResult> planLoaderInstall({
    required String root,
    required String installerPath,
    required String kind,
    required String gameVersion,
    required String loaderVersion,
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'loader.plan';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'root': root,
        'installerPath': installerPath,
        'kind': kind,
        'gameVersion': gameVersion,
        'loaderVersion': loaderVersion,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final plan = _asMap(data['plan'], methodName);
    final versionId = _asString(data['versionId']);
    final versionJson = _asString(data['versionJson']);
    if (versionId == null || versionJson == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned no version payload.',
      );
    }
    return LoaderPlanResult(
      plan: plan,
      versionId: versionId,
      versionJson: versionJson,
      requiresJava: data['requiresJava'] == true,
      installerPath: _asString(data['installerPath']),
    );
  }

  /// Write the merged version JSON into `<root>/versions/<id>/<id>.json`.
  Future<LoaderCommitResult> commitLoaderVersion({
    required String root,
    required String versionId,
    required String versionJson,
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'loader.commit';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'root': root,
        'versionId': versionId,
        'versionJson': versionJson,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return LoaderCommitResult(
      versionId: _asString(data['versionId']) ?? versionId,
      versionPath: _asString(data['versionPath']) ?? '',
    );
  }

  /// Locate the instance's installer and verify generated artifacts.
  ///
  /// [mode] is `quick` (existence/size plus hashes for generated launch files)
  /// or `full` (hash every artifact). Missing installers return a normal
  /// `ok:false` envelope with `MANIFEST_MISSING`, not a transport exception.
  Future<Map<String, Object?>> checkLoader({
    required String root,
    required String kind,
    required String gameVersion,
    required String loaderVersion,
    String mode = 'quick',
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'loader.check';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'root': root,
        'kind': kind,
        'gameVersion': gameVersion,
        'loaderVersion': loaderVersion,
        'mode': mode,
      }),
      onProgress: onProgress,
    );
    return _decodeRawEnvelope(jsonText, fallbackMethod: methodName);
  }

  /// Repair missing/corrupt Forge/NeoForge generated artifacts.
  ///
  /// [javaPath] must point at the JRE used for processors. The raw
  /// `loader.repair` envelope is returned even when repair failed; callers
  /// should inspect `ok`, `data.repair.status` and `data.verification`.
  Future<Map<String, Object?>> repairLoader({
    required String installerPath,
    required String root,
    required String kind,
    required String javaPath,
    String librariesPath = 'libraries',
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'loader.repair';
    final paramsJson = jsonEncode(<String, Object?>{
      'installerPath': installerPath,
      'root': root,
      'librariesPath': librariesPath,
      'kind': kind,
      'javaPath': javaPath,
    });
    final jsonText = await _runAsync(
      methodName,
      paramsJson: paramsJson,
      onProgress: onProgress,
    );
    return _decodeRawEnvelope(jsonText, fallbackMethod: methodName);
  }

  /// Build a vanilla game-file plan rooted at the instance's install root.
  Future<VanillaInstallPlan> planVanillaInstall({
    required String root,
    required String versionId,
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'version.plan';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'root': root,
        'versionId': versionId,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final plan = _asMap(data['plan'], methodName);
    final id = _asString(data['versionId']);
    final versionJson = _asString(data['versionJson']);
    final installRoot = _asString(data['installRoot']);
    if (id == null || versionJson == null || installRoot == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned an incomplete plan.',
      );
    }
    return VanillaInstallPlan(
      plan: plan,
      versionId: id,
      versionJson: versionJson,
      installRoot: installRoot,
    );
  }

  /// Write a downloaded vanilla version JSON into the install root.
  Future<void> commitVanillaVersion({
    required String root,
    required String versionId,
    required String versionJson,
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'version.commit';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'root': root,
        'versionId': versionId,
        'versionJson': versionJson,
      }),
      onProgress: onProgress,
    );
    _decodeEnvelope(jsonText, fallbackMethod: methodName);
  }

  /// Read Mojang's version manifest through the C# module.
  Future<MojangVersionList> listGameVersions({
    bool includeSnapshots = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'version.list';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'includeSnapshots': includeSnapshots,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return MojangVersionList(
      latestRelease: _asString(data['latestRelease']),
      latestSnapshot: _asString(data['latestSnapshot']),
      items: List<MojangGameVersion>.unmodifiable(
        _asList(data['items'])
            .map(MojangGameVersion.tryParse)
            .whereType<MojangGameVersion>(),
      ),
    );
  }

  /// Search Modrinth through the C# module.
  ///
  /// [kind] is `mod`, `shader` or `resourcepack`. For mods supply [loader];
  /// for shaders supply [shaderLoader]. The UI never calls the provider API.
  Future<ResourceSearchResult> searchModrinth({
    required String query,
    required String kind,
    String? gameVersion,
    String? loader,
    String? shaderLoader,
    String sort = 'relevance',
    int page = 0,
    int pageSize = 20,
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'resource.search';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': 'modrinth',
        'kind': kind,
        'query': query,
        'gameVersion': ?gameVersion,
        'loader': ?loader,
        'shaderLoader': ?shaderLoader,
        'sort': sort,
        'page': page,
        'pageSize': pageSize,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final items = <ResourceProject>[];
    for (final value in _asList(data['items'])) {
      final project = ResourceProject.tryParse(value);
      if (project != null) items.add(project);
    }

    return ResourceSearchResult(
      provider: _asString(data['provider']) ?? 'modrinth',
      total: _asInt(data['total']) ?? items.length,
      page: _asInt(data['page']) ?? page,
      pageSize: _asInt(data['pageSize']) ?? pageSize,
      items: List<ResourceProject>.unmodifiable(items),
    );
  }

  /// Load one Modrinth project, including gallery images and links.
  Future<ResourceProject> modrinthProject({
    required String projectId,
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'resource.project';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': 'modrinth',
        'projectId': projectId,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final project = ResourceProject.tryParse(data);
    if (project == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned no project.',
      );
    }
    return project;
  }

  /// List compatible Modrinth versions for a selected project.
  Future<List<ResourceVersion>> modrinthVersions({
    required String projectId,
    required String kind,
    required String gameVersion,
    String? loader,
    String? shaderLoader,
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'resource.versions';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': 'modrinth',
        'projectId': projectId,
        'kind': kind,
        'gameVersion': gameVersion,
        'loader': ?loader,
        'shaderLoader': ?shaderLoader,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return List<ResourceVersion>.unmodifiable(
      _asList(data['items'])
          .map(ResourceVersion.tryParse)
          .whereType<ResourceVersion>(),
    );
  }

  /// Ask C# to build a validated single-file DownloadPlan.
  Future<Map<String, Object?>> planModrinthDownload({
    required String projectId,
    required String kind,
    required String gameVersion,
    required String root,
    String? versionId,
    String? loader,
    String? shaderLoader,
    bool includeDependencies = false,
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'resource.plan';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': 'modrinth',
        'projectId': projectId,
        'kind': kind,
        'gameVersion': gameVersion,
        'root': root,
        'versionId': ?versionId,
        'loader': ?loader,
        'shaderLoader': ?shaderLoader,
        'includeDependencies': includeDependencies,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return _asMap(data['plan'], methodName);
  }

  /// Execute a validated DownloadPlan through the current Rust bridge.
  ///
  /// Until the C++ DownloadManager/Rust module owns effects, the C# module
  /// invokes the `archoera-dl` CLI with the plan file. The plan still goes
  /// through the same Rust validator/executor.
  Future<DownloadExecutionResult> executeDownload(
    Map<String, Object?> plan, {
    void Function(int done, int total)? onProgress,
  }) async {
    _ensureDownloadsEnabled();
    const methodName = 'download.execute';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'plan': plan}),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return DownloadExecutionResult(
      status: _asString(data['status']) ?? 'failed',
      ok: data['downloadOk'] == true,
      planPath: _asString(data['planPath']) ?? '',
      stdout: _asString(data['stdout']),
      stderr: _asString(data['stderr']),
    );
  }

  /// Read the C#-owned settings document.
  Future<AppSettings> getSettings({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'settings.get';
    final jsonText = await _runAsync(methodName, onProgress: onProgress);
    return _parseSettings(jsonText, methodName);
  }

  /// Update all configured instance roots and persist them atomically.
  Future<AppSettings> setInstanceRoots(
    List<String> instancesRoots, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'settings.set';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instancesRoots': instancesRoots,
      }),
      onProgress: onProgress,
    );
    return _parseSettings(jsonText, methodName);
  }

  /// Persist the manual Java selection or switch back to automatic.
  Future<AppSettings> setJavaSelection({
    String? javaPath,
    bool? javaAuto,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'settings.set';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'javaPath': javaPath,
        'javaAuto': ?javaAuto,
      }),
      onProgress: onProgress,
    );
    return _parseSettings(jsonText, methodName);
  }

  /// Read total/available physical memory through the native module.
  Future<SystemMemoryInfo?> getSystemMemory({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'system.memory';
    final jsonText = await _runAsync(methodName, onProgress: onProgress);
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return SystemMemoryInfo.tryParse(data);
  }

  /// Persist memory and JVM options.
  Future<AppSettings> setRuntimeOptions({
    int? memoryMinMb,
    int? memoryMaxMb,
    String? jvmArgs,
    String? gcMode,
    String? gameArgs,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'settings.set';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'memoryMinMb': ?memoryMinMb,
        'memoryMaxMb': ?memoryMaxMb,
        'jvmArgs': ?jvmArgs,
        'gcMode': ?gcMode,
        'gameArgs': ?gameArgs,
      }),
      onProgress: onProgress,
    );
    return _parseSettings(jsonText, methodName);
  }

  /// List mod files in an instance's `mods` directory.
  Future<List<InstalledMod>> listInstanceMods(
    String instanceId, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.mods.list';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'instanceId': instanceId}),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return List<InstalledMod>.unmodifiable(
      _asList(data['items'])
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
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'fileName': fileName,
        'enabled': enabled,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return InstalledMod.tryParse(data['item']);
  }

  /// Delete one mod file from an instance.
  Future<bool> removeInstanceMod({
    required String instanceId,
    required String fileName,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.mods.remove';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'fileName': fileName,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return data['removed'] == true;
  }

  /// Backward-compatible single-root update.
  Future<AppSettings> setInstancesRoot(
    String instancesRoot, {
    void Function(int done, int total)? onProgress,
  }) => setInstanceRoots([instancesRoot], onProgress: onProgress);

  AppSettings _parseSettings(String jsonText, String methodName) {
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final settings = AppSettings.tryParse(data['settings']);
    if (settings == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned no settings object.',
      );
    }
    return settings;
  }

  /// List persisted instances through the C# repository.
  Future<List<GameInstance>> listInstances({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.list';
    final jsonText = await _runAsync(methodName, onProgress: onProgress);
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return List<GameInstance>.unmodifiable(
      _asList(data['items'])
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
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        if (roots.isNotEmpty) 'roots': roots,
        'dryRun': dryRun,
      }),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final items = _asList(data['items'])
        .map(GameInstance.fromNative)
        .whereType<GameInstance>()
        .toList(growable: false);
    final rootsStatus = <InstanceRootStatus>[];
    for (final value in _asList(data['roots'])) {
      if (value is! Map) continue;
      rootsStatus.add(
        InstanceRootStatus(
          path: value['path']?.toString() ?? '',
          exists: value['exists'] == true,
          instanceCount: _asInt(value['instanceCount']) ?? 0,
        ),
      );
    }
    return InstanceSyncResult(
      items: List<GameInstance>.unmodifiable(items),
      adopted: _asList(data['adopted'])
          .map((value) => value.toString())
          .toList(),
      updated: _asList(data['updated'])
          .map((value) => value.toString())
          .toList(),
      missing: _asList(data['missing'])
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
    final jsonText = await _runAsync(
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
    return _parseInstance(jsonText, methodName);
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
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'id': id,
        'name': ?name,
        'favorite': ?favorite,
        'lastPlayedAt': ?lastPlayedAt?.toUtc().toIso8601String(),
      }),
      onProgress: onProgress,
    );
    return _parseInstance(jsonText, methodName);
  }

  /// Remove the registry entry. Files are intentionally not deleted here.
  Future<bool> deleteInstance(
    String id, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'instance.delete';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'id': id}),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return data['deleted'] == true;
  }

  GameInstance _parseInstance(String jsonText, String methodName) {
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final instance = GameInstance.fromNative(data['instance'] ?? data);
    if (instance == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned no instance object.',
      );
    }
    return instance;
  }

  /// Begin Microsoft device-code sign-in. The returned object never contains
  /// the device code or tokens; call [pollMicrosoftLogin] with [flowId].
  Future<MicrosoftDeviceCodeStart> startMicrosoftLogin({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.login.microsoft.start';
    final jsonText = await _runAsync(methodName, onProgress: onProgress);
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final start = MicrosoftDeviceCodeStart.fromNative(data);
    if (start == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned no usable device-code challenge.',
      );
    }
    return start;
  }

  /// Wait for the user to finish Microsoft authorization and the full
  /// Xbox Live → XSTS → Minecraft chain. This intentionally blocks until the
  /// C# module completes, expires, or is cancelled.
  Future<Account> pollMicrosoftLogin(
    String flowId, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.login.microsoft.poll';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'flowId': flowId}),
      onProgress: onProgress,
    );
    return _parseAccountEnvelope(jsonText, methodName);
  }

  Future<void> cancelMicrosoftLogin(String flowId) async {
    const methodName = 'account.login.microsoft.cancel';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'flowId': flowId}),
    );
    _decodeEnvelope(jsonText, fallbackMethod: methodName);
  }

  Future<List<Account>> listAccounts({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.list';
    final jsonText = await _runAsync(methodName, onProgress: onProgress);
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return List<Account>.unmodifiable(
      _asList(data['items']).map(Account.fromNative).whereType<Account>(),
    );
  }

  Future<Account?> currentAccount({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.current';
    final jsonText = await _runAsync(methodName, onProgress: onProgress);
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return Account.fromNative(data['account']);
  }

  Future<Account> selectAccount(
    String id, {
    bool requirePremium = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.select';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'id': id,
        'requirePremium': requirePremium,
      }),
      onProgress: onProgress,
    );
    return _parseAccountEnvelope(jsonText, methodName);
  }

  Future<Account> loginOffline(
    String name, {
    bool requirePremium = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.login.offline';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'name': name,
        'requirePremium': requirePremium,
      }),
      onProgress: onProgress,
    );
    return _parseAccountEnvelope(jsonText, methodName);
  }

  Future<Account> refreshAccount(
    String id, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.refresh';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'id': id}),
      onProgress: onProgress,
    );
    return _parseAccountEnvelope(jsonText, methodName);
  }

  Future<bool> removeAccount(
    String id, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.remove';
    final jsonText = await _runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'id': id}),
      onProgress: onProgress,
    );
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    return data['deleted'] == true;
  }

  Account _parseAccountEnvelope(String jsonText, String methodName) {
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = _asMap(decoded['data'], methodName);
    final account = Account.fromNative(data['account']);
    if (account == null) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$methodName returned no account object.',
      );
    }
    return account;
  }

  /// Shared async-task path used by [pingAsync], [scanJava] and loader calls.
  ///
  /// Starts a native task with the supplied [paramsJson], forwards progress
  /// events and waits for the terminal task-state event. The raw result JSON is
  /// returned; each caller owns envelope validation and parsing.
  Future<String> _runAsync(
    String methodName, {
    void Function(int done, int total)? onProgress,
    String paramsJson = '{}',
  }) async {
    final core = _requireCore();
    final pump = _pump;
    if (pump == null) {
      throw const CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: 'The core event pump is not running.',
      );
    }

    var taskId = 0;
    try {
      taskId = _startTask(core, methodName, paramsJson: paramsJson);
      _emitTask(
        TaskUpdate(
          taskId: taskId,
          method: methodName,
          state: TaskUpdateState.running,
        ),
      );

      final CoreEvent terminal;
      try {
        terminal = await pump.waitForTerminal(
          taskId,
          onEvent: (event) {
            if (event.type == CoreEventType.taskProgress) {
              onProgress?.call(event.done, event.total);
              _emitTask(
                TaskUpdate(
                  taskId: taskId,
                  method: methodName,
                  state: TaskUpdateState.running,
                  done: event.done,
                  total: event.total,
                  bytesPerSecond: event.bytesPerSec,
                  message:
                      event.payload['current']?.toString() ?? event.message,
                ),
              );
            }
          },
        );
      } on CoreEventPumpClosed catch (error) {
        _emitTask(
          TaskUpdate(
            taskId: taskId,
            method: methodName,
            state: TaskUpdateState.cancelled,
            message: error.message,
          ),
        );
        throw CoreException(
          status: _ok,
          code: 'E_CANCELLED',
          message: error.message,
          taskId: taskId,
        );
      }

      if (terminal.state == CoreTaskState.completed) {
        // The card may have been disposed (and the core shut down) while this
        // future was waiting on the event pump. Never touch a freed handle.
        if (!identical(_core, core)) {
          _emitTask(
            TaskUpdate(
              taskId: taskId,
              method: methodName,
              state: TaskUpdateState.cancelled,
            ),
          );
          throw CoreException(
            status: _ok,
            code: 'E_CANCELLED',
            message: 'The core client was closed before the result was read.',
            taskId: taskId,
          );
        }
        final result = _readResult(core, taskId);
        _emitTask(
          TaskUpdate(
            taskId: taskId,
            method: methodName,
            state: TaskUpdateState.completed,
          ),
        );
        return result;
      }

      _emitTask(
        TaskUpdate(
          taskId: taskId,
          method: methodName,
          state: terminal.state == CoreTaskState.cancelled
              ? TaskUpdateState.cancelled
              : TaskUpdateState.failed,
          message: terminal.message,
        ),
      );
      throw _taskException(terminal, taskId);
    } finally {
      if (taskId != 0) {
        pump.forgetTask(taskId);
        // `close()` may have shut the core down while this future was pending.
        if (identical(_core, core)) {
          try {
            _releaseFn(core, taskId);
          } catch (_) {
            // Release is best-effort; the core also expires terminal tasks.
          }
        }
      }
    }
  }

  void _ensureDownloadsEnabled() {
    if (!kChinaBuild) return;
    throw const CoreException(
      status: _ok,
      code: 'DOWNLOAD_DISABLED',
      message: 'Downloads are unavailable in this mainland-China build.',
    );
  }

  /// Ask the core to cancel [taskId].
  ///
  /// Cancellation itself is asynchronous: the pump observes the cancelled
  /// task-state event and completes the pending future with [CoreException].
  void _emitTask(TaskUpdate update) {
    if (_closed || _taskUpdates.isClosed) return;
    _taskUpdates.add(update);
  }

  void cancel(int taskId) {
    final core = _requireCore();
    final status = _cancelFn(core, taskId);
    if (status != _ok) {
      throw CoreException(
        status: status,
        code: _statusNames[status] ?? 'E_UNKNOWN',
        message: 'archoera_cancel failed for task $taskId.',
        taskId: taskId,
      );
    }
  }

  /// Shut down the core. Safe to call more than once.
  void close() {
    if (_closed) return;
    _closed = true;

    // Stop new wakeups, then let the core join its workers. The NativeCallable
    // must stay alive until shutdown returns: a worker may already hold the
    // wake target and call it while joining.
    final pump = _pump;
    _pump = null;
    pump?.detach();

    final core = _core;
    _core = null;
    try {
      if (core != null) {
        _shutdownFn(core);
      }
    } catch (_) {
      // A shutdown failure must not crash widget disposal.
    } finally {
      pump?.dispose();
      _taskUpdates.close();
    }
  }

  int _startTask(
    ffi.Pointer<ffi.Void> core,
    String methodName, {
    String paramsJson = '{}',
  }) {
    final method = methodName.toNativeUtf8();
    final params = paramsJson.toNativeUtf8();
    final taskIdPtr = calloc<ffi.Uint64>();
    final errorPtr = calloc<_ArchoeraErrorNative>();

    try {
      final status = _callFn(core, method, params, taskIdPtr, errorPtr);
      if (status != _ok) {
        throw _exceptionForStatus(status, errorPtr.ref);
      }

      final id = taskIdPtr.value;
      if (id == 0) {
        throw CoreException(
          status: _ok,
          code: 'E_INTERNAL',
          message:
              'archoera_call($methodName) returned OK but the task id was 0.',
        );
      }
      return id;
    } finally {
      calloc.free(errorPtr);
      calloc.free(taskIdPtr);
      malloc.free(params);
      malloc.free(method);
    }
  }

  CoreException _taskException(CoreEvent event, int taskId) {
    final state = event.state?.name ?? 'unknown';
    final fallback = event.state == CoreTaskState.cancelled
        ? 'E_CANCELLED'
        : 'E_INTERNAL';
    final message = event.message;

    return CoreException(
      status: event.code,
      code: _statusNames[event.code] ?? fallback,
      message: message == null || message.isEmpty
          ? 'Task $taskId ended in state $state.'
          : message,
      taskId: taskId,
    );
  }

  void _initialize() {
    final abiVersion = _abiVersionFn();
    if (abiVersion != expectedAbiVersion) {
      throw CoreException(
        status: _ok,
        code: 'E_UNSUPPORTED',
        message:
            'Unsupported archoera_core ABI v$abiVersion '
            '(expected v$expectedAbiVersion).',
      );
    }

    final buildInfoPtr = _buildInfoFn();
    _buildInfo = buildInfoPtr == ffi.nullptr ? '' : buildInfoPtr.toDartString();

    final config = '{}'.toNativeUtf8();
    final outCore = calloc<ffi.Pointer<ffi.Void>>();
    final errorPtr = calloc<_ArchoeraErrorNative>();

    try {
      final status = _initFn(config, outCore, errorPtr);
      if (status != _ok) {
        throw _exceptionForStatus(status, errorPtr.ref);
      }

      final core = outCore.value;
      if (core == ffi.nullptr) {
        throw const CoreException(
          status: _ok,
          code: 'E_INTERNAL',
          message: 'archoera_init returned OK but produced no core handle.',
        );
      }

      _core = core;
      _abiVersion = abiVersion;

      try {
        _pump = EventPump(core, _pollFn, _setWakeupFn);
      } catch (_) {
        // Do not leak a core that cannot deliver events.
        _core = null;
        _pump = null;
        try {
          _shutdownFn(core);
        } catch (_) {
          // Ignore cleanup failures and rethrow the pump error.
        }
        rethrow;
      }
    } finally {
      calloc.free(errorPtr);
      calloc.free(outCore);
      malloc.free(config);
    }
  }

  String _readResult(ffi.Pointer<ffi.Void> core, int taskId) {
    final outLength = calloc<ffi.IntPtr>();
    final errorPtr = calloc<_ArchoeraErrorNative>();
    ffi.Pointer<Utf8> buffer = ffi.nullptr;

    try {
      // Phase 1: ask for the required size.
      var status = _resultFn(core, taskId, ffi.nullptr, 0, outLength, errorPtr);
      if (status != _ok && status != _eBuffer) {
        throw _exceptionForStatus(status, errorPtr.ref, taskId: taskId);
      }

      var length = outLength.value;
      if (length <= 0) {
        throw CoreException(
          status: status,
          code: 'E_INTERNAL',
          message:
              'archoera_result reported an invalid buffer length ($length).',
          taskId: taskId,
        );
      }

      // Phase 2: read into a buffer of the reported size.
      buffer = malloc<ffi.Uint8>(length).cast<Utf8>();
      status = _resultFn(core, taskId, buffer, length, outLength, errorPtr);

      if (status == _eBuffer) {
        final needed = outLength.value;
        if (needed <= length) {
          throw CoreException(
            status: status,
            code: 'E_BUFFER',
            message:
                'archoera_result requested a larger buffer but did not '
                'increase out_len.',
            taskId: taskId,
          );
        }

        malloc.free(buffer);
        buffer = malloc<ffi.Uint8>(needed).cast<Utf8>();
        length = needed;
        status = _resultFn(core, taskId, buffer, length, outLength, errorPtr);
      }

      if (status != _ok) {
        throw _exceptionForStatus(status, errorPtr.ref, taskId: taskId);
      }

      return buffer.toDartString();
    } finally {
      if (buffer != ffi.nullptr) {
        malloc.free(buffer);
      }
      calloc.free(errorPtr);
      calloc.free(outLength);
    }
  }

  PingResult _parsePing(String jsonText, {required String fallbackMethod}) {
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: fallbackMethod);

    final dataValue = decoded['data'];
    final data = dataValue is Map<String, Object?>
        ? dataValue
        : const <String, Object?>{};
    final metaValue = decoded['meta'];
    final meta = metaValue is Map<String, Object?>
        ? metaValue
        : const <String, Object?>{};

    final pong = data.containsKey('pong') ? data['pong'] : decoded['pong'];
    final method = (data['method'] ?? decoded['method'] ?? fallbackMethod)
        .toString();
    final schemaVersion = meta['schemaVersion'];

    return PingResult(
      pong: pong,
      method: method,
      rawJson: decoded,
      data: data,
      schemaVersion: schemaVersion is int ? schemaVersion : null,
      requestId: meta['requestId']?.toString(),
    );
  }

  JavaScanResult _parseJavaScan(
    String jsonText, {
    required String fallbackMethod,
  }) {
    final decoded = _decodeEnvelope(jsonText, fallbackMethod: fallbackMethod);

    final dataValue = decoded['data'];
    if (dataValue is! Map<String, Object?>) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$fallbackMethod returned no data object.',
      );
    }

    final itemsValue = dataValue['items'];
    if (itemsValue is! List) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$fallbackMethod returned no items array.',
      );
    }

    final items = <JavaRuntime>[];
    for (final value in itemsValue) {
      final runtime = JavaRuntime.tryParse(value);
      if (runtime != null) items.add(runtime);
    }

    final metaValue = decoded['meta'];
    final meta = metaValue is Map<String, Object?>
        ? metaValue
        : const <String, Object?>{};

    return JavaScanResult(
      items: List<JavaRuntime>.unmodifiable(items),
      count: _asInt(dataValue['count']) ?? items.length,
      rawJson: decoded,
      schemaVersion: meta['schemaVersion'] is int
          ? meta['schemaVersion'] as int
          : null,
      requestId: meta['requestId']?.toString(),
    );
  }

  /// Decode an object envelope without treating `ok: false` as an exception.
  Map<String, Object?> _decodeRawEnvelope(
    String jsonText, {
    required String fallbackMethod,
  }) {
    Object? decoded;
    try {
      decoded = jsonDecode(jsonText);
    } on FormatException catch (error) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$fallbackMethod returned invalid JSON.',
        detail: error.message,
      );
    }

    if (decoded is! Map<String, Object?>) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$fallbackMethod returned a non-object JSON result.',
      );
    }

    return decoded;
  }

  /// Decode a method result envelope and surface `ok: false` as an exception.
  Map<String, Object?> _decodeEnvelope(
    String jsonText, {
    required String fallbackMethod,
  }) {
    Object? decoded;
    try {
      decoded = jsonDecode(jsonText);
    } on FormatException catch (error) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$fallbackMethod returned invalid JSON.',
        detail: error.message,
      );
    }

    if (decoded is! Map<String, Object?>) {
      throw CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: '$fallbackMethod returned a non-object JSON result.',
      );
    }

    if (decoded['ok'] == false) {
      final error = decoded['error'];
      if (error is Map) {
        throw CoreException(
          status: _ok,
          code: error['code']?.toString() ?? 'ERROR',
          message:
              error['message']?.toString() ??
              '$fallbackMethod reported failure.',
          detail: error['details']?.toString(),
        );
      }
      throw CoreException(
        status: _ok,
        code: 'ERROR',
        message: '$fallbackMethod reported failure without an error object.',
      );
    }

    return decoded;
  }

  static Map<String, Object?> _asMap(Object? value, String method) {
    if (value is Map<String, Object?>) return value;
    if (value is Map) {
      return value.map((key, value) => MapEntry(key.toString(), value));
    }
    throw CoreException(
      status: _ok,
      code: 'E_INVALID',
      message: '$method returned no object.',
    );
  }

  static List<Object?> _asList(Object? value) =>
      value is List ? value.cast<Object?>() : const <Object?>[];

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

  ffi.Pointer<ffi.Void> _requireCore() {
    if (_closed) {
      throw const CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: 'The core client has already been closed.',
      );
    }

    final core = _core;
    if (core == null || core == ffi.nullptr) {
      throw const CoreException(
        status: _ok,
        code: 'E_INVALID',
        message: 'The core client is not initialized.',
      );
    }
    return core;
  }

  CoreException _exceptionForStatus(
    int status,
    _ArchoeraErrorNative error, {
    int? taskId,
  }) {
    final statusCode = _statusNames[status] ?? 'E_UNKNOWN';
    final errorCode = _statusNames[error.code];
    final message = _readNativeString(error.message);
    final detail = _readNativeString(error.detail);

    return CoreException(
      status: status,
      code: errorCode ?? statusCode,
      message: message == null || message.isEmpty
          ? 'archoera_core call failed (status $statusCode).'
          : message,
      detail: detail,
      taskId: taskId,
    );
  }

  static String? _readNativeString(ffi.Pointer<Utf8> pointer) {
    if (pointer == ffi.nullptr) return null;
    try {
      return pointer.toDartString();
    } catch (_) {
      return null;
    }
  }

  static List<String> _defaultLibraryCandidates() {
    final candidates = <String>[];
    final configured = Platform.environment['ARCHOERACX_CORE_LIB'];
    if (configured != null && configured.trim().isNotEmpty) {
      candidates.add(configured.trim());
    }

    final name = _libraryFileName();
    final separator = Platform.pathSeparator;
    final executableDir = File(Platform.resolvedExecutable).parent.path;

    // Packaged app first: this must work even when the process CWD was
    // deleted or the app was launched from a desktop shortcut.
    candidates.addAll(<String>[
      '$executableDir$separator$name',
      '$executableDir${separator}lib$separator$name',
      '$executableDir$separator..${separator}lib$separator$name',
    ]);

    if (Platform.isMacOS) {
      candidates.add(
        '$executableDir$separator..${separator}Frameworks$separator$name',
      );
    }

    // Development layouts. CWD may be unavailable when the bundle directory
    // was replaced while the process was running; never let that abort the
    // entire lookup with PathNotFoundException.
    final cwd = _safeCurrentDirectory();
    if (cwd != null) {
      candidates.addAll(<String>[
        '$cwd${separator}build${separator}native$separator$name',
        '$cwd${separator}app${separator}build${separator}native$separator$name',
        '$cwd$separator..${separator}app${separator}build$separator'
            'native$separator$name',
        '$cwd${separator}build${separator}linux${separator}x64$separator'
            'release${separator}bundle${separator}lib$separator$name',
        '$cwd${separator}app${separator}build${separator}linux'
            '${separator}x64${separator}release${separator}bundle'
            '${separator}lib$separator$name',
      ]);

      if (Platform.isWindows) {
        candidates.add(
          '$cwd${separator}build${separator}windows${separator}x64'
          '${separator}runner${separator}Release$separator$name',
        );
      } else if (Platform.isMacOS) {
        candidates.add(
          '$cwd${separator}build${separator}macos${separator}Build'
          '${separator}Products${separator}Release$separator$name',
        );
      }
    }

    return candidates;
  }

  static String? _safeCurrentDirectory() {
    try {
      final value = Directory.current.path;
      return value.isEmpty ? null : value;
    } catch (_) {
      return null;
    }
  }

  static String? _tryAbsolutePath(String candidate) {
    try {
      final file = File(candidate);
      return file.isAbsolute ? file.path : file.absolute.path;
    } catch (_) {
      return null;
    }
  }

  static String _libraryFileName() {
    if (Platform.isWindows) return 'archoera_core.dll';
    if (Platform.isMacOS) return 'libarchoera_core.dylib';
    return 'libarchoera_core.so';
  }
}
