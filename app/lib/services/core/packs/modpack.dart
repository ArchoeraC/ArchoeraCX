import 'dart:convert';

import '../core_transport.dart';
import 'pack_install_result.dart';
import 'platform_pack_models.dart';
import '../resources/resource.dart' show providerModrinth;

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

  /// Registered platform catalogues, including their display names.
  Future<List<PlatformPackProviderInfo>> platformPackProviders({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'pack.platform.providers';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return List<PlatformPackProviderInfo>.unmodifiable(
      asList(data['items'])
          .map(PlatformPackProviderInfo.tryParse)
          .whereType<PlatformPackProviderInfo>(),
    );
  }

  /// Search a platform catalogue (currently FTB) through the C# module.
  Future<PlatformPackSearchResult> searchPlatformPacks({
    required String provider,
    String query = '',
    int page = 0,
    int pageSize = 20,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'pack.platform.search';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': provider,
        'query': query,
        'page': page,
        'pageSize': pageSize,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return PlatformPackSearchResult(
      provider: data['provider']?.toString() ?? provider,
      total: asInt(data['total']) ?? 0,
      page: asInt(data['page']) ?? page,
      pageSize: asInt(data['pageSize']) ?? pageSize,
      items: List<PlatformPackSummary>.unmodifiable(
        asList(data['items'])
            .map(PlatformPackSummary.tryParse)
            .whereType<PlatformPackSummary>(),
      ),
    );
  }

  /// List versions for a platform pack.
  Future<List<PlatformPackVersion>> platformPackVersions({
    required String provider,
    required String packId,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'pack.platform.versions';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': provider,
        'packId': packId,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return List<PlatformPackVersion>.unmodifiable(
      asList(data['items'])
          .map(PlatformPackVersion.tryParse)
          .whereType<PlatformPackVersion>(),
    );
  }

  /// Install one platform pack version into a new instance.
  Future<PackInstallResult> installPlatformPack({
    required String provider,
    required String packId,
    required String versionId,
    String? name,
    String? rootPath,
    CoreDetailedProgress? onDetailedProgress,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'pack.platform.install';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': provider,
        'packId': packId,
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
  final decoded = transport.decodeEnvelope(
    jsonText,
    fallbackMethod: methodName,
  );
  final data = transport.asMap(decoded['data'], methodName);
  final result = PackInstallResult.tryParse(data);
  if (result == null) {
    throw CoreTransport.invalidEnvelope(methodName, 'no installed instance');
  }
  return result;
}
