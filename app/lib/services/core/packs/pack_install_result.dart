import '../../../models/game_instance.dart';

/// Result of installing a Modrinth `.mrpack` into a new instance.
class PackInstallResult {
  const PackInstallResult({
    required this.instance,
    required this.packVersionId,
    required this.source,
    required this.downloadedFiles,
    required this.skippedFiles,
    required this.extractedFiles,
    required this.downloadedBytes,
  });

  final GameInstance instance;
  final String packVersionId;
  final String source;
  final int downloadedFiles;
  final int skippedFiles;
  final int extractedFiles;
  final int downloadedBytes;

  static PackInstallResult? tryParse(Object? value) {
    if (value is! Map) return null;
    final instance = GameInstance.fromNative(value['instance']);
    if (instance == null) return null;
    return PackInstallResult(
      instance: instance,
      packVersionId: value['packVersionId']?.toString() ?? '',
      source: value['source']?.toString() ?? '',
      downloadedFiles: _int(value['downloadedFiles']) ?? 0,
      skippedFiles: _int(value['skippedFiles']) ?? 0,
      extractedFiles: _int(value['extractedFiles']) ?? 0,
      downloadedBytes: _int(value['downloadedBytes']) ?? 0,
    );
  }

  static int? _int(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    if (value is String) return int.tryParse(value);
    return null;
  }
}

/// Result of repairing an existing instance's loader dependencies.
class PackRepairResult {
  const PackRepairResult({
    required this.instanceId,
    required this.versionId,
    required this.loader,
    required this.gameVersion,
  });

  final String instanceId;
  final String versionId;
  final String loader;
  final String gameVersion;

  static PackRepairResult? tryParse(Object? value) {
    if (value is! Map) return null;
    final instanceId = value['instanceId']?.toString();
    final versionId = value['versionId']?.toString();
    if (instanceId == null || versionId == null) return null;
    return PackRepairResult(
      instanceId: instanceId,
      versionId: versionId,
      loader: value['loader']?.toString() ?? '',
      gameVersion: value['gameVersion']?.toString() ?? '',
    );
  }
}

/// Preview returned by `pack.inspect` for a local `.mrpack`.
class PackInspection {
  const PackInspection({
    required this.name,
    required this.versionId,
    required this.summary,
    required this.gameVersion,
    required this.loader,
    required this.loaderVersion,
    required this.fileCount,
    required this.totalBytes,
    required this.overrideFiles,
    required this.overrideBytes,
    required this.ignoredServerFiles,
  });

  final String name;
  final String versionId;
  final String summary;
  final String gameVersion;
  final String loader;
  final String loaderVersion;
  final int fileCount;
  final int totalBytes;
  final int overrideFiles;
  final int overrideBytes;
  final int ignoredServerFiles;

  static PackInspection? tryParse(Object? value) {
    if (value is! Map) return null;
    final name = value['name']?.toString();
    if (name == null || name.isEmpty) return null;
    return PackInspection(
      name: name,
      versionId: value['versionId']?.toString() ?? '',
      summary: value['summary']?.toString() ?? '',
      gameVersion: value['gameVersion']?.toString() ?? '',
      loader: value['loader']?.toString() ?? 'vanilla',
      loaderVersion: value['loaderVersion']?.toString() ?? '',
      fileCount: _int(value['fileCount']) ?? 0,
      totalBytes: _int(value['totalBytes']) ?? 0,
      overrideFiles: _int(value['overrideFiles']) ?? 0,
      overrideBytes: _int(value['overrideBytes']) ?? 0,
      ignoredServerFiles: _int(value['ignoredServerFiles']) ?? 0,
    );
  }

  static int? _int(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    if (value is String) return int.tryParse(value);
    return null;
  }
}
