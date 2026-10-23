import 'dart:convert';

import '../core_transport.dart';
import 'java_runtime_plan.dart';
import 'java_scan.dart';

/// Java domain binding for the native core: `java.scan`, `java.select` and
/// `java.runtime.plan`.
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
