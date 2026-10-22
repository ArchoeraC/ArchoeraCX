import 'package:flutter/material.dart';

import 'tokens.dart';

abstract final class AppTheme {
  static ThemeData get light => lightWith();
  static ThemeData get dark => darkWith();

  static ThemeData lightWith({
    Color? accent,
    bool globalTint = false,
    bool reduceMotion = false,
    VisualDensity density = VisualDensity.standard,
  }) {
    return _build(
      AppTokens.light.withAccent(
        accent ?? AppTokens.brandAccent,
        brightness: Brightness.light,
        tinted: globalTint,
        reduceMotion: reduceMotion,
      ),
      Brightness.light,
      density,
    );
  }

  static ThemeData darkWith({
    Color? accent,
    bool globalTint = false,
    bool reduceMotion = false,
    VisualDensity density = VisualDensity.standard,
  }) {
    return _build(
      AppTokens.dark.withAccent(
        accent ?? AppTokens.dark.accent,
        brightness: Brightness.dark,
        tinted: globalTint,
        reduceMotion: reduceMotion,
      ),
      Brightness.dark,
      density,
    );
  }

  static ThemeData _build(
    AppTokens tokens,
    Brightness brightness,
    VisualDensity density,
  ) {
    final scheme =
        ColorScheme.fromSeed(
          seedColor: tokens.accent,
          brightness: brightness,
        ).copyWith(
          primary: tokens.accent,
          onPrimary: tokens.onAccent,
          primaryContainer: tokens.accentSoft,
          onPrimaryContainer: tokens.textPrimary,
          secondary: tokens.accent,
          onSecondary: tokens.onAccent,
          secondaryContainer: tokens.surface2,
          onSecondaryContainer: tokens.textPrimary,
          surface: tokens.surface1,
          onSurface: tokens.textPrimary,
          surfaceContainerLowest: tokens.bg,
          surfaceContainerLow: tokens.surface1,
          surfaceContainer: tokens.surface2,
          surfaceContainerHigh: tokens.surface3,
          surfaceContainerHighest: tokens.surface3,
          outline: tokens.border,
          outlineVariant: tokens.border,
          error: tokens.danger,
          onError: tokens.onAccent,
        );

    final base = ThemeData(
      useMaterial3: true,
      fontFamily: 'NotoSansSC',
      colorScheme: scheme,
      scaffoldBackgroundColor: tokens.bg,
      visualDensity: density,
      splashFactory: NoSplash.splashFactory,
      extensions: [tokens],
    );

    final panelCut = BorderRadius.only(
      topRight: Radius.circular(tokens.bevelL),
      bottomLeft: Radius.circular(tokens.bevelS),
    );
    final dialogCut = BorderRadius.only(
      topLeft: Radius.circular(tokens.bevelL),
      bottomRight: Radius.circular(tokens.bevelS),
    );

    return base.copyWith(
      textTheme: _textTheme(base.textTheme, tokens),
      appBarTheme: AppBarTheme(
        backgroundColor: Colors.transparent,
        foregroundColor: tokens.textPrimary,
        elevation: 0,
        centerTitle: false,
      ),
      cardTheme: CardThemeData(
        elevation: 0,
        color: tokens.surface1,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(tokens.radiusL),
          side: BorderSide(color: tokens.border),
        ),
        margin: EdgeInsets.zero,
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: tokens.surface1,
        surfaceTintColor: Colors.transparent,
        shape: BeveledRectangleBorder(
          side: BorderSide(color: tokens.border),
          borderRadius: dialogCut,
        ),
      ),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: tokens.surface1,
        modalBackgroundColor: tokens.surface1,
        surfaceTintColor: Colors.transparent,
        shape: BeveledRectangleBorder(
          side: BorderSide(color: tokens.border),
          borderRadius: BorderRadius.only(
            topLeft: Radius.circular(tokens.bevelL),
            topRight: Radius.circular(tokens.bevelL),
          ),
        ),
      ),
      popupMenuTheme: PopupMenuThemeData(
        color: tokens.surface1,
        surfaceTintColor: Colors.transparent,
        shape: BeveledRectangleBorder(
          side: BorderSide(color: tokens.border),
          borderRadius: panelCut,
        ),
      ),
      snackBarTheme: SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        backgroundColor: tokens.textPrimary,
        contentTextStyle: TextStyle(color: tokens.bg),
        shape: BeveledRectangleBorder(
          side: BorderSide(color: tokens.border),
          borderRadius: panelCut,
        ),
      ),
      dividerTheme: DividerThemeData(
        color: tokens.border,
        thickness: 1,
        space: 1,
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: tokens.accent,
          foregroundColor: tokens.onAccent,
          disabledBackgroundColor: tokens.surface3,
          disabledForegroundColor: tokens.textTertiary,
          minimumSize: const Size(0, 40),
          padding: const EdgeInsets.symmetric(horizontal: 18),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(tokens.radiusM),
          ),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: tokens.textPrimary,
          side: BorderSide(color: tokens.border),
          minimumSize: const Size(0, 40),
          padding: const EdgeInsets.symmetric(horizontal: 18),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(tokens.radiusM),
          ),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: tokens.accent,
          minimumSize: const Size(0, 36),
          padding: const EdgeInsets.symmetric(horizontal: 12),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(tokens.radiusS),
          ),
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(
          foregroundColor: tokens.textSecondary,
          highlightColor: tokens.accentSoft,
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: tokens.surface2,
        hintStyle: TextStyle(color: tokens.textTertiary),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 14,
          vertical: 12,
        ),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(tokens.radiusM),
          borderSide: BorderSide.none,
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(tokens.radiusM),
          borderSide: BorderSide(color: tokens.border),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(tokens.radiusM),
          borderSide: BorderSide(color: tokens.accent),
        ),
      ),
      tooltipTheme: TooltipThemeData(
        decoration: BoxDecoration(
          color: tokens.textPrimary,
          borderRadius: BorderRadius.circular(tokens.radiusS),
        ),
        textStyle: TextStyle(color: tokens.bg, fontSize: 12),
        waitDuration: const Duration(milliseconds: 500),
      ),
      scrollbarTheme: ScrollbarThemeData(
        thickness: WidgetStateProperty.all(6),
        radius: const Radius.circular(999),
        thumbColor: WidgetStatePropertyAll(tokens.borderStrong),
      ),
      switchTheme: SwitchThemeData(
        thumbColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) return tokens.onAccent;
          return tokens.textTertiary;
        }),
        trackColor: WidgetStateProperty.resolveWith((states) {
          if (states.contains(WidgetState.selected)) return tokens.accent;
          return tokens.surface3;
        }),
      ),
    );
  }

  static TextTheme _textTheme(TextTheme base, AppTokens tokens) {
    return base.copyWith(
      displaySmall: base.displaySmall?.copyWith(
        fontSize: 32,
        height: 1.25,
        fontWeight: FontWeight.w500,
        letterSpacing: -0.8,
        color: tokens.textPrimary,
      ),
      headlineSmall: base.headlineSmall?.copyWith(
        fontSize: 24,
        height: 1.3,
        fontWeight: FontWeight.w500,
        letterSpacing: -0.4,
        color: tokens.textPrimary,
      ),
      titleLarge: base.titleLarge?.copyWith(
        fontSize: 20,
        height: 1.35,
        fontWeight: FontWeight.w500,
        color: tokens.textPrimary,
      ),
      titleMedium: base.titleMedium?.copyWith(
        fontSize: 16,
        height: 1.4,
        fontWeight: FontWeight.w500,
        color: tokens.textPrimary,
      ),
      titleSmall: base.titleSmall?.copyWith(
        fontSize: 14,
        height: 1.4,
        fontWeight: FontWeight.w500,
        color: tokens.textPrimary,
      ),
      bodyLarge: base.bodyLarge?.copyWith(
        fontSize: 15,
        height: 1.5,
        color: tokens.textPrimary,
      ),
      bodyMedium: base.bodyMedium?.copyWith(
        fontSize: 14,
        height: 1.55,
        color: tokens.textPrimary,
      ),
      bodySmall: base.bodySmall?.copyWith(
        fontSize: 12,
        height: 1.5,
        color: tokens.textSecondary,
      ),
      labelLarge: base.labelLarge?.copyWith(
        fontSize: 14,
        height: 1.3,
        fontWeight: FontWeight.w500,
        color: tokens.textPrimary,
      ),
      labelMedium: base.labelMedium?.copyWith(
        fontSize: 12,
        height: 1.3,
        fontWeight: FontWeight.w500,
        color: tokens.textSecondary,
      ),
      labelSmall: base.labelSmall?.copyWith(
        fontSize: 11,
        height: 1.3,
        fontWeight: FontWeight.w500,
        letterSpacing: 0.4,
        color: tokens.textTertiary,
      ),
    );
  }
}
