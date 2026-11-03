/// Value types for `java.distributions*`.
///
/// Catalogue responses carry provider-native ids only. The archive URL and
/// SHA-256 stay inside C#; install sends the id back and the provider resolves
/// the current download again.
library;

/// OS/architecture/archive triple a catalogue request targets. The current
/// host is marked with [current]; other entries are offered for manual
/// downloads to another machine.
final class JavaDistributionPlatformInfo {
  const JavaDistributionPlatformInfo({
    required this.os,
    required this.arch,
    required this.archive,
    this.current = false,
  });

  final String os;
  final String arch;
  final String archive;

  /// True when this triple runs on the machine hosting the app.
  final bool current;

  static JavaDistributionPlatformInfo? tryParse(Object? value) {
    if (value is! Map) return null;
    final os = value['os']?.toString();
    final arch = value['arch']?.toString();
    if (os == null || arch == null) return null;
    return JavaDistributionPlatformInfo(
      os: os,
      arch: arch,
      archive: value['archive']?.toString() ?? '',
      current: value['current'] == true,
    );
  }
}

/// One registered JDK vendor.
final class JavaDistributionProviderInfo {
  const JavaDistributionProviderInfo({
    required this.id,
    required this.name,
    required this.shortName,
    required this.vendor,
    required this.homepage,
    required this.listsAllVersions,
    required this.recommendedMajor,
  });

  final String id;
  final String name;

  /// Compact label for provider buttons.
  final String shortName;
  final String vendor;
  final String homepage;
  final bool listsAllVersions;
  final int recommendedMajor;

  static JavaDistributionProviderInfo? tryParse(Object? value) {
    if (value is! Map) return null;
    final id = value['id']?.toString();
    final name = value['name']?.toString();
    if (id == null || name == null) return null;
    return JavaDistributionProviderInfo(
      id: id,
      name: name,
      shortName: _stringOrNull(value['shortName']) ?? name,
      vendor: value['vendor']?.toString() ?? '',
      homepage: value['homepage']?.toString() ?? '',
      listsAllVersions: value['listsAllVersions'] == true,
      recommendedMajor: _asInt(value['recommendedMajor']) ?? 21,
    );
  }
}

/// Static provider catalogue plus the supported target triples.
final class JavaDistributionCatalog {
  const JavaDistributionCatalog({
    required this.platform,
    required this.platforms,
    required this.providers,
    required this.recommendedMajor,
    required this.defaultRoot,
  });

  /// The host triple; requests without an explicit platform use this one.
  final JavaDistributionPlatformInfo platform;

  /// Every triple the installer may request, including foreign platforms.
  final List<JavaDistributionPlatformInfo> platforms;
  final List<JavaDistributionProviderInfo> providers;
  final int recommendedMajor;
  final String defaultRoot;
}

/// Feature releases available from one provider.
final class JavaDistributionMajors {
  const JavaDistributionMajors({
    required this.provider,
    required this.majors,
    required this.recommendedMajor,
  });

  final String provider;
  final List<int> majors;
  final int recommendedMajor;
}

/// One selectable build.
final class JavaDistributionVersion {
  const JavaDistributionVersion({
    required this.id,
    required this.major,
    required this.version,
    required this.archive,
    required this.size,
    required this.sha256,
    required this.lts,
    required this.recommended,
    required this.latestPatch,
    required this.releasedAt,
  });

  final String id;
  final int major;
  final String version;
  final String archive;
  final int size;
  final String? sha256;
  final bool lts;
  final bool recommended;

  /// False for historical patches that may contain known CVEs.
  final bool latestPatch;
  final DateTime? releasedAt;
}

/// Build list for one provider/major pair.
final class JavaDistributionVersions {
  const JavaDistributionVersions({
    required this.provider,
    required this.major,
    required this.recommendedMajor,
    required this.versions,
  });

  final String provider;
  final int major;
  final int recommendedMajor;
  final List<JavaDistributionVersion> versions;
}

/// Installed JDK location after a successful archive install.
final class JavaDistributionInstallResult {
  const JavaDistributionInstallResult({
    required this.provider,
    required this.version,
    required this.major,
    required this.home,
    required this.executable,
    required this.bytes,
    required this.archive,
    required this.extracted,
  });

