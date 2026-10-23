import 'dart:convert';

import '../core_transport.dart';
import 'download_policy.dart';
import 'maintenance.dart';
import 'region_policy.dart';
import '../resources/resource_models.dart';
import 'update_check.dart';

/// Download-transport domain binding for the native core: executing validated
/// [DownloadPlan]s through the Rust module, the region and download-risk
/// policies, `.part` maintenance and the launcher update check.
mixin CoreClientDownloads on CoreTransport {
  /// Execute a validated DownloadPlan through the current Rust bridge.
  ///
  /// Until the C++ DownloadManager/Rust module owns effects, the C# module
  /// invokes the `archoera-dl` CLI with the plan file. The plan still goes
  /// through the same Rust validator/executor.
  Future<DownloadExecutionResult> executeDownload(
    Map<String, Object?> plan, {
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'download.execute';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'plan': plan}),
      onProgress: onProgress,
      label: label,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return DownloadExecutionResult(
      status: asString(data['status']) ?? 'failed',
      ok: data['downloadOk'] == true,
      planPath: asString(data['planPath']) ?? '',
      stdout: asString(data['stdout']),
      stderr: asString(data['stderr']),
    );
  }

  /// Deletes stale `.part` files of interrupted downloads.
  ///
  /// Only files untouched for [olderThanDays] are removed, so an in-flight
  /// transfer (which keeps its `.part` fresh) is never touched.
  Future<PartialCleanupResult> cleanPartialDownloads({
    int olderThanDays = 3,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'maintenance.cleanPartials';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'olderThanDays': olderThanDays}),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    return PartialCleanupResult.fromNative(decoded['data']);
  }

  /// Read coarse region policy (mainland China vs other regions).
  Future<RegionPolicyState> getRegionPolicy({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'region.policy';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final policy = RegionPolicyState.tryParse(data);
    if (policy == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'an invalid region policy');
    }
    return policy;
  }

  /// Read the local download-risk gate.
  Future<DownloadPolicyState> getDownloadPolicy({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'download.policy';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final policy = DownloadPolicyState.tryParse(data);
    if (policy == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'an invalid policy');
    }
    return policy;
  }

  /// Compare this build with the newest GitHub release.
  Future<UpdateCheckResult> checkForUpdate({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'update.check';
    final jsonText = await runAsync(
      methodName,
      paramsJson: '{}',
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final result = UpdateCheckResult.tryParse(decoded['data']);
    if (result == null) {
      throw CoreTransport.invalidEnvelope(
        methodName,
        'an incomplete version check',
      );
    }
    return result;
  }
}
