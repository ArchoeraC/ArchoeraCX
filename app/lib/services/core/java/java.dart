import 'dart:convert';

import '../core_transport.dart';
import 'java_distribution.dart';
import 'java_runtime_plan.dart';
import 'java_scan.dart';

export 'java_scan.dart' show JavaRuntime;

/// Java domain binding for the native core: `java.scan`, `java.select`,
/// `java.runtime.plan` and the `java.distributions*` archive catalogue.
mixin CoreClientJava on CoreTransport {
  /// Submit `java.scan` and wait for its terminal task-state event.
  ///
  /// [roots] and [executables] are optional manual paths forwarded to the
  /// business module; [includeDefaults] controls whether its built-in
  /// discovery locations are also probed. The core routes the call to the C#
  /// business module. An absent or unsupported module surfaces as
  /// [CoreException] with `E_UNSUPPORTED`, `E_NOT_FOUND` or a module-specific
  /// code; callers can render that gracefully. Like `pingAsync`, completion is
  /// driven only by the event pump. No timer or polling loop is involved.
  Future<JavaScanResult> scanJava({
    List<String> roots = const <String>[],
    List<String> executables = const <String>[],
    bool includeDefaults = true,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'java.scan';
    final paramsJson = jsonEncode(<String, Object?>{
      'roots': roots,
      'executables': executables,
      'includeDefaults': includeDefaults,
    });
    final jsonText = await runAsync(
      methodName,
      paramsJson: paramsJson,
      onProgress: onProgress,
      label: label,
    );
    return _parseJavaScan(this, jsonText, fallbackMethod: methodName);
  }

  /// Ask C# to select a Java runtime for a game version. The settings decide
  /// whether the manual path or automatic scan is used.
  Future<JavaRuntime> selectJava({
    String? gameVersion,
    int? requiredMajor,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'java.select';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'gameVersion': ?gameVersion,
        'requiredMajor': ?requiredMajor,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final runtime = JavaRuntime.tryParse(data);
    if (runtime == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'no Java runtime');
    }
    return runtime;
  }

  /// Static list of JDK vendors plus the current host triple.
  Future<JavaDistributionCatalog> javaDistributions() async {
    const methodName = 'java.distributions';
    final jsonText = await runAsync(methodName, paramsJson: '{}');
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final platform = JavaDistributionPlatformInfo.tryParse(data['platform']);
    if (platform == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'no platform');
    }

    final platforms = <JavaDistributionPlatformInfo>[];
    for (final value in asList(data['platforms'])) {
      final item = JavaDistributionPlatformInfo.tryParse(value);
      if (item != null) platforms.add(item);
    }

    final providers = <JavaDistributionProviderInfo>[];
    for (final value in asList(data['providers'])) {
      final provider = JavaDistributionProviderInfo.tryParse(value);
      if (provider != null) providers.add(provider);
    }

    return JavaDistributionCatalog(
      platform: platform,
      platforms: platforms.isEmpty
          ? List<JavaDistributionPlatformInfo>.unmodifiable(
              <JavaDistributionPlatformInfo>[
                JavaDistributionPlatformInfo(
                  os: platform.os,
                  arch: platform.arch,
                  archive: platform.archive,
                  current: true,
                ),
              ],
            )
          : List.unmodifiable(platforms),
      providers: List.unmodifiable(providers),
      recommendedMajor: asInt(data['recommendedMajor']) ?? 21,
      defaultRoot: data['defaultRoot']?.toString() ?? '',
    );
  }

  Map<String, Object?> _platformParams(JavaDistributionPlatformInfo? platform) {
    if (platform == null) return const <String, Object?>{};
    return <String, Object?>{
      'os': platform.os,
      'arch': platform.arch,
      'archive': platform.archive,
    };
  }

  /// Feature releases a vendor publishes for this host.
  Future<JavaDistributionMajors> javaDistributionMajors({
    required String provider,
    JavaDistributionPlatformInfo? platform,
  }) async {
    const methodName = 'java.distributions.majors';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': provider,
        ..._platformParams(platform),
      }),
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final majors = <int>[];
    for (final value in asList(data['majors'])) {
      final major = asInt(value);
      if (major != null) majors.add(major);
    }

    return JavaDistributionMajors(
      provider: data['provider']?.toString() ?? provider,
      majors: List.unmodifiable(majors),
      recommendedMajor: asInt(data['recommendedMajor']) ?? 21,
    );
  }

  /// Every published build for one feature release, newest first.
  Future<JavaDistributionVersions> javaDistributionVersions({
    required String provider,
    required int major,
    JavaDistributionPlatformInfo? platform,
  }) async {
    const methodName = 'java.distributions.versions';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': provider,
        'major': major,
        ..._platformParams(platform),
      }),
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final versions = <JavaDistributionVersion>[];
    for (final value in asList(data['versions'])) {
      final version = _parseDistributionVersion(value);
      if (version != null) versions.add(version);
    }

    return JavaDistributionVersions(
      provider: data['provider']?.toString() ?? provider,
      major: asInt(data['major']) ?? major,
      recommendedMajor: asInt(data['recommendedMajor']) ?? 21,
      versions: List.unmodifiable(versions),
    );
  }

  /// CVE ids that affect one resolved build. A missing feed is returned as an
  /// unavailable report rather than an exception, so the page can still show
  /// the build list and let the user install with a visible warning.
  Future<JavaCveReport> javaDistributionAdvisories({
    required String provider,
    required int major,
    required String versionId,
    JavaDistributionPlatformInfo? platform,
  }) async {
    const methodName = 'java.distributions.advisories';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': provider,
        'major': major,
        'versionId': versionId,
        ..._platformParams(platform),
      }),
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final report = JavaCveReport.tryParse(decoded['data']);
    if (report == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'no CVE report');
    }

    return report;
  }

  /// Downloads and extracts a JDK archive into [directory] (or the managed
  /// runtime root when omitted).
  ///
  /// The task is driven by the event pump; [onProgress] receives the archive
  /// byte counts, [onRate] the executor's current bytes/second and
  /// [onDetailedProgress] carries the extraction message. [onTaskStarted]
  /// exposes the native task id so the page can cancel the download without
  /// leaving for the task center.
  Future<JavaDistributionInstallResult> installJavaDistribution({
    required String provider,
    required String versionId,
    String? directory,
    bool autoExtract = true,
    bool autoDelete = true,
    bool allowOutdated = false,
    bool acknowledgeCves = false,
    JavaDistributionPlatformInfo? platform,
    bool allowIncompatible = false,
    void Function(int done, int total)? onProgress,
    void Function(int bytesPerSecond)? onRate,
    CoreDetailedProgress? onDetailedProgress,
    void Function(int taskId)? onTaskStarted,
    String? label,
  }) async {
    const methodName = 'java.distributions.install';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'provider': provider,
        'versionId': versionId,
        'directory': ?directory,
        'autoExtract': autoExtract,
        'autoDelete': autoDelete,
        'allowOutdated': allowOutdated,
        'acknowledgeCves': acknowledgeCves,
        'allowIncompatible': allowIncompatible,
        ..._platformParams(platform),
      }),
      onProgress: onProgress,
      onRate: onRate,
      onDetailedProgress: onDetailedProgress,
      onTaskStarted: onTaskStarted,
      label: label,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final result = JavaDistributionInstallResult.tryParse(decoded['data']);
    if (result == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'no installed JDK');
    }

    return result;
  }

  /// Ask the C# module for the Mojang Java runtime download plan.
  ///
  /// Nothing is downloaded here: the returned plan is executed through
  /// `executeDownload` and lands under `<root>/runtime/java-<major>`.
  Future<JavaRuntimePlan> planJavaRuntime({
    required int major,
    String? root,
    void Function(int done, int total)? onProgress,
    String? label,
  }) async {
    const methodName = 'java.runtime.plan';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'major': major, 'root': ?root}),
      onProgress: onProgress,
      label: label,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final plan = JavaRuntimePlan.tryParse(decoded['data']);
    if (plan == null || plan.major <= 0 || plan.plan.isEmpty) {
      throw CoreTransport.invalidEnvelope(
        methodName,
        'an incomplete runtime plan',
      );
    }
    return plan;
  }
}

