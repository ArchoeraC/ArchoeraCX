/// Superuser state reported by `system.elevation`.
///
/// The C++ core probes the process with the platform's own API (POSIX
/// effective uid, Win32 token elevation); Flutter only presents the decision
/// and never inspects the environment itself.
final class ProcessElevation {
  const ProcessElevation({required this.elevated, this.kind});

  /// True when this process holds root/administrator rights.
  final bool elevated;

  /// Backend label: `root` on POSIX, `administrator` on Windows. Null when the
  /// process is not elevated.
  final String? kind;

  static ProcessElevation? tryParse(Object? value) {
    if (value is! Map) return null;
    final kind = value['kind']?.toString();
    return ProcessElevation(
      elevated: value['elevated'] == true,
      kind: kind == null || kind.isEmpty ? null : kind,
    );
  }
}
