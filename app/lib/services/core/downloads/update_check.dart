/// Result of `update.check`: the newest GitHub release compared to this build.
class UpdateCheckResult {
  const UpdateCheckResult({
    required this.current,
    required this.latest,
    required this.hasUpdate,
    this.url,
    this.name,
    this.publishedAt,
    this.notes,
  });

  /// Version of the running build, e.g. `0.0.1+1`.
  final String current;

  /// Newest published release tag without the leading `v`.
  final String latest;

  final bool hasUpdate;

  /// Release page, opened through `system.openUrl` when the user asks.
  final String? url;
  final String? name;
  final DateTime? publishedAt;
  final String? notes;

  static UpdateCheckResult? tryParse(Object? value) {
    if (value is! Map) return null;
    final current = value['current']?.toString();
    final latest = value['latest']?.toString();
    if (current == null || latest == null) return null;
    return UpdateCheckResult(
      current: current,
      latest: latest,
      hasUpdate: value['hasUpdate'] == true,
      url: value['url']?.toString(),
      name: value['name']?.toString(),
      publishedAt: DateTime.tryParse(value['publishedAt']?.toString() ?? ''),
      notes: value['notes']?.toString(),
    );
  }
}
