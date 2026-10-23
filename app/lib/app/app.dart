import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../l10n/generated/app_localizations.dart';
import '../render/app_shaders.dart';
import '../stores/appearance.dart';
import '../stores/locale.dart';
import '../stores/providers.dart';
import '../widgets/common/toast.dart';
import '../theme/app_theme.dart';

class ArchoeraApp extends ConsumerWidget {
  const ArchoeraApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final router = ref.watch(routerProvider);
    final locale = ref.watch(localeProvider);
    final appearance = ref.watch(appearanceProvider);
    final systemAppearance = ref.watch(systemAppearanceProvider).value;

    final accent = appearance.accentMode == AccentMode.system
        ? systemAppearance?.accent
        : appearance.accentColor;
    final systemDark = systemAppearance?.dark;
    final themeMode =
        appearance.themeMode == ThemeMode.system && systemDark != null
        ? (systemDark ? ThemeMode.dark : ThemeMode.light)
        : appearance.themeMode;
    final density = switch (appearance.density) {
      UiDensity.compact => VisualDensity.compact,
      UiDensity.standard => VisualDensity.standard,
      UiDensity.comfortable => const VisualDensity(horizontal: 1, vertical: 1),
    };

    return MaterialApp.router(
      onGenerateTitle: (context) => AppLocalizations.of(context).appName,
      theme: AppTheme.lightWith(
        accent: accent,
        globalTint: appearance.globalTint,
        reduceMotion: appearance.reduceMotion,
        density: density,
      ),
      darkTheme: AppTheme.darkWith(
        accent: accent,
        globalTint: appearance.globalTint,
        reduceMotion: appearance.reduceMotion,
        density: density,
      ),
      themeMode: themeMode,
      routerConfig: router,
      // Shader programs are shared by the background, the page transition and
      // interactive panels; loading them above the router keeps a single
      // compiled copy and lets every consumer degrade to its plain fallback.
      builder: (context, child) => AppShaderScope(
        child: ToastOverlay(child: child ?? const SizedBox.shrink()),
      ),
      locale: locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      debugShowCheckedModeBanner: false,
    );
  }
}
