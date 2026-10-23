import 'dart:convert';

import '../core_transport.dart';
import 'resource_models.dart';

/// The provider id every `resource.*` method call carries. The core routes it
/// to the C# provider client.
const String providerModrinth = 'modrinth';

/// Resource-search domain binding for the native core: `resource.search`,
/// `resource.project`, `resource.versions` and `resource.plan`.
mixin CoreClientResource on CoreTransport {
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
    const methodName = 'resource.search';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': providerModrinth,
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
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final items = <ResourceProject>[];
    for (final value in asList(data['items'])) {
      final project = ResourceProject.tryParse(value);
      if (project != null) items.add(project);
    }

    return ResourceSearchResult(
      provider: asString(data['provider']) ?? providerModrinth,
      total: asInt(data['total']) ?? items.length,
      page: asInt(data['page']) ?? page,
      pageSize: asInt(data['pageSize']) ?? pageSize,
      items: List<ResourceProject>.unmodifiable(items),
    );
  }

  /// Load one Modrinth project, including gallery images and links.
  Future<ResourceProject> modrinthProject({
    required String projectId,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'resource.project';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': providerModrinth,
        'projectId': projectId,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final project = ResourceProject.tryParse(data);
    if (project == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'no project');
    }
    return project;
  }

  /// List compatible Modrinth versions for a selected project.
  Future<List<ResourceVersion>> modrinthVersions({
    required String projectId,
    required String kind,
    String? gameVersion,
    String? loader,
    String? shaderLoader,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'resource.versions';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': providerModrinth,
        'projectId': projectId,
        'kind': kind,
        'gameVersion': ?gameVersion,
        'loader': ?loader,
        'shaderLoader': ?shaderLoader,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return List<ResourceVersion>.unmodifiable(
      asList(data['items'])
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
    const methodName = 'resource.plan';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': providerModrinth,
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
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return asMap(data['plan'], methodName);
  }
}
