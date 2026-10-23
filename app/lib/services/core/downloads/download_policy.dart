/// Local download-risk gate returned by `download.policy`.
final class DownloadPolicyState {
  const DownloadPolicyState({
    required this.enabled,
    required this.reason,
    required this.configPath,
    required this.noticeVersion,
    this.downloadLevel,
    this.missingFields = const <String>[],
  });

  final bool enabled;
  final String reason;
  final String configPath;
  final int noticeVersion;
  final String? downloadLevel;
  final List<String> missingFields;

  bool get requiresConfiguration => !enabled && reason != 'ENABLED';

  static DownloadPolicyState? tryParse(Object? value) {
    if (value is! Map) return null;
    final reason = value['reason']?.toString();
    final configPath = value['configPath']?.toString();
    if (reason == null || configPath == null) return null;
    return DownloadPolicyState(
      enabled: value['enabled'] == true,
      reason: reason,
      configPath: configPath,
      noticeVersion: _int(value['noticeVersion']) ?? 1,
      downloadLevel: value['downloadLevel']?.toString(),
      missingFields: (value['missingFields'] is List)
          ? (value['missingFields'] as List)
                .map((item) => item.toString())
                .toList(growable: false)
          : const <String>[],
    );
  }

  static int? _int(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    return int.tryParse(value?.toString() ?? '');
  }
}
