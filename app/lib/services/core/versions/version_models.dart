/// One entry from Mojang's version manifest.
class MojangGameVersion {
  const MojangGameVersion({
    required this.id,
    required this.type,
    this.releaseTime,
    this.url,
    this.sha1,
  });

  final String id;
  final String type;
  final DateTime? releaseTime;
  final String? url;
  final String? sha1;

  bool get isRelease => type == 'release';

  static MojangGameVersion? tryParse(Object? value) {
    if (value is! Map) return null;
    final id = value['id']?.toString();
    final type = value['type']?.toString();
    if (id == null || id.isEmpty || type == null) return null;
    final released = value['releaseTime']?.toString();
    return MojangGameVersion(
      id: id,
      type: type,
      releaseTime: released == null ? null : DateTime.tryParse(released),
      url: value['url']?.toString(),
      sha1: value['sha1']?.toString(),
    );
  }
}

class MojangVersionList {
  const MojangVersionList({
    required this.items,
    this.latestRelease,
    this.latestSnapshot,
  });

  final List<MojangGameVersion> items;
  final String? latestRelease;
  final String? latestSnapshot;
}