  final String provider;
  final String version;
  final int major;
  final String home;
  final String executable;
  final int bytes;

  /// Kept archive path when the user disabled auto-delete.
  final String? archive;

  /// False when the user asked to download the archive without extracting it.
  final bool extracted;

  static JavaDistributionInstallResult? tryParse(Object? value) {
    if (value is! Map) return null;
    final provider = value['provider']?.toString();
    final home = value['home']?.toString() ?? '';
    final executable = value['executable']?.toString() ?? '';
    final extracted = value['extracted'] != false;
    if (provider == null ||
        (extracted && (home.isEmpty || executable.isEmpty))) {
      return null;
    }
    return JavaDistributionInstallResult(
      provider: provider,
      version: value['version']?.toString() ?? '',
      major: _asInt(value['major']) ?? 0,
      home: home,
      executable: executable,
      bytes: _asInt(value['bytes']) ?? 0,
      archive: _stringOrNull(value['archive']),
      extracted: extracted,
    );
  }
}

/// One CVE that affects the selected JDK build.
final class JavaCveAdvisory {
  const JavaCveAdvisory({
    required this.id,
    required this.severity,
    required this.score,
    required this.summary,
    required this.url,
    required this.publishedAt,
    required this.aliases,
  });

  final String id;

  /// One of `critical`, `high`, `medium`, `low`, `none`, `unknown`.
  final String severity;
  final double? score;
  final String summary;
  final String? url;
  final DateTime? publishedAt;
  final List<String> aliases;

  static JavaCveAdvisory? tryParse(Object? value) {
    if (value is! Map) return null;
    final id = _stringOrNull(value['id']);
    if (id == null) return null;
    return JavaCveAdvisory(
      id: id,
      severity: _stringOrNull(value['severity']) ?? 'unknown',
      score: _asDouble(value['score']),
      summary: value['summary']?.toString() ?? '',
      url: _stringOrNull(value['url']),
      publishedAt: DateTime.tryParse(value['publishedAt']?.toString() ?? ''),
      aliases: _stringList(value['aliases']),
    );
  }
}

/// CVE lookup result for one build. `status` is `ok` or `unavailable`; an
/// unavailable feed never blocks the catalogue or the installer by itself.
final class JavaCveReport {
  const JavaCveReport({
    required this.provider,
    required this.version,
    required this.status,
    required this.reason,
    required this.advisories,
    required this.sources,
    required this.checkedAt,
  });

  final String provider;
  final String version;
  final String status;
  final String? reason;
  final List<JavaCveAdvisory> advisories;
  final List<String> sources;
  final DateTime? checkedAt;

  bool get available => status == 'ok';

  static JavaCveReport? tryParse(Object? value) {
    if (value is! Map) return null;
    final provider = _stringOrNull(value['provider']);
    final version = _stringOrNull(value['version']);
    if (provider == null || version == null) return null;
    final advisories = <JavaCveAdvisory>[];
    final rawAdvisories = value['advisories'];
    if (rawAdvisories is List) {
      for (final item in rawAdvisories) {
        final advisory = JavaCveAdvisory.tryParse(item);
        if (advisory != null) advisories.add(advisory);
      }
    }

    return JavaCveReport(
      provider: provider,
      version: version,
      status: _stringOrNull(value['status']) ?? 'unavailable',
      reason: _stringOrNull(value['reason']),
      advisories: List.unmodifiable(advisories),
      sources: List.unmodifiable(_stringList(value['sources'])),
      checkedAt: DateTime.tryParse(value['checkedAt']?.toString() ?? ''),
    );
  }
}

int? _asInt(Object? value) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  return int.tryParse(value?.toString() ?? '');
}

double? _asDouble(Object? value) {
  if (value is num) return value.toDouble();
  return double.tryParse(value?.toString() ?? '');
}

List<String> _stringList(Object? value) {
  if (value is! List) return const <String>[];
  return List.unmodifiable(
    value
        .map((item) => item?.toString().trim())
        .whereType<String>()
        .where((item) => item.isNotEmpty),
  );
}

String? _stringOrNull(Object? value) {
  final text = value?.toString().trim();
  return text == null || text.isEmpty ? null : text;
}
