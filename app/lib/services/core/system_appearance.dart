import 'dart:ui' show Color;

/// System light/dark preference and accent color reported by the native core.
///
/// The core probes the OS-standard sources (XDG Desktop Portal on Linux,
/// registry values on Windows, AppKit on macOS) and pushes updates as domain
/// events. When no value is available, [theme] is `unknown` and the UI keeps
/// Flutter's platform-brightness fallback.
class SystemAppearance {
  const SystemAppearance({
    required this.theme,
    required this.accent,
    required this.source,
  });

  const SystemAppearance.unknown()
    : theme = 'unknown',
      accent = null,
      source = 'unknown';

  /// `light`, `dark` or `unknown`.
  final String theme;

  /// OS accent color, or null when the platform did not report one.
  final Color? accent;

  /// Native backend label (`xdg-portal`, `windows-registry`, `macos`, ...).
  final String source;

  bool? get dark => switch (theme) {
    'dark' => true,
    'light' => false,
    _ => null,
  };

  static SystemAppearance? tryParse(Object? value) {
    if (value is! Map) return null;
    final theme = value['theme']?.toString();
    if (theme != 'light' && theme != 'dark' && theme != 'unknown') {
      return null;
    }
    return SystemAppearance(
      theme: theme!,
      accent: _parseAccent(value['accent']),
      source: value['source']?.toString() ?? 'unknown',
    );
  }

  static Color? _parseAccent(Object? value) {
    if (value is! Map) return null;
    final r = _channel(value['r']);
    final g = _channel(value['g']);
    final b = _channel(value['b']);
    if (r == null || g == null || b == null) return null;
    return Color.fromARGB(255, r, g, b);
  }

  static int? _channel(Object? value) {
    final parsed = value is num ? value.toInt() : int.tryParse('$value');
    if (parsed == null) return null;
    return parsed.clamp(0, 255);
  }

  @override
  String toString() =>
      'SystemAppearance(theme: $theme, accent: $accent, source: $source)';
}
