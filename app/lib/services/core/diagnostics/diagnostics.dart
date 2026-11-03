import 'dart:convert';

import '../core_transport.dart';
import 'crash_analysis.dart';

/// Diagnostics domain binding: analyze the artifacts of a finished launch.
mixin CoreClientDiagnostics on CoreTransport {
  /// Analyzes the newest launch log (or [logPath]) plus `latest.log`,
  /// `crash-reports/` and `hs_err_pid*.log`.
  Future<CrashAnalysis> analyzeCrash(
    String instanceId, {
    String? logPath,
    int? exitCode,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'diagnostics.analyze';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'instanceId': instanceId,
        'logPath': ?logPath,
        'exitCode': ?exitCode,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final analysis = CrashAnalysis.tryParse(data['analysis']);
    if (analysis == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'an invalid analysis');
    }
    return analysis;
  }

  /// Returns the stored analysis from the last `diagnostics.analyze`, if any.
  Future<CrashAnalysis?> latestCrash(
    String instanceId, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'diagnostics.latest';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'instanceId': instanceId}),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return CrashAnalysis.tryParse(data['analysis']);
  }
}
