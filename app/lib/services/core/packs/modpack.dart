import 'dart:convert';

import '../core_transport.dart';
import 'pack_install_result.dart';
import '../resources/resource.dart'
    show providerModrinth;

/// Modpack domain binding for the native core: `modpack.inspect`,
/// `modpack.installLocal` and `modpack.install`.
mixin CoreClientModpack on CoreTransport {
  /// Parse a local `.mrpack` without installing anything.
  Future<PackInspection> inspectModpack(
    String path, {
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'modpack.inspect';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'path': path}),
      onProgress: onProgress,
      label: label,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final inspection = PackInspection.tryParse(data);
    if (inspection == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'no pack metadata');
    }
    return inspection;
  }

  /// Install a local `.mrpack` into a new instance.
  Future<PackInstallResult> installModpackLocal({
    required String path,
    String? name,
    String? rootPath,
    CoreDetailedProgress? onDetailedProgress,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'modpack.installLocal';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'path': path,
        'name': ?name,
        'rootPath': ?rootPath,
      }),
      onProgress: onProgress,
      label: label,
      onDetailedProgress: onDetailedProgress,
    );
    return _parsePackInstall(this, jsonText, methodName);
  }

  /// Install a Modrinth modpack version into a new instance.
  Future<PackInstallResult> installModpackFromModrinth({
    required String projectId,
    required String versionId,
    String? name,
    String? rootPath,
    CoreDetailedProgress? onDetailedProgress,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'modpack.install';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': providerModrinth,
        'projectId': projectId,
        'versionId': versionId,
        'name': ?name,
        'rootPath': ?rootPath,
      }),
      onProgress: onProgress,
      label: label,
      onDetailedProgress: onDetailedProgress,
    );
    return _parsePackInstall(this, jsonText, methodName);
  }
}

PackInstallResult _parsePackInstall(
  CoreTransport transport,
  String jsonText,
  String methodName,
) {
  final decoded = transport.decodeEnvelope(jsonText, fallbackMethod: methodName);
  final data = transport.asMap(decoded['data'], methodName);
  final result = PackInstallResult.tryParse(data);
  if (result == null) {
    throw CoreTransport.invalidEnvelope(methodName, 'no installed instance');
  }
  return result;
}
