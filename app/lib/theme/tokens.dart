import 'package:flutter/material.dart';

/// ArchoeraCX design tokens.
///
/// Pages must read colors/radii/motion from here (or from [ColorScheme]),
/// never hard-code visual values.
@immutable
class AppTokens extends ThemeExtension<AppTokens> {
  const AppTokens({
    required this.accent,
    required this.accentHover,
    required this.accentPressed,
    required this.accentSoft,
    required this.onAccent,
    required this.bg,
    required this.surface1,
    required this.surface2,
    required this.surface3,
    required this.textPrimary,
    required this.textSecondary,
    required this.textTertiary,
    required this.danger,
    required this.success,
    required this.warning,
    required this.border,
    required this.borderStrong,
    required this.overlay,
    required this.radiusS,
    required this.radiusM,
    required this.radiusL,
    required this.radiusXL,
    required this.bevelS,
    required this.bevelM,
    required this.bevelL,
    required this.fast,
    required this.normal,
    required this.slow,
    required this.curve,
  });

  final Color accent;
  final Color accentHover;
  final Color accentPressed;
  final Color accentSoft;
  final Color onAccent;

  final Color bg;
  final Color surface1;
  final Color surface2;
  final Color surface3;

  final Color textPrimary;
  final Color textSecondary;
  final Color textTertiary;

  final Color danger;
  final Color success;
  final Color warning;

  final Color border;
  final Color borderStrong;
  final Color overlay;

  final double radiusS;
  final double radiusM;
  final double radiusL;
  final double radiusXL;

  /// Straight-cut corner sizes for the angular panel language.
  final double bevelS;
  final double bevelM;
  final double bevelL;

  final Duration fast;
  final Duration normal;
  final Duration slow;
  final Curve curve;

  static const light = AppTokens(
    accent: Color(0xFF4F7CFF),
    accentHover: Color(0xFF6B90FF),
    accentPressed: Color(0xFF3D66E0),
    accentSoft: Color(0x1F4F7CFF),
    onAccent: Color(0xFFFFFFFF),
    bg: Color(0xFFF7F8FB),
    surface1: Color(0xFFFFFFFF),
    surface2: Color(0xFFF1F3F8),
    surface3: Color(0xFFE8EBF3),
    textPrimary: Color(0xFF111827),
    textSecondary: Color(0xFF4B5563),
    textTertiary: Color(0xFF8A94A6),
    danger: Color(0xFFD64545),
    success: Color(0xFF2F9E6B),
    warning: Color(0xFFB7791F),
    border: Color(0xFFE3E7EF),
    borderStrong: Color(0xFFCFD6E4),
    overlay: Color(0x66000000),
    radiusS: 2,
    radiusM: 4,
    radiusL: 4,
    radiusXL: 6,
    bevelS: 8,
    bevelM: 14,
    bevelL: 22,
    fast: Duration(milliseconds: 120),
    normal: Duration(milliseconds: 180),
    slow: Duration(milliseconds: 240),
    curve: Curves.easeOutCubic,
  );

  static const dark = AppTokens(
    accent: Color(0xFF7A9BFF),
    accentHover: Color(0xFF94AEFF),
    accentPressed: Color(0xFF5F82E6),
    accentSoft: Color(0x267A9BFF),
    onAccent: Color(0xFF0B1020),
    bg: Color(0xFF10141B),
    surface1: Color(0xFF171D26),
    surface2: Color(0xFF1E2632),
    surface3: Color(0xFF27313F),
    textPrimary: Color(0xFFF2F5FA),
    textSecondary: Color(0xFFAAB5C6),
    textTertiary: Color(0xFF738094),
    danger: Color(0xFFFF7A7A),
    success: Color(0xFF54D39A),
    warning: Color(0xFFF0B45A),
    border: Color(0xFF2A3442),
    borderStrong: Color(0xFF3A4759),
    overlay: Color(0xA6000000),
    radiusS: 2,
    radiusM: 4,
    radiusL: 4,
    radiusXL: 6,
    bevelS: 8,
    bevelM: 14,
    bevelL: 22,
    fast: Duration(milliseconds: 120),
    normal: Duration(milliseconds: 180),
    slow: Duration(milliseconds: 240),
    curve: Curves.easeOutCubic,
  );

