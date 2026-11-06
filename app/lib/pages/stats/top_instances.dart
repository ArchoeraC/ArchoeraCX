part of '../stats_page.dart';

class _RankingPanels extends StatelessWidget {
  const _RankingPanels({required this.stats});

  final PlayStats stats;

  @override
  Widget build(BuildContext context) {
    final top = _TopInstancesCard(stats: stats);
    final recent = _RecentSessionsCard(stats: stats);

    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth >= 920) {
          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(flex: 3, child: top),
              const SizedBox(width: AppSpacing.md),
              Expanded(flex: 2, child: recent),
            ],
          );
        }
        return Column(
          children: [
            top,
            const SizedBox(height: AppSpacing.md),
            recent,
          ],
        );
      },
    );
  }
}

class _TopInstancesCard extends StatelessWidget {
  const _TopInstancesCard({required this.stats});

  final PlayStats stats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final items = stats.topInstances;

    return AppPanel(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Container(
                width: 34,
                height: 34,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: tokens.accentSoft,
                  borderRadius: BorderRadius.circular(tokens.radiusS),
                ),
                child: Icon(AppIcons.stats, size: 18, color: tokens.accent),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      l10n.statsTopInstances,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    Text(
                      l10n.statsTopCount('${items.length}'),
                      style: Theme.of(context).textTheme.labelSmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          if (items.isEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: AppSpacing.xl),
              child: Center(
                child: Text(
                  l10n.statsNoSessions,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ),
            )
          else
            for (var index = 0; index < items.length; index++)
              _TopInstanceRow(item: items[index], rank: index + 1),
        ],
      ),
    );
  }
}

class _TopInstanceRow extends StatelessWidget {
  const _TopInstanceRow({required this.item, required this.rank});

  final InstancePlayStat item;
  final int rank;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final loader = _wireLoader(item.loader);
    final first = rank == 1;

    return Container(
      margin: const EdgeInsets.only(bottom: 2),
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: AppSpacing.xs,
      ),
      decoration: BoxDecoration(
        color: first ? tokens.accentSoft.withValues(alpha: 0.55) : null,
        borderRadius: BorderRadius.circular(tokens.radiusM),
      ),
      child: Row(
        children: [
          SizedBox(
            width: 24,
            child: Text(
              rank.toString().padLeft(2, '0'),
              style: _tabular(
                Theme.of(context).textTheme.labelMedium?.copyWith(
                  color: first ? tokens.accent : tokens.textTertiary,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
          ),
          LoaderMark(loader: loader, size: 26),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  item.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    fontWeight: first ? FontWeight.w600 : FontWeight.w500,
                  ),
                ),
                Text(
                  '${loader.labelFor(l10n)} · ${item.gameVersion}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                item.playMs > 0
                    ? _durationText(l10n, item.playMs)
                    : l10n.statsNoPlaytime,
                style: _tabular(
                  Theme.of(context).textTheme.bodyMedium
                      ?.copyWith(fontWeight: FontWeight.w600),
                ),
              ),
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (item.running) ...[
                    Container(
                      width: 6,
                      height: 6,
                      decoration: BoxDecoration(
                        color: tokens.success,
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 4),
                    Text(
                      l10n.statsRunning,
                      style: Theme.of(context).textTheme.labelSmall
                          ?.copyWith(color: tokens.success),
                    ),
                  ] else
                    Text(
                      l10n.statsSessionCount(item.sessions),
                      style: Theme.of(context).textTheme.labelSmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                ],
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _RecentSessionsCard extends StatelessWidget {
  const _RecentSessionsCard({required this.stats});

  final PlayStats stats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final items = stats.recentSessions;

    return AppPanel(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Container(
                width: 34,
                height: 34,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: tokens.surface2,
                  borderRadius: BorderRadius.circular(tokens.radiusS),
                ),
                child: Icon(
                  AppIcons.time,
                  size: 18,
                  color: tokens.textSecondary,
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      l10n.statsRecentSessions,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    Text(
                      l10n.statsSessionCount(stats.summary.totalSessions),
                      style: Theme.of(context).textTheme.labelSmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          if (items.isEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: AppSpacing.xl),
              child: Center(
                child: Text(
                  l10n.statsRecentEmpty,
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ),
            )
          else
            for (final item in items) _RecentSessionRow(item: item),
        ],
      ),
    );
  }
}

class _RecentSessionRow extends StatelessWidget {
  const _RecentSessionRow({required this.item});

  final RecentPlaySession item;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final loader = _wireLoader(item.loader);
    final date = MaterialLocalizations.of(context)
        .formatShortDate(item.startedAt);
    final time = DateFormat.Hm(l10n.localeName).format(item.startedAt);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(
        children: [
          SizedBox(
            width: 67,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  date,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.textSecondary),
                ),
                Text(
                  time,
                  style: _tabular(
                    Theme.of(context).textTheme.labelSmall
                        ?.copyWith(color: tokens.textTertiary),
                  ),
                ),
              ],
            ),
          ),
          LoaderMark(loader: loader, size: 18),
          const SizedBox(width: 6),
          Expanded(
            child: Text(
              item.name,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          if (item.endedAt == null)
            Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 6,
                  height: 6,
                  decoration: BoxDecoration(
                    color: tokens.success,
                    shape: BoxShape.circle,
                  ),
                ),
                const SizedBox(width: 4),
                Text(
                  l10n.statsRunning,
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.success),
                ),
              ],
            )
          else
            Text(
              _durationText(l10n, item.durationMs),
              style: _tabular(
                Theme.of(context).textTheme.labelMedium
                    ?.copyWith(color: tokens.textSecondary),
              ),
            ),
        ],
      ),
    );
  }
}
