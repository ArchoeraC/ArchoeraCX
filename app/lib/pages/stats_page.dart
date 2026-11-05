import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../app/shell.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/game_instance.dart';
import '../services/core/stats/stats.dart';
import '../stores/stats.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_panel.dart';
import '../widgets/common/loader_mark.dart';

part 'stats/overview_cards.dart';
part 'stats/activity_panels.dart';
part 'stats/loader_panel.dart';
part 'stats/top_instances.dart';

/// Local play history, aggregated by C# into one snapshot.
///
/// The provider is watched only while the bottom bar selects this branch:
/// other pages stay offstage in the shell's indexed stack, and a background
/// `stats.get` would re-read the instance index at startup for nothing.
class StatsPage extends ConsumerWidget {
  const StatsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final active = AppBranchScope.maybeOf(context)?.index == AppBranch.stats;
    final statsAsync = active ? ref.watch(playStatsProvider) : null;
    final refreshing =
        statsAsync != null && statsAsync.isLoading && statsAsync.hasValue;

    final Widget body;
    if (statsAsync?.hasValue ?? false) {
      body = _StatsBody(stats: statsAsync!.requireValue);
    } else if (statsAsync?.hasError ?? false) {
      body = _StatsError(
        title: l10n.statsLoadFailed,
        message: l10n.statsLoadFailedHint,
        retryLabel: l10n.statsRetry,
        onRetry: () => ref.invalidate(playStatsProvider),
      );
    } else {
      body = const _StatsSkeleton();
    }

    return SingleChildScrollView(
      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppPageHeader(
            title: l10n.statsTitle,
            subtitle: l10n.statsSubtitle,
            actions: [
              IconButton(
                tooltip: l10n.statsRefresh,
                onPressed: !active || refreshing
                    ? null
                    : () => ref.invalidate(playStatsProvider),
                icon: refreshing
                    ? SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(
                          strokeWidth: 2,
                          color: tokens.accent,
                        ),
                      )
                    : const Icon(AppIcons.refresh, size: 18),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
          body,
          const SizedBox(height: AppSpacing.lg),
          Text(
            l10n.statsFootnote,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.textTertiary),
          ),
        ],
      ),
    );
  }
}

class _StatsBody extends StatelessWidget {
  const _StatsBody({required this.stats});

  final PlayStats stats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (stats.summary.totalSessions == 0) ...[
          _StatsEmptyNotice(l10n: l10n),
          const SizedBox(height: AppSpacing.md),
        ],
        _OverviewCards(stats: stats),
        const SizedBox(height: AppSpacing.lg),
        _ActivityPanels(stats: stats),
        const SizedBox(height: AppSpacing.lg),
        _RankingPanels(stats: stats),
      ],
    );
  }
}

class _StatsEmptyNotice extends StatelessWidget {
  const _StatsEmptyNotice({required this.l10n});

  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return AppPanel(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.md,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        children: [
          Icon(AppIcons.play, size: 18, color: tokens.accent),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: RichText(
              text: TextSpan(
                style: Theme.of(context).textTheme.bodyMedium
                    ?.copyWith(color: tokens.textSecondary),
                children: [
                  TextSpan(
                    text: '${l10n.statsEmptyTitle}  ',
                    style: TextStyle(
                      color: tokens.textPrimary,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  TextSpan(text: l10n.statsEmptyMessage),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _StatsError extends StatelessWidget {
  const _StatsError({
    required this.title,
    required this.message,
    required this.retryLabel,
    required this.onRetry,
  });

  final String title;
  final String message;
  final String retryLabel;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return AppPanel(
      padding: const EdgeInsets.all(AppSpacing.xl),
      child: Column(
        children: [
          Icon(AppIcons.warning, size: 26, color: tokens.warning),
          const SizedBox(height: AppSpacing.sm),
          Text(title, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: AppSpacing.xs),
          Text(
            message,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.textSecondary),
          ),
          const SizedBox(height: AppSpacing.md),
          OutlinedButton.icon(
            onPressed: onRetry,
            icon: const Icon(AppIcons.refresh, size: 16),
            label: Text(retryLabel),
          ),
        ],
      ),
    );
  }
}

class _StatsSkeleton extends StatelessWidget {
  const _StatsSkeleton();

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    Widget box(double height) => Container(
      height: height,
      decoration: BoxDecoration(
        color: tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusXL),
      ),
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        LayoutBuilder(
          builder: (context, constraints) {
            final columns = constraints.maxWidth >= 1100 ? 5 : 2;
            return Row(
              children: [
                for (var index = 0; index < columns; index++) ...[
                  if (index > 0) const SizedBox(width: AppSpacing.sm),
                  Expanded(child: box(108)),
                ],
              ],
            );
          },
        ),
        const SizedBox(height: AppSpacing.lg),
        box(236),
        const SizedBox(height: AppSpacing.lg),
        box(280),
      ],
    );
  }
}

/// Shared compact duration: `12h 5m` in English, `12小时 5分` in Chinese.
String _durationText(AppLocalizations l10n, int milliseconds) {
  final minutes = math.max(0, milliseconds) ~/ 60000;
  if (minutes < 60) return '$minutes${l10n.statsMinutesShort}';
  final hours = minutes ~/ 60;
  final rest = minutes % 60;
  if (rest == 0) return '$hours${l10n.statsHoursShort}';
  return '$hours${l10n.statsHoursShort} $rest${l10n.statsMinutesShort}';
}

/// Maps the wire loader slug to the enum used by [LoaderMark] and labels.
GameLoader _wireLoader(String value) {
  final slug = value.trim().toLowerCase();
  if (slug.isEmpty || slug == 'vanilla') return GameLoader.vanilla;
  return gameLoaderForSlug(slug) ?? GameLoader.vanilla;
}

/// Text style for tabular numbers so card values do not jitter.
TextStyle? _tabular(TextStyle? style) =>
    style?.copyWith(fontFeatures: const [FontFeature.tabularFigures()]);
