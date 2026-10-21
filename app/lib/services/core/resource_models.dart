/// Provider-independent resource models returned by the native module.
///
/// The UI consumes these values only; it never talks to Modrinth/CurseForge
/// directly. JSON parsing is deliberately defensive because module responses
/// can outlive the UI version that produced the request.
library;

class ResourceProject {
  const ResourceProject({
    required this.id,
    required this.slug,
    required this.name,
    required this.summary,
    required this.kind,
    this.downloads = 0,
    this.follows = 0,
    this.updatedAt,
    this.author,
    this.iconUrl,
    this.categories = const <String>[],
    this.loaders = const <String>[],
    this.gameVersions = const <String>[],
    this.clientSide,
    this.license,
  });

  final String id;
  final String slug;
  final String name;
  final String summary;
  final String kind;
  final int downloads;
  final int follows;
  final String? updatedAt;
  final String? author;
  final String? iconUrl;
  final List<String> categories;
  final List<String> loaders;
  final List<String> gameVersions;
  final String? clientSide;
  final String? license;

  static ResourceProject? tryParse(Object? value) {
    if (value is! Map) return null;
    final id = _string(value['id']);
    final name = _string(value['name']);
    if (id == null || id.isEmpty || name == null || name.isEmpty) return null;

    return ResourceProject(
      id: id,
      slug: _string(value['slug']) ?? '',
      name: name,
      summary: _string(value['summary']) ?? '',
      kind: _string(value['kind']) ?? 'mod',
      downloads: _int(value['downloads']) ?? 0,
      follows: _int(value['follows']) ?? 0,
      updatedAt: _string(value['updatedAt']),
      author: _string(value['author']),
      iconUrl: _string(value['iconUrl']),
      categories: _strings(value['categories']),
      loaders: _strings(value['loaders']),
      gameVersions: _strings(value['gameVersions']),
      clientSide: _string(value['clientSide']),
      license: _string(value['license']),
    );
  }
}

class ResourceVersion {
  const ResourceVersion({
    required this.id,
    required this.projectId,
    required this.name,
    required this.versionNumber,
    required this.versionType,
    this.publishedAt,
    this.downloads = 0,
    this.gameVersions = const <String>[],
    this.loaders = const <String>[],
    this.files = const <ResourceFile>[],
    this.dependencies = const <ResourceDependency>[],
  });

  final String id;
  final String projectId;
  final String name;
  final String versionNumber;
  final String versionType;
  final String? publishedAt;
  final int downloads;
  final List<String> gameVersions;
  final List<String> loaders;
  final List<ResourceFile> files;
  final List<ResourceDependency> dependencies;

  ResourceFile? get primaryFile {
    for (final file in files) {
      if (file.primary) return file;
    }
    return files.isEmpty ? null : files.first;
  }

  static ResourceVersion? tryParse(Object? value) {
    if (value is! Map) return null;
    final id = _string(value['id']);
    if (id == null || id.isEmpty) return null;

    return ResourceVersion(
      id: id,
      projectId: _string(value['projectId']) ?? '',
      name: _string(value['name']) ?? '',
      versionNumber: _string(value['versionNumber']) ?? '',
      versionType: _string(value['versionType']) ?? 'release',
      publishedAt: _string(value['publishedAt']),
      downloads: _int(value['downloads']) ?? 0,
      gameVersions: _strings(value['gameVersions']),
      loaders: _strings(value['loaders']),
      files: _list(value['files'])
          .map(ResourceFile.tryParse)
          .whereType<ResourceFile>()
          .toList(growable: false),
      dependencies: _list(value['dependencies'])
          .map(ResourceDependency.tryParse)
          .whereType<ResourceDependency>()
          .toList(growable: false),
    );
  }
}

class ResourceFile {
  const ResourceFile({
    required this.fileName,
    required this.url,
    required this.size,
    this.sha1,
    this.sha512,
    this.primary = false,
  });

  final String fileName;
  final String url;
  final int size;
  final String? sha1;
  final String? sha512;
  final bool primary;

  static ResourceFile? tryParse(Object? value) {
    if (value is! Map) return null;
    final fileName = _string(value['fileName']);
    if (fileName == null || fileName.isEmpty) return null;

    return ResourceFile(
      fileName: fileName,
      url: _string(value['url']) ?? '',
      size: _int(value['size']) ?? 0,
      sha1: _string(value['sha1']),
      sha512: _string(value['sha512']),
      primary: value['primary'] == true,
    );
  }
}

class ResourceDependency {
  const ResourceDependency({
    required this.kind,
    this.projectId,
    this.versionId,
    this.fileName,
  });

  final String kind;
  final String? projectId;
  final String? versionId;
  final String? fileName;

  static ResourceDependency? tryParse(Object? value) {
    if (value is! Map) return null;
    final kind = _string(value['kind']);
    if (kind == null || kind.isEmpty) return null;

    return ResourceDependency(
      kind: kind,
      projectId: _string(value['projectId']),
      versionId: _string(value['versionId']),
      fileName: _string(value['fileName']),
    );
  }
}

class ResourceSearchResult {
  const ResourceSearchResult({
    required this.provider,
    required this.total,
    required this.page,
    required this.pageSize,
    required this.items,
  });

  final String provider;
  final int total;
  final int page;
  final int pageSize;
  final List<ResourceProject> items;
}

class DownloadExecutionResult {
  const DownloadExecutionResult({
    required this.status,
    required this.ok,
    required this.planPath,
    this.stdout,
    this.stderr,
  });

  final String status;
  final bool ok;
  final String planPath;
  final String? stdout;
  final String? stderr;
}

String? _string(Object? value) {
  if (value == null) return null;
  final text = value.toString().trim();
  return text.isEmpty ? null : text;
}

int? _int(Object? value) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  if (value is String) return int.tryParse(value);
  return null;
}

List<Object?> _list(Object? value) =>
    value is List ? value.cast<Object?>() : const <Object?>[];

List<String> _strings(Object? value) =>
    _list(value).map(_string).whereType<String>().toList(growable: false);
