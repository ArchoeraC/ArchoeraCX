part of '../stats_page.dart';

class _OverviewCards extends StatelessWidget {
  const _OverviewCards({required this.stats});

  final PlayStats stats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final summary = stats.summary;
    final total = _splitDuration(l10n, summary.totalPlayMs);
    final week = _splitDuration(l10n, summary.weekPlayMs);
    final today = _durationText(l10n, summary.todayPlayMs);
    final lastWeek = _durationText(l10n, summary.lastWeekPlayMs);

    final cards = <_OverviewCardData>[
      _OverviewCardData(
        icon: AppIcons.time,
        value: total.$1,
        unit: total.$2,
        value2: total.$3,
        unit2: total.$4,
        label: l10n.statsTotalPlaytime,
        detail: l10n.statsTodayPlayed(today),
      ),
      _OverviewCardData(
        icon: AppIcons.stats,
        value: week.$1,
        unit: week.$2,
        value2: week.$3,
        unit2: week.$4,
        label: l10n.statsThisWeek,
        detail: l10n.statsLastWeek(lastWeek),
      ),
      _OverviewCardData(
        icon: AppIcons.play,
        value: '${summary.totalSessions}',
        label: l10n.statsSessions,
        detail: l10n.statsWeekSessionCount(summary.weekSessions),
      ),
      _OverviewCardData(
        icon: AppIcons.quick,
        value: '${summary.streakDays}',
        unit: l10n.statsDaysShort,
        label: l10n.statsStreak,
        detail: l10n.statsActiveDaysCount(summary.activeDays),
      ),
      _OverviewCardData(
        icon: AppIcons.instances,
        value: '${summary.instanceCount}',
        label: l10n.statsInstances,
        detail: l10n.statsModTotal(summary.modCount),
      ),
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = switch (constraints.maxWidth) {
          >= 1180 => 5,
          >= 820 => 3,
          _ => 2,
        };
        final spacing = AppSpacing.sm;
        final width =
            (constraints.maxWidth - spacing * (columns - 1)) / columns;
        return Wrap(
          spacing: spacing,
          runSpacing: spacing,
          children: [
            for (final card in cards)
              SizedBox(
                width: width,
                child: _OverviewCard(data: card),
              ),
          ],
        );
      },
    );
  }

  (String, String?, String?, String?) _splitDuration(
    AppLocalizations l10n,
    int milliseconds,
  ) {
    final minutes = math.max(0, milliseconds) ~/ 60000;
    if (minutes < 60) return ('$minutes', l10n.statsMinutesShort, null, null);
    final hours = minutes ~/ 60;
    final rest = minutes % 60;
    return (
      '$hours',
      l10n.statsHoursShort,
      rest == 0 ? null : '$rest',
      rest == 0 ? null : l10n.statsMinutesShort,
    );
  }
}

class _OverviewCardData {
  const _OverviewCardData({
    required this.icon,
    required this.value,
    required this.label,
    this.unit,
    this.value2,
    this.unit2,
    this.detail,
  });

  final IconData icon;
  final String value;
  final String label;
  final String? unit;
  final String? value2;
  final String? unit2;
  final String? detail;
}

class _OverviewCard extends StatelessWidget {
  const _OverviewCard({required this.data});

  final _OverviewCardData data;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return AppPanel(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: SizedBox(
        height: 84,
        child: Stack(
          clipBehavior: Clip.none,
          children: [
            Positioned(
              right: -6,
              bottom: -10,
              child: Icon(
                data.icon,
                size: 54,
                color: tokens.accent.withValues(alpha: 0.10),
              ),
            ),
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.baseline,
                  textBaseline: TextBaseline.alphabetic,
                  children: [
                    Flexible(
                      child: Text(
                        data.value,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: _tabular(
                          textTheme.headlineMedium?.copyWith(
                            fontSize: 30,
                            height: 1,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                    ),
                    if (data.unit != null)
                      Text(
                        ' ${data.unit}',
                        style: textTheme.bodySmall?.copyWith(
                          color: tokens.textTertiary,
                        ),
                      ),
                    if (data.value2 != null) ...[
                      const SizedBox(width: AppSpacing.xs),
                      Text(
                        data.value2!,
                        style: _tabular(
                          textTheme.headlineMedium?.copyWith(
                            fontSize: 26,
                            height: 1,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                      if (data.unit2 != null)
                        Text(
                          ' ${data.unit2}',
                          style: textTheme.bodySmall?.copyWith(
                            color: tokens.textTertiary,
                          ),
                        ),
                    ],
                  ],
                ),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      data.label,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.textSecondary,
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                    if (data.detail != null)
                      Text(
                        data.detail!,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.labelSmall?.copyWith(
                          color: tokens.textTertiary,
                        ),
                      ),
                  ],
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
