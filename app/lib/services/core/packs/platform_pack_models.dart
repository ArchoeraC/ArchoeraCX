/// A modpack row from a platform catalogue (FTB today).
class PlatformPackSummary {
  const PlatformPackSummary({
    required this.provider,
    required this.id,
    required this.name,
    required this.summary,
    this.iconUrl,
    this.author,
    this.downloads = 0,
    this.updatedAt,
    this.websiteUrl,
  });

  final String provider;
  final String id;
  final String name;
  final String summary;
  final String? iconUrl;
  final String? author;
  final int downloads;
  final DateTime? updatedAt;
  final String? websiteUrl;

  static PlatformPackSummary? tryParse(Object? value) {
    if (value is! Map) return null;
    final provider = value['provider']?.toString();
    final id = value['id']?.toString();
    final name = value['name']?.toString();
    if (provider == null || id == null || name == null) return null;
    return PlatformPackSummary(
      provider: provider,
      id: id,
      name: name,
      summary: value['summary']?.toString() ?? '',
      iconUrl: _stringOrNull(value['iconUrl']),
      author: _stringOrNull(value['author']),
      downloads: _asInt(value['downloads']) ?? 0,
      updatedAt: DateTime.tryParse(value['updatedAt']?.toString() ?? ''),
      websiteUrl: _stringOrNull(value['websiteUrl']),
    );
  }
}

/// One installable version from a platform catalogue.
class PlatformPackVersion {
  const PlatformPackVersion({
    required this.provider,
    required this.packId,
    required this.id,
    required this.name,
    required this.gameVersion,
    required this.loader,
    required this.loaderVersion,
    this.versionType = 'release',
    this.publishedAt,
    this.directArchive = false,
  });

  final String provider;
  final String packId;
  final String id;
  final String name;
  final String gameVersion;
  final String loader;
  final String loaderVersion;
  final String versionType;
  final DateTime? publishedAt;

  /// True when the provider publishes one direct zip and no per-file digest.
  /// The page warns before installing it.
  final bool directArchive;

  static PlatformPackVersion? tryParse(Object? value) {
    if (value is! Map) return null;
    final provider = value['provider']?.toString();
    final packId = value['packId']?.toString();
    final id = value['id']?.toString();
    if (provider == null || packId == null || id == null) return null;
    return PlatformPackVersion(
      provider: provider,
      packId: packId,
      id: id,
      name: value['name']?.toString() ?? id,
      gameVersion: value['gameVersion']?.toString() ?? '',
      loader: value['loader']?.toString() ?? 'vanilla',
      loaderVersion: value['loaderVersion']?.toString() ?? '',
      versionType: value['versionType']?.toString() ?? 'release',
      publishedAt: DateTime.tryParse(value['publishedAt']?.toString() ?? ''),
      directArchive: value['directArchive'] == true,
    );
  }
}

int? _asInt(Object? value) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value?.toString() ?? '');
}

String? _stringOrNull(Object? value) {
  final text = value?.toString().trim();
  return text == null || text.isEmpty ? null : text;
}

class PlatformPackSearchResult {
  const PlatformPackSearchResult({
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
  final List<PlatformPackSummary> items;
}

/// One registered platform provider exposed by the native registry.
class PlatformPackProviderInfo {
  const PlatformPackProviderInfo({required this.id, required this.name});

  final String id;
  final String name;

  static PlatformPackProviderInfo? tryParse(Object? value) {
    if (value is! Map) return null;
    final id = value['id']?.toString();
    final name = value['name']?.toString();
    if (id == null || name == null) return null;
    return PlatformPackProviderInfo(id: id, name: name);
  }
}
