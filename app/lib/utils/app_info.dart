/// Build-time identity shared by the UI shell.
///
/// Keep this in sync with `pubspec.yaml` and the platform runners:
/// - Dart package: `archoera_cx`
/// - Linux application ID: `awa.archoera.archoera_cx`
/// - macOS bundle ID: `awa.archoera.archoeraCx` (Apple bundle IDs use
///   alphanumerics, hyphen and period; Flutter normalizes the underscore).
abstract final class AppInfo {
  static const name = 'ArchoeraCX';
  static const packageName = 'archoera_cx';
  static const applicationId = 'awa.archoera.archoera_cx';
  static const version = '0.1.0+1';
}
