/// Build provenance reported by `system.identity`.
///
/// Official releases embed the project's Microsoft OAuth client id at compile
/// time; the module only returns its SHA-256, so the About page can show an
/// "official build" badge without the id ever leaving CI secrets.
final class BuildIdentity {
  const BuildIdentity({
    required this.channel,
    required this.official,
    this.clientIdSha256,
    this.clientIdFingerprint,
  });

  /// `official`, `custom` or `unconfigured`.
  final String channel;

  /// True when the compiled-in client id matches the official one. This
  /// describes a build configuration, not proof that the binary is unmodified.
  final bool official;

  /// Lowercase SHA-256 of the compiled-in client id, or null when absent.
  final String? clientIdSha256;

  /// First 12 hex characters of [clientIdSha256], for compact display.
  final String? clientIdFingerprint;

  bool get hasClientId => clientIdSha256 != null && clientIdSha256!.isNotEmpty;

  static BuildIdentity? tryParse(Object? value) {
    if (value is! Map) return null;
    final channel = value['channel']?.toString();
    if (channel == null || channel.isEmpty) return null;
    final hash = value['clientIdSha256']?.toString();
    final fingerprint = value['clientIdFingerprint']?.toString();
    return BuildIdentity(
      channel: channel,
      official: value['official'] == true,
      clientIdSha256: hash == null || hash.isEmpty ? null : hash,
      clientIdFingerprint: fingerprint == null || fingerprint.isEmpty
          ? null
          : fingerprint,
    );
  }
}