  @override
  AppTokens copyWith({
    Color? accent,
    Color? accentHover,
    Color? accentPressed,
    Color? accentSoft,
    Color? onAccent,
    Color? bg,
    Color? surface1,
    Color? surface2,
    Color? surface3,
    Color? textPrimary,
    Color? textSecondary,
    Color? textTertiary,
    Color? danger,
    Color? success,
    Color? warning,
    Color? border,
    Color? borderStrong,
    Color? overlay,
    double? radiusS,
    double? radiusM,
    double? radiusL,
    double? radiusXL,
    double? bevelS,
    double? bevelM,
    double? bevelL,
    Duration? fast,
    Duration? normal,
    Duration? slow,
    Curve? curve,
  }) {
    return AppTokens(
      accent: accent ?? this.accent,
      accentHover: accentHover ?? this.accentHover,
      accentPressed: accentPressed ?? this.accentPressed,
      accentSoft: accentSoft ?? this.accentSoft,
      onAccent: onAccent ?? this.onAccent,
      bg: bg ?? this.bg,
      surface1: surface1 ?? this.surface1,
      surface2: surface2 ?? this.surface2,
      surface3: surface3 ?? this.surface3,
      textPrimary: textPrimary ?? this.textPrimary,
      textSecondary: textSecondary ?? this.textSecondary,
      textTertiary: textTertiary ?? this.textTertiary,
      danger: danger ?? this.danger,
      success: success ?? this.success,
      warning: warning ?? this.warning,
      border: border ?? this.border,
      borderStrong: borderStrong ?? this.borderStrong,
      overlay: overlay ?? this.overlay,
      radiusS: radiusS ?? this.radiusS,
      radiusM: radiusM ?? this.radiusM,
      radiusL: radiusL ?? this.radiusL,
      radiusXL: radiusXL ?? this.radiusXL,
      bevelS: bevelS ?? this.bevelS,
      bevelM: bevelM ?? this.bevelM,
      bevelL: bevelL ?? this.bevelL,
      fast: fast ?? this.fast,
      normal: normal ?? this.normal,
      slow: slow ?? this.slow,
      curve: curve ?? this.curve,
    );
  }

  @override
  AppTokens lerp(covariant AppTokens? other, double t) {
    if (other == null) return this;

    Duration lerpDuration(Duration a, Duration b) => Duration(
      microseconds:
          (a.inMicroseconds + (b.inMicroseconds - a.inMicroseconds) * t)
              .round(),
    );

    return AppTokens(
      accent: Color.lerp(accent, other.accent, t)!,
      accentHover: Color.lerp(accentHover, other.accentHover, t)!,
      accentPressed: Color.lerp(accentPressed, other.accentPressed, t)!,
      accentSoft: Color.lerp(accentSoft, other.accentSoft, t)!,
      onAccent: Color.lerp(onAccent, other.onAccent, t)!,
      bg: Color.lerp(bg, other.bg, t)!,
      surface1: Color.lerp(surface1, other.surface1, t)!,
      surface2: Color.lerp(surface2, other.surface2, t)!,
      surface3: Color.lerp(surface3, other.surface3, t)!,
      textPrimary: Color.lerp(textPrimary, other.textPrimary, t)!,
      textSecondary: Color.lerp(textSecondary, other.textSecondary, t)!,
      textTertiary: Color.lerp(textTertiary, other.textTertiary, t)!,
      danger: Color.lerp(danger, other.danger, t)!,
      success: Color.lerp(success, other.success, t)!,
      warning: Color.lerp(warning, other.warning, t)!,
      border: Color.lerp(border, other.border, t)!,
      borderStrong: Color.lerp(borderStrong, other.borderStrong, t)!,
      overlay: Color.lerp(overlay, other.overlay, t)!,
      radiusS: radiusS + (other.radiusS - radiusS) * t,
      radiusM: radiusM + (other.radiusM - radiusM) * t,
      radiusL: radiusL + (other.radiusL - radiusL) * t,
      radiusXL: radiusXL + (other.radiusXL - radiusXL) * t,
      bevelS: bevelS + (other.bevelS - bevelS) * t,
      bevelM: bevelM + (other.bevelM - bevelM) * t,
      bevelL: bevelL + (other.bevelL - bevelL) * t,
      fast: lerpDuration(fast, other.fast),
      normal: lerpDuration(normal, other.normal),
      slow: lerpDuration(slow, other.slow),
      curve: t < 0.5 ? curve : other.curve,
    );
  }
}

extension AppTokensContext on BuildContext {
  AppTokens get tokens => Theme.of(this).extension<AppTokens>()!;
}

/// Spacing scale. Keep layout rhythm consistent across pages.
abstract final class AppSpacing {
  static const xxs = 4.0;
  static const xs = 8.0;
  static const sm = 12.0;
  static const md = 16.0;
  static const lg = 24.0;
  static const xl = 32.0;
  static const xxl = 48.0;
  static const xxxl = 64.0;
}

/// Shell dimensions.
abstract final class AppLayout {
  static const titleBarHeight = 56.0;
  static const bottomBarHeight = 60.0;
  static const homeBarHeight = 46.0;
  static const contentMaxWidth = 1400.0;
}
