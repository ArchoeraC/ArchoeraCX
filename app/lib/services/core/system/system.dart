import 'dart:convert';

import 'build_identity.dart';
import '../core_transport.dart';
import 'single_instance.dart';
import 'system_appearance.dart';
import 'system_memory.dart';
import 'system_status.dart';

/// System / OS-integration domain binding for the native core. Only these
/// methods ever touch the desktop from Dart side; each one delegates to a
/// native OS API through the C++ core.
mixin CoreClientSystem on CoreTransport {
  /// Claims the per-user single-instance lock through the native core.
  ///
  /// The primary process also starts the native activation listener, so later
  /// launches wake this one through `platform.activate` domain events. The
  /// caller must subscribe to [events] before invoking this method when it
  /// needs every activation; otherwise an activation that races startup can be
  /// missed.
  Future<SingleInstanceClaim> claimSingleInstance() async {
    const methodName = 'platform.singleInstance';
    final jsonText = await runAsync(methodName);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return SingleInstanceClaim(
      primary: data['primary'] == true,
      signaled: data['signaled'] == true,
    );
  }

  /// Read total/available physical memory through the native module.
  Future<SystemMemoryInfo?> getSystemMemory({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'system.memory';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return SystemMemoryInfo.tryParse(data);
  }

  /// Read the display adapters from the C++ core.
  ///
  /// The payload is forwarded to `launch.start` as `hardware`; the launch
  /// module decides whether to prefer the dedicated GPU.
  Future<Map<String, Object?>?> getSystemGpu({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'system.gpu';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = decoded['data'];
    return data is Map ? Map<String, Object?>.from(data) : null;
  }

  /// Read build provenance (official build badge) through the native module.
  ///
  /// Only the SHA-256 of the compiled-in Microsoft client id is returned; the
  /// id itself never crosses the ABI.
  Future<BuildIdentity?> getBuildIdentity({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'system.identity';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    return BuildIdentity.tryParse(decoded['data']);
  }

  /// Read the OS light/dark preference and accent color from the C++ core.
  ///
  /// The first call also starts the native watcher; subsequent changes arrive
  /// as `system.appearance` domain events.
  Future<SystemAppearance?> getSystemAppearance({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'system.appearance';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    return SystemAppearance.tryParse(decoded['data']);
  }

  /// Opens an http(s) URL through the OS default browser using native APIs.
  ///
  /// Returns false when the core is unavailable or the platform refused the
  /// request; callers may keep a Dart-side fallback for older cores.
  Future<bool> openExternalUrl(
    String url, {
    void Function(int done, int total)? onProgress,
  }) => tryRunBool(
    'system.openUrl',
    paramsJson: jsonEncode(<String, Object?>{'url': url}),
    dataKey: 'opened',
    onProgress: onProgress,
  );

  /// Opens an existing absolute directory in the OS file manager.
  ///
  /// The core validates that [path] is an existing absolute directory and
  /// rejects anything else with `E_INVALID`. Returns false when the core is
  /// unavailable or the platform refused the request, so callers can show a
  /// single localized failure message.
  Future<bool> openSystemPath(
    String path, {
    void Function(int done, int total)? onProgress,
  }) => tryRunBool(
    'system.openPath',
    paramsJson: jsonEncode(<String, Object?>{'path': path}),
    dataKey: 'opened',
    onProgress: onProgress,
  );

  /// Reveals an existing absolute directory in the OS file manager.
  ///
  /// Uses the platform "show item" API where available (Linux
  /// `org.freedesktop.FileManager1`, Windows Explorer, macOS Finder) and
  /// falls back to opening the directory. The core rejects relative, missing
  /// or non-directory paths with `E_INVALID`; false means the platform
  /// refused the request.
  Future<bool> revealSystemPath(
    String path, {
    void Function(int done, int total)? onProgress,
  }) => tryRunBool(
    'system.revealPath',
    paramsJson: jsonEncode(<String, Object?>{'path': path}),
    dataKey: 'opened',
    onProgress: onProgress,
  );

  /// Posts a desktop notification through the OS notification service.
  Future<bool> showSystemNotification({
    required String title,
    required String body,
  }) => tryRunBool(
    'system.notify',
    paramsJson: jsonEncode(<String, Object?>{'title': title, 'body': body}),
    dataKey: 'sent',
  );

  /// Read CPU/memory/disk/process load for the Wiki page.
  ///
  /// [path] selects the volume used for the disk row; the module falls back to
  /// the launcher directory when it is omitted.
  Future<SystemStatus?> getSystemStatus({
    String? path,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'system.status';
    final jsonText = await runAsync(
      methodName,
      paramsJson: path == null
          ? '{}'
          : jsonEncode(<String, Object?>{'path': path}),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return SystemStatus.tryParse(data);
  }

  /// Starts the core-owned load sampler. The core pushes `system.status`
  /// domain events every [intervalMs] while the watch is active; Dart never
  /// polls. Idempotent: calling it again only updates the interval/path.
  Future<bool> startSystemStatusWatch({
    int intervalMs = 5000,
    String? path,
  }) => tryRunBool(
    'system.status.watch',
    paramsJson: jsonEncode(<String, Object?>{
      'intervalMs': intervalMs,
      'path': ?path,
    }),
    dataKey: 'watching',
  );

  /// Stops the core-owned load sampler. Safe to call when no watch is active
  /// or when the core has already been closed.
  Future<void> stopSystemStatusWatch() async {
    try {
      await runAsync('system.status.unwatch');
    } catch (_) {
      // Shutdown races are expected; the core stops the sampler itself.
    }
  }
}
