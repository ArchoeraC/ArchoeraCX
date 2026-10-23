part of '../wiki_page.dart';

/// Machine load: four metrics in a responsive grid inside one panel.
class _SystemLoadPanel extends StatelessWidget {
  const _SystemLoadPanel({required this.status});

  final AsyncValue<SystemStatus?> status;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final value = status.value;

    return AppPanel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Text(
                l10n.wikiSystemTitle,
                style: textTheme.labelSmall?.copyWith(
                  color: tokens.textSecondary,
                  letterSpacing: 1.2,
                  fontWeight: FontWeight.w500,
                ),
              ),
              const Spacer(),
              Text(
                l10n.wikiSystemLive,
                style: textTheme.labelSmall?.copyWith(
                  color: tokens.textTertiary,
                  letterSpacing: 0,
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          Container(height: 1, color: tokens.border),
          const SizedBox(height: AppSpacing.md),
          if (status.hasError)
            _InlineFailure(message: l10n.wikiStatusFailed)
          else if (value == null)
            Text(
              l10n.wikiUnavailable,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            )
          else
            _MetricGrid(status: value),
          if (value != null) ...[
            const SizedBox(height: AppSpacing.md),
            _SystemCaption(status: value),
          ],
        ],
      ),
    );
  }
}

class _MetricGrid extends StatelessWidget {
  const _MetricGrid({required this.status});

  final SystemStatus status;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    final cpuMeta = [
      if (status.cores > 0) l10n.wikiCoresCount(status.cores),
      if (status.loadAverage1m != null)
        l10n.wikiLoadAverage(_formatDecimal(status.loadAverage1m!)),
    ].join(' · ');

    final memoryMeta = status.hasMemory
        ? l10n.wikiMemoryUsage(
            _formatGb(status.memoryTotalMb! - (status.memoryAvailableMb ?? 0)),
            _formatGb(status.memoryTotalMb!),
          )
        : null;

    final diskMeta = status.diskFreeBytes == null
        ? null
        : l10n.wikiDiskFree(formatBytes(status.diskFreeBytes!));

    final launcherMeta = status.launcherUptimeSeconds == null
        ? null
        : l10n.wikiUptime(_formatDuration(status.launcherUptimeSeconds!));

    final tiles = <Widget>[
      _MetricTile(
        label: l10n.wikiCpuLabel,
        value: _formatPercent(status.cpuPercent),
        meta: cpuMeta.isEmpty ? null : cpuMeta,
        fraction: status.cpuPercent == null ? null : status.cpuPercent! / 100,
      ),
      _MetricTile(
        label: l10n.wikiMemoryLabel,
        value: _formatPercent(status.memoryPercent),
        meta: memoryMeta,
        fraction: status.memoryPercent == null
            ? null
            : status.memoryPercent! / 100,
      ),
      _MetricTile(
        label: l10n.wikiDiskLabel,
        value: _formatPercent(status.diskPercent),
        meta: diskMeta,
        fraction: status.diskPercent == null ? null : status.diskPercent! / 100,
      ),
      _MetricTile(
        label: l10n.wikiLauncherLabel,
        value: status.launcherWorkingSetMb == null
            ? '—'
            : '${status.launcherWorkingSetMb} MB',
        meta: launcherMeta,
        fraction: null,
      ),
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        final width = constraints.maxWidth;
        final columns = width >= 720 ? 4 : (width >= 380 ? 2 : 1);
        final gap = AppSpacing.lg;
        final tileWidth = (width - gap * (columns - 1)) / columns;

        return Wrap(
          spacing: gap,
          runSpacing: AppSpacing.lg,
          children: [
            for (final tile in tiles) SizedBox(width: tileWidth, child: tile),
          ],
        );
      },
    );
  }
}

class _MetricTile extends StatelessWidget {
  const _MetricTile({
    required this.label,
    required this.value,
    this.meta,
    this.fraction,
  });

  final String label;
  final String value;
  final String? meta;

  /// `null` hides the meter (a count, not a ratio).
  final double? fraction;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final ratio = fraction?.clamp(0.0, 1.0);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          label,
          maxLines: 1,
          style: textTheme.labelSmall?.copyWith(
            color: tokens.textTertiary,
            letterSpacing: 1,
          ),
        ),
        const SizedBox(height: AppSpacing.xxs),
        Text(
          value,
          maxLines: 1,
          style: textTheme.titleLarge?.copyWith(
            fontSize: 22,
            fontFeatures: const [FontFeature.tabularFigures()],
          ),
        ),
        const SizedBox(height: 2),
        Text(
          meta ?? '',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: textTheme.labelSmall?.copyWith(
            color: tokens.textTertiary,
            letterSpacing: 0,
          ),
        ),
        const SizedBox(height: AppSpacing.xs),
        _Meter(fraction: ratio),
      ],
    );
  }
}

class _Meter extends StatelessWidget {
  const _Meter({required this.fraction});

  final double? fraction;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final ratio = fraction;
    final color = ratio == null
        ? tokens.surface3
        : ratio >= 0.92
        ? tokens.danger
        : ratio >= 0.75
        ? tokens.warning
        : tokens.accent;

    return SizedBox(
      height: 3,
      child: ColoredBox(
        color: tokens.surface3,
        child: Align(
          alignment: Alignment.centerLeft,
          child: FractionallySizedBox(
            widthFactor: ratio ?? 0,
            heightFactor: 1,
            child: ColoredBox(color: color),
          ),
        ),
      ),
    );
  }
}

class _SystemCaption extends StatelessWidget {
  const _SystemCaption({required this.status});

  final SystemStatus status;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final parts = <String>[
      if (status.systemUptimeSeconds != null)
        l10n.wikiSystemUptime(_formatDuration(status.systemUptimeSeconds!)),
      if (status.os != null && status.os!.trim().isNotEmpty) status.os!,
    ];
    if (parts.isEmpty) return const SizedBox.shrink();

    return Text(
      parts.join(' · '),
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
      style: Theme.of(context).textTheme.labelSmall
          ?.copyWith(color: tokens.textTertiary, letterSpacing: 0),
    );
  }
}

String _formatPercent(double? percent) =>
    percent == null ? '—' : '${percent.round()}%';

String _formatDecimal(double value) {
  final rounded = (value * 100).round() / 100;
  return rounded == rounded.roundToDouble()
      ? rounded.toStringAsFixed(0)
      : rounded.toStringAsFixed(2);
}

String _formatGb(int megabytes) => (megabytes / 1024).toStringAsFixed(1);

String _formatDuration(double seconds) {
  final total = seconds.round();
  final days = total ~/ 86400;
  final hours = (total % 86400) ~/ 3600;
  final minutes = (total % 3600) ~/ 60;
  if (days > 0) return '${days}d ${hours.toString().padLeft(2, '0')}h';
  if (hours > 0) return '${hours}h ${minutes.toString().padLeft(2, '0')}m';
  if (minutes > 0) return '${minutes}m';
  return '${total}s';
}
