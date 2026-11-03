import 'dart:async';
import 'dart:math';

import 'package:flutter/material.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/system/process_elevation.dart';
import '../stores/locale.dart';
import '../theme/app_theme.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_panel.dart';

/// Runs the superuser refusal gate and completes true only after the user
/// explicitly accepts the elevated start.
///
/// This is the only UI an elevated launch builds: the shell, tray and
/// single-instance claim are never created until the decision is made, so a
/// `sudo`/administrator start cannot touch instances or downloads by accident.
/// The caller exits the process when the result is false.
Future<bool> runElevationGate(ProcessElevation elevation) async {
  final locale = await resolveStartupLocale();
  final decision = Completer<bool>();

  runApp(
    _ElevationGateApp(elevation: elevation, locale: locale, onDecision: (proceed) {
      if (!decision.isCompleted) decision.complete(proceed);
    }),
  );

  return decision.future;
}

class _ElevationGateApp extends StatelessWidget {
  const _ElevationGateApp({
    required this.elevation,
    required this.locale,
    required this.onDecision,
  });

  final ProcessElevation elevation;
  final Locale locale;
  final ValueChanged<bool> onDecision;

  @override
  Widget build(BuildContext context) {
    // The gate always runs dark: it may appear in a dark room right after
    // `sudo`, and a full-white first frame reads as a flashbang. Neutral dark
    // surfaces keep the warning visible without the luminance jump.
    return MaterialApp(
      onGenerateTitle: (context) => AppLocalizations.of(context).appName,
      theme: AppTheme.darkWith(),
      darkTheme: AppTheme.darkWith(),
      themeMode: ThemeMode.dark,
      locale: locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      debugShowCheckedModeBanner: false,
      home: ElevationGatePage(elevation: elevation, onDecision: onDecision),
    );
  }
}

/// Copy the gate rotates through: one headline plus one confirm label is
/// drawn per launch.
///
/// Kept in one place so the picker and the rotation test cannot drift apart.
/// The body keeps the factual consequences; only the framing rotates, because
/// a user who starts the launcher elevated more than once would otherwise see
/// the exact same dialog and stop reading it.
({List<String> headlines, List<String> confirms}) elevationGateVariants(
  AppLocalizations l10n,
) => (
  headlines: <String>[
    l10n.elevationTitle,
    l10n.elevationTitleJustKnew,
    l10n.elevationTitleBegging,
    l10n.elevationTitleAgain,
    l10n.elevationTitleYourCall,
    l10n.elevationTitleThinkTwice,
  ],
  confirms: <String>[
    l10n.elevationContinue,
    l10n.elevationContinueBegging,
    l10n.elevationContinueInsist,
    l10n.elevationContinueStopNagging,
    l10n.elevationContinueMyProblem,
    l10n.elevationContinueJustOnce,
  ],
);

/// Headline and confirm label for one gate launch.
class _ElevationGateCopy {
  const _ElevationGateCopy({required this.title, required this.confirm});

  final String title;
  final String confirm;

  /// Picks one headline and one confirm label for this launch.
  static _ElevationGateCopy pick(AppLocalizations l10n, Random random) {
    final variants = elevationGateVariants(l10n);
    return _ElevationGateCopy(
      title: variants.headlines[random.nextInt(variants.headlines.length)],
      confirm: variants.confirms[random.nextInt(variants.confirms.length)],
    );
  }
}

/// The refusal surface itself, kept separate so tests can drive the two
/// decisions without owning the root widget.
class ElevationGatePage extends StatefulWidget {
  const ElevationGatePage({
    super.key,
    required this.elevation,
    required this.onDecision,
    this.random,
  });

  final ProcessElevation elevation;
  final ValueChanged<bool> onDecision;

  /// Test seam. Production leaves this null so every elevated start rolls a
  /// fresh [Random] and the copy differs between launches.
  final Random? random;

  @override
  State<ElevationGatePage> createState() => _ElevationGatePageState();
}

class _ElevationGatePageState extends State<ElevationGatePage> {
  _ElevationGateCopy? _copy;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // Localized strings arrive through an inherited widget, so the copy can
    // only be picked once dependencies exist. Picking exactly once keeps a
    // rebuild (resize, theme switch) from reshuffling the dialog.
    _copy ??= _ElevationGateCopy.pick(
      AppLocalizations.of(context),
      widget.random ?? Random(),
    );
  }

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final l10n = AppLocalizations.of(context);
    final copy = _copy!;
    final kind = widget.elevation.kind == 'administrator'
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
              // The gate is hand-built instead of showDialog, so it must apply
              // the shared angular dialog shape itself; a plain BoxDecoration
              // with circular radii would read as a generic rounded card.
              decoration: ShapeDecoration(
                color: tokens.surface1,
                shape: AppShapes.dialog(
                  tokens,
                  side: BorderSide(color: tokens.border),
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
                        alignment: Alignment.center,
                        // Same beveled proportions as AccountMark so warning
                        // badges match the angular mark used across the app.
                        decoration: ShapeDecoration(
                          color: tokens.danger.withValues(alpha: 0.12),
                          shape: BeveledRectangleBorder(
                            borderRadius: const BorderRadius.only(
                              topRight: Radius.circular(11.5),
                              bottomLeft: Radius.circular(7),
                            ),
                          ),
                        ),
                        child: Icon(AppIcons.warning, color: tokens.danger),
                      ),
                      const SizedBox(width: AppSpacing.sm),
                      Expanded(
                        child: Text(
                          copy.title,
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
                        onPressed: () => widget.onDecision(false),
                        child: Text(l10n.elevationExit),
                      ),
                      const SizedBox(width: AppSpacing.sm),
                      FilledButton(
                        style: FilledButton.styleFrom(
                          backgroundColor: tokens.danger,
                          foregroundColor: tokens.onAccent,
                        ),
                        onPressed: () => widget.onDecision(true),
                        child: Text(copy.confirm),
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