JavaDistributionVersion? _parseDistributionVersion(Object? value) {
  if (value is! Map) return null;
  final id = value['id']?.toString();
  if (id == null) return null;
  final major = value['major'];
  final size = value['size'];
  return JavaDistributionVersion(
    id: id,
    major: major is num ? major.toInt() : int.tryParse('$major') ?? 0,
    version: value['version']?.toString() ?? id,
    archive: value['archive']?.toString() ?? '',
    size: size is num ? size.toInt() : int.tryParse('$size') ?? 0,
    sha256: value['sha256']?.toString(),
    lts: value['lts'] == true,
    recommended: value['recommended'] == true,
    latestPatch: value['latestPatch'] != false,
    releasedAt: DateTime.tryParse(value['releasedAt']?.toString() ?? ''),
  );
}

JavaScanResult _parseJavaScan(
  CoreTransport transport,
  String jsonText, {
  required String fallbackMethod,
}) {
  final decoded = transport.decodeEnvelope(
    jsonText,
    fallbackMethod: fallbackMethod,
  );

  final dataValue = decoded['data'];
  if (dataValue is! Map<String, Object?>) {
    throw CoreTransport.invalidEnvelope(fallbackMethod, 'no data object');
  }

  final itemsValue = dataValue['items'];
  if (itemsValue is! List) {
    throw CoreTransport.invalidEnvelope(fallbackMethod, 'no items array');
  }

  final items = <JavaRuntime>[];
  for (final value in itemsValue) {
    final runtime = JavaRuntime.tryParse(value);
    if (runtime != null) items.add(runtime);
  }

  final metaValue = decoded['meta'];
  final meta = metaValue is Map<String, Object?>
      ? metaValue
      : const <String, Object?>{};

  return JavaScanResult(
    items: List<JavaRuntime>.unmodifiable(items),
    count: transport.asInt(dataValue['count']) ?? items.length,
    rawJson: decoded,
    schemaVersion: meta['schemaVersion'] is int
        ? meta['schemaVersion'] as int
        : null,
    requestId: meta['requestId']?.toString(),
  );
}
