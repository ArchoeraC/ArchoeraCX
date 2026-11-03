/// A single explainable finding from the native crash analyzer.
class CrashFinding {
  const CrashFinding({
    required this.ruleId,
    required this.severity,
    required this.confidence,
    required this.args,
    required this.evidence,
  });

  final String ruleId;
  final String severity;
  final int confidence;
  final Map<String, String> args;
  final List<String> evidence;

  static CrashFinding? tryParse(Object? value) {
    if (value is! Map) return null;
    final ruleId = value['ruleId']?.toString();
    if (ruleId == null || ruleId.isEmpty) return null;

    final args = <String, String>{};
    final rawArgs = value['args'];
    if (rawArgs is Map) {
      for (final entry in rawArgs.entries) {
        final text = entry.value?.toString();
        if (text != null) args[entry.key.toString()] = text;
      }
    }

    final evidence = <String>[];
    final rawEvidence = value['evidence'];
    if (rawEvidence is List) {
      for (final line in rawEvidence) {
        final text = line?.toString();
        if (text != null && text.isNotEmpty) evidence.add(text);
      }
    }

    return CrashFinding(
      ruleId: ruleId,
      severity: value['severity']?.toString() ?? 'crash',
      confidence: _asInt(value['confidence']) ?? 0,
      args: args,
      evidence: evidence,
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

/// Result of analyzing one launch's logs and crash artifacts.
class CrashAnalysis {
  const CrashAnalysis({
    required this.outcome,
    required this.exitCode,
    required this.logPath,
    required this.crashReportPath,
    required this.hsErrPath,
    required this.analyzedAt,
    required this.findings,
  });

  final String outcome;
  final int? exitCode;
  final String? logPath;
  final String? crashReportPath;
  final String? hsErrPath;
  final DateTime? analyzedAt;
  final List<CrashFinding> findings;

  bool get abnormal => outcome != 'clean';

  CrashFinding? get primary => findings.isEmpty ? null : findings.first;

  static CrashAnalysis? tryParse(Object? value) {
    if (value is! Map) return null;
    final outcome = value['outcome']?.toString();
    if (outcome == null || outcome.isEmpty) return null;

    final findings = <CrashFinding>[];
    final rawFindings = value['findings'];
    if (rawFindings is List) {
      for (final item in rawFindings) {
        final finding = CrashFinding.tryParse(item);
        if (finding != null) findings.add(finding);
      }
    }

    return CrashAnalysis(
      outcome: outcome,
      exitCode: _asInt(value['exitCode']),
      logPath: _stringOrNull(value['logPath']),
      crashReportPath: _stringOrNull(value['crashReportPath']),
      hsErrPath: _stringOrNull(value['hsErrPath']),
      analyzedAt: DateTime.tryParse(value['analyzedAt']?.toString() ?? ''),
      findings: findings,
    );
  }
}
