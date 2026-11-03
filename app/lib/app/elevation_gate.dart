import 'dart:async';

import 'package:flutter/material.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/system/process_elevation.dart';
import '../stores/locale.dart';
import '../theme/app_theme.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';

/// Runs the superuser refusal gate and completes true only after the user
/// explicitly accepts the elevated start.
///
/// This is the only UI an elevated launch builds: the shell, tray and
/// single-instance claim are never created until the decision is made, so a
/// `sudo`/administrator start cannot touch instances or downloads by accident.
/// The caller exits the process when the result is false.
Future<bool> runElevationGate(ProcessElevation elevation) async {
  final locale = await resolveStartupLocale();
  final brightness =
      WidgetsBinding.instance.platformDispatcher.platformBrightness;
  final decision = Completer<bool>();

  runApp(
    _ElevationGateApp(
      elevation: elevation,
      locale: locale,
      brightness: brightness,
      onDecision: (proceed) {
        if (!decision.isCompleted) decision.complete(proceed);
      },
    ),
  );

  return decision.future;
}

class _ElevationGateApp extends StatelessWidget {
  const _ElevationGateApp({
    required this.elevation,
    required this.locale,
    required this.brightness,
    required this.onDecision,
  });

  final ProcessElevation elevation;
  final Locale locale;
  final Brightness brightness;
  final ValueChanged<bool> onDecision;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      onGenerateTitle: (context) => AppLocalizations.of(context).appName,
      theme: AppTheme.lightWith(),
      darkTheme: AppTheme.darkWith(),
      themeMode: brightness == Brightness.dark
          ? ThemeMode.dark
          : ThemeMode.light,
      locale: locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      debugShowCheckedModeBanner: false,
      home: ElevationGatePage(elevation: elevation, onDecision: onDecision),
    );
  }
}

/// The refusal surface itself, kept separate so tests can drive the two
/// decisions without owning the root widget.
class ElevationGatePage extends StatelessWidget {
  const ElevationGatePage({
    super.key,
    required this.elevation,
    required this.onDecision,
  });

  final ProcessElevation elevation;
  final ValueChanged<bool> onDecision;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final l10n = AppLocalizations.of(context);
    final kind = elevation.kind == 'administrator'
        ? l10n.elevationKindAdministrator
        : l10n.elevationKindRoot;

    return Scaffold(
      backgroundColor: tokens.bg,
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 560),
            child: Container(
              padding: const EdgeInsets.all(AppSpacing.lg),
              decoration: BoxDecoration(
                color: tokens.surface1,
                border: Border.all(color: tokens.border),
                borderRadius: BorderRadius.only(
                  topLeft: Radius.circular(tokens.bevelL),
                  bottomRight: Radius.circular(tokens.bevelS),
                ),
              ),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Container(
                        width: 44,
                        height: 44,
                        decoration: BoxDecoration(
                          color: tokens.danger.withValues(alpha: 0.12),
                          borderRadius: BorderRadius.circular(tokens.radiusM),
                        ),
                        child: Icon(AppIcons.warning, color: tokens.danger),
                      ),
                      const SizedBox(width: AppSpacing.sm),
                      Expanded(
                        child: Text(
                          l10n.elevationTitle,
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: AppSpacing.md),
                  Text(
                    l10n.elevationBody(kind),
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                  const SizedBox(height: AppSpacing.lg),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.end,
                    children: [
                      OutlinedButton(
                        onPressed: () => onDecision(false),
                        child: Text(l10n.elevationExit),
                      ),
                      const SizedBox(width: AppSpacing.sm),
                      FilledButton(
                        style: FilledButton.styleFrom(
                          backgroundColor: tokens.danger,
                          foregroundColor: tokens.onAccent,
                        ),
                        onPressed: () => onDecision(true),
                        child: Text(l10n.elevationContinue),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
