import 'dart:convert';

import '../core_transport.dart';
import 'loader_plan.dart';
import 'loader_versions.dart';
import '../packs/pack_install_result.dart';

/// Loader (Forge/NeoForge) domain binding for the native core: `loader.verify`,
/// `loader.versions`, `loader.plan`, `loader.commit`, `loader.check`,
/// `loader.repair` and `loader.autoRepair`.
mixin CoreClientLoader on CoreTransport {
  /// Verify every generated/extracted artifact of a Forge/NeoForge install.
  ///
  /// The result is the raw `loader.verify` envelope. Unlike `scanJava`, an
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
    final jsonText = await runAsync(
      methodName,
      paramsJson: paramsJson,
      onProgress: onProgress,
    );
    return decodeRawEnvelope(jsonText, fallbackMethod: methodName);
  }

  /// List loader builds for a game version through the C# module.
  Future<List<LoaderVersionInfo>> getLoaderVersions({
    required String kind,
    required String gameVersion,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'loader.versions';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'kind': kind,
        'gameVersion': gameVersion,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return List<LoaderVersionInfo>.unmodifiable(
      asList(data['items'])
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
    bool shareLoaderFiles = true,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'loader.plan';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'root': root,
        'installerPath': installerPath,
        'kind': kind,
        'gameVersion': gameVersion,
        'loaderVersion': loaderVersion,
        'shareLoaderFiles': shareLoaderFiles,
      }),
      onProgress: onProgress,
      label: label,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final plan = asMap(data['plan'], methodName);
    final versionId = asString(data['versionId']);
    final versionJson = asString(data['versionJson']);
    if (versionId == null || versionJson == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'no version payload');
    }
    return LoaderPlanResult(
      plan: plan,
      versionId: versionId,
      versionJson: versionJson,
      requiresJava: data['requiresJava'] == true,
      installerPath: asString(data['installerPath']),
      installRoot: asString(data['installRoot']) ?? root,
      shareLoaderFiles: data['shareLoaderFiles'] != false,
    );
  }

  /// Write the merged version JSON into `<root>/versions/<id>/<id>.json`.
  Future<LoaderCommitResult> commitLoaderVersion({
    required String root,
    required String versionId,
    required String versionJson,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'loader.commit';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'root': root,
        'versionId': versionId,
        'versionJson': versionJson,
      }),
      onProgress: onProgress,
      label: label,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return LoaderCommitResult(
      versionId: asString(data['versionId']) ?? versionId,
      versionPath: asString(data['versionPath']) ?? '',
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
    final jsonText = await runAsync(
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
    return decodeRawEnvelope(jsonText, fallbackMethod: methodName);
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
    String? label,
  }) async {
    const methodName = 'loader.repair';
    final paramsJson = jsonEncode(<String, Object?>{
      'installerPath': installerPath,
      'root': root,
      'librariesPath': librariesPath,
      'kind': kind,
      'javaPath': javaPath,
    });
    final jsonText = await runAsync(
      methodName,
      paramsJson: paramsJson,
      onProgress: onProgress,
      label: label,
    );
    return decodeRawEnvelope(jsonText, fallbackMethod: methodName);
  }

  /// Repair an existing Forge/NeoForge instance: restore missing downloads
  /// and rerun loader processors. Used by the dependency-check dialog.
  Future<PackRepairResult> repairInstanceDependencies(
    String instanceId, {
    CoreDetailedProgress? onDetailedProgress,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'loader.autoRepair';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'instanceId': instanceId}),
      onProgress: onProgress,
      label: label,
      onDetailedProgress: onDetailedProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final result = PackRepairResult.tryParse(data);
    if (result == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'no repaired instance');
    }
    return result;
  }
}
