part of '../stats_page.dart';

class _LoaderPanel extends StatelessWidget {
  const _LoaderPanel({required this.stats});

  final PlayStats stats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final entries = [
      for (final entry in stats.loaders)
        if (entry.instances > 0) entry,
    ];
    final total = entries.fold<int>(0, (sum, item) => sum + item.instances);

    return SizedBox(
      height: _ActivityHeatmapCard.cardHeight,
      child: AppPanel(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.statsLoadersTitle,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
                Text(
                  l10n.statsLoaderKinds(entries.length),
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ],
            ),
            const Spacer(),
            if (entries.isEmpty)
              Expanded(
                child: Center(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(
                        AppIcons.instances,
                        size: 24,
                        color: tokens.textTertiary,
                      ),
                      const SizedBox(height: AppSpacing.xs),
                      Text(
                        l10n.statsNoInstances,
                        style: Theme.of(context).textTheme.bodySmall
                            ?.copyWith(color: tokens.textTertiary),
                      ),
                    ],
                  ),
                ),
              )
            else
              Expanded(
                child: Row(
                  children: [
                    SizedBox(
                      width: 108,
                      height: 108,
                      child: Stack(
                        alignment: Alignment.center,
                        children: [
                          Positioned.fill(
                            child: CustomPaint(
                              painter: _DonutPainter(
                                entries: entries,
                                total: total,
                                track: tokens.surface2,
                                colors: [
                                  for (
                                    var index = 0;
                                    index < entries.length;
                                    index++
                                  )
                                    tokens.accent.withValues(
                                      alpha: math.max(
                                        0.28,
                                        0.95 - index * 0.16,
                                      ),
                                    ),
                                ],
                              ),
                            ),
                          ),
                          Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Text(
                                '$total',
                                style: _tabular(
                                  Theme.of(context).textTheme.headlineSmall
                                      ?.copyWith(fontWeight: FontWeight.w600),
                                ),
                              ),
                              Text(
                                l10n.statsInstances,
                                style: Theme.of(context).textTheme.labelSmall
                                    ?.copyWith(color: tokens.textTertiary),
                              ),
                            ],
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: AppSpacing.sm),
                    Expanded(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          for (var index = 0; index < entries.length; index++)
                            _LoaderLegendRow(
                              entry: entries[index],
                              color: tokens.accent.withValues(
                                alpha: math.max(0.28, 0.95 - index * 0.16),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _LoaderLegendRow extends StatelessWidget {
  const _LoaderLegendRow({required this.entry, required this.color});

  final LoaderInstanceStat entry;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final loader = _wireLoader(entry.loader);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        children: [
          Container(
            width: 7,
            height: 7,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: AppSpacing.xs),
          LoaderMark(loader: loader, size: 15),
          const SizedBox(width: 4),
          Expanded(
            child: Text(
              loader.labelFor(l10n),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.labelMedium,
            ),
          ),
          Text(
            '${entry.instances}',
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

class _DonutPainter extends CustomPainter {
  _DonutPainter({
    required this.entries,
    required this.total,
    required this.track,
    required this.colors,
  });

  final List<LoaderInstanceStat> entries;
  final int total;
  final Color track;
  final List<Color> colors;

  @override
  void paint(Canvas canvas, Size size) {
    if (total <= 0 || size.isEmpty) return;

    final center = size.center(Offset.zero);
    final radius = math.min(size.width, size.height) / 2 - 8;
    final trackPaint = Paint()
      ..color = track
      ..style = PaintingStyle.stroke
      ..strokeWidth = 14;
    canvas.drawCircle(center, radius, trackPaint);

    var start = -math.pi / 2;
    for (var index = 0; index < entries.length; index++) {
      final sweep = entries[index].instances / total * math.pi * 2;
      const gap = 0.035;
      final drawSweep = math.max(0.01, sweep - gap);
      final paint = Paint()
        ..color = colors[index]
        ..style = PaintingStyle.stroke
        ..strokeWidth = 14
        ..strokeCap = StrokeCap.butt;
      canvas.drawArc(
        Rect.fromCircle(center: center, radius: radius),
        start + gap / 2,
        drawSweep,
        false,
        paint,
      );
      start += sweep;
    }
  }

  @override
  bool shouldRepaint(covariant _DonutPainter oldDelegate) =>
      oldDelegate.entries != entries ||
      oldDelegate.total != total ||
      oldDelegate.track != track ||
      oldDelegate.colors != colors;
}
