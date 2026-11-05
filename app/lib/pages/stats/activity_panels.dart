part of '../stats_page.dart';

class _ActivityPanels extends StatelessWidget {
  const _ActivityPanels({required this.stats});

  final PlayStats stats;

  @override
  Widget build(BuildContext context) {
    final heatmap = _ActivityHeatmapCard(stats: stats);
    final hourly = _HourlyCard(stats: stats);
    final loaders = _LoaderPanel(stats: stats);

    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth >= 1120) {
          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(flex: 10, child: heatmap),
              const SizedBox(width: AppSpacing.md),
              Expanded(flex: 11, child: hourly),
              const SizedBox(width: AppSpacing.md),
              Expanded(flex: 9, child: loaders),
            ],
          );
        }
        if (constraints.maxWidth >= 760) {
          return Column(
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(flex: 10, child: heatmap),
                  const SizedBox(width: AppSpacing.md),
                  Expanded(flex: 11, child: hourly),
                ],
              ),
              const SizedBox(height: AppSpacing.md),
              loaders,
            ],
          );
        }
        return Column(
          children: [
            heatmap,
            const SizedBox(height: AppSpacing.md),
            hourly,
            const SizedBox(height: AppSpacing.md),
            loaders,
          ],
        );
      },
    );
  }
}

class _ActivityHeatmapCard extends StatelessWidget {
  const _ActivityHeatmapCard({required this.stats});

  final PlayStats stats;

  /// Height shared by the three activity cards on a wide window.
  static const double cardHeight = 252;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final locale = l10n.localeName;
    final daily = stats.daily;
    final maxPlay = daily.fold<int>(
      0,
      (value, day) => math.max(value, day.playMs),
    );

    final aligned = <DailyPlayStat?>[];
    if (daily.isNotEmpty) {
      final lead = daily.first.day.weekday - 1;
      aligned.addAll(List<DailyPlayStat?>.filled(lead, null));
      aligned.addAll(daily);
      while (aligned.length % 7 != 0) {
        aligned.add(null);
      }
    }
    final weeks = [
      for (var start = 0; start < aligned.length; start += 7)
        aligned.sublist(start, math.min(start + 7, aligned.length)),
    ];

    final monthFormat = DateFormat.MMM(locale);
    String? previousMonth;
    final monthLabels = <String?>[];
    for (final week in weeks) {
      DailyPlayStat? first;
      for (final day in week) {
        if (day != null) {
          first = day;
          break;
        }
      }
      if (first == null) {
        monthLabels.add(null);
        continue;
      }
      final label = monthFormat.format(first.day);
      monthLabels.add(label == previousMonth ? null : label);
      previousMonth = label;
    }

    final weekdayFormat = DateFormat.E(locale);
    final monday = DateTime(2024, 1, 1);
    final weekdayLabels = [
      for (var row = 0; row < 7; row++)
        row == 0 || row == 2 || row == 4
            ? weekdayFormat.format(monday.add(Duration(days: row)))
            : '',
    ];

    return SizedBox(
      height: cardHeight,
      child: AppPanel(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.statsActivityTitle,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
                Text(
                  l10n.statsLast90Days,
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ],
            ),
            const Spacer(),
            Center(
              child: Row(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SizedBox(
                    width: 28,
                    child: Column(
                      children: [
                        const SizedBox(height: 18),
                        for (final label in weekdayLabels)
                          SizedBox(
                            height: 16,
                            child: Align(
                              alignment: Alignment.centerRight,
                              child: Text(
                                label,
                                style: Theme.of(context).textTheme.labelSmall
                                    ?.copyWith(
                                      color: tokens.textTertiary,
                                      fontSize: 10,
                                    ),
                              ),
                            ),
                          ),
                      ],
                    ),
                  ),
                  for (var weekIndex = 0; weekIndex < weeks.length; weekIndex++)
                    Padding(
                      // Uniform gap: a month does not start on a week
                      // boundary, so isolating months here would break the
                      // rectangle. The label above already marks the change.
                      padding: const EdgeInsets.only(right: 3),
                      child: Column(
                        key: ValueKey('stats-week-$weekIndex'),
                        children: [
                          SizedBox(
                            width: 13,
                            height: 18,
                            child: OverflowBox(
                              alignment: Alignment.center,
                              minWidth: 0,
                              maxWidth: 64,
                              child: Text(
                                monthLabels[weekIndex] ?? '',
                                maxLines: 1,
                                softWrap: false,
                                overflow: TextOverflow.visible,
                                style: Theme.of(context).textTheme.labelSmall
                                    ?.copyWith(
                                      color: tokens.textTertiary,
                                      fontSize: 10,
                                    ),
                              ),
                            ),
                          ),
                          for (final day in weeks[weekIndex])
                            Padding(
                              padding: const EdgeInsets.only(bottom: 3),
                              child: _HeatCell(day: day, maxPlayMs: maxPlay),
                            ),
                        ],
                      ),
                    ),
                ],
              ),
            ),
            const Spacer(),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(
                  l10n.statsLess,
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
                const SizedBox(width: AppSpacing.xs),
                for (var level = 1; level <= 5; level++)
                  Padding(
                    padding: const EdgeInsets.only(right: 3),
                    child: Container(
                      width: 10,
                      height: 10,
                      decoration: BoxDecoration(
                        color: tokens.accent.withValues(
                          alpha: 0.16 + level / 5 * 0.7,
                        ),
                        borderRadius: BorderRadius.circular(2),
                      ),
                    ),
                  ),
                const SizedBox(width: AppSpacing.xs),
                Text(
                  l10n.statsMore,
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _HeatCell extends StatelessWidget {
  const _HeatCell({required this.day, required this.maxPlayMs});

  final DailyPlayStat? day;
  final int maxPlayMs;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final value = day;
    if (value == null) {
      return const SizedBox(width: 13, height: 13);
    }

    final l10n = AppLocalizations.of(context);
    final ratio = maxPlayMs == 0 ? 0.0 : value.playMs / maxPlayMs;
    final active = value.sessions > 0;
    final color = active
        ? tokens.accent.withValues(alpha: 0.16 + ratio.clamp(0.0, 1.0) * 0.70)
        : tokens.surface2;

    return Tooltip(
      message: l10n.statsDayTooltip(
        MaterialLocalizations.of(context).formatShortDate(value.day),
        _durationText(l10n, value.playMs),
        l10n.statsSessionCount(value.sessions),
      ),
      waitDuration: const Duration(milliseconds: 300),
      child: Container(
        width: 13,
        height: 13,
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(3),
        ),
      ),
    );
  }
}

class _HourlyCard extends StatelessWidget {
  const _HourlyCard({required this.stats});

  final PlayStats stats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final totalPlay = stats.hourly.fold<int>(
      0,
      (sum, hour) => sum + hour.playMs,
    );
    final totalSessions = stats.hourly.fold<int>(
      0,
      (sum, hour) => sum + hour.sessions,
    );
    final useSessions = totalPlay == 0 && totalSessions > 0;
    final values = [
      for (final hour in stats.hourly)
        useSessions ? hour.sessions : hour.playMs,
    ];

    var peakIndex = 0;
    for (var index = 1; index < values.length; index++) {
      if (values[index] > values[peakIndex]) peakIndex = index;
    }
    final hasData = totalSessions > 0 && values.length >= 2;

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
                    l10n.statsHoursTitle,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
                if (useSessions)
                  Text(
                    l10n.statsBySessions,
                    style: Theme.of(context).textTheme.labelSmall
                        ?.copyWith(color: tokens.textTertiary),
                  ),
              ],
            ),
            const SizedBox(height: AppSpacing.xs),
            Expanded(
              child: hasData
                  ? LayoutBuilder(
                      builder: (context, constraints) {
                        final width = constraints.maxWidth;
                        final height = constraints.maxHeight;
                        final maxValue = values[peakIndex];
                        final peakX = peakIndex / 23 * width;
                        final peakY =
                            height -
                            4 -
                            (values[peakIndex] / maxValue) *
                                math.max(1, height - 16);
                        final badgeWidth = 88.0;
                        return Stack(
                          clipBehavior: Clip.none,
                          children: [
                            Positioned.fill(
                              child: CustomPaint(
                                painter: _HourlyChartPainter(
                                  values: values,
                                  accent: tokens.accent,
                                  area: tokens.accent.withValues(alpha: 0.12),
                                  grid: tokens.border.withValues(alpha: 0.55),
                                ),
                              ),
                            ),
                            Positioned(
                              left: (peakX - badgeWidth / 2)
                                  .clamp(0.0, math.max(0.0, width - badgeWidth))
                                  .toDouble(),
                              top: (peakY - 40)
                                  .clamp(0.0, math.max(0.0, height - 40))
                                  .toDouble(),
                              child: SizedBox(
                                width: badgeWidth,
                                child: DecoratedBox(
                                  decoration: BoxDecoration(
                                    color: tokens.accent,
                                    borderRadius: BorderRadius.circular(
                                      tokens.radiusS,
                                    ),
                                  ),
                                  child: Padding(
                                    padding: const EdgeInsets.symmetric(
                                      horizontal: 6,
                                      vertical: 3,
                                    ),
                                    child: Text(
                                      l10n.statsPeakHour(
                                        peakIndex.toString().padLeft(2, '0'),
                                      ),
                                      textAlign: TextAlign.center,
                                      maxLines: 1,
                                      overflow: TextOverflow.ellipsis,
                                      style: Theme.of(context)
                                          .textTheme
                                          .labelSmall
                                          ?.copyWith(
                                            color: tokens.onAccent,
                                            fontWeight: FontWeight.w600,
                                          ),
                                    ),
                                  ),
                                ),
                              ),
                            ),
                          ],
                        );
                      },
                    )
                  : Center(
                      child: Text(
                        l10n.statsNoSessions,
                        style: Theme.of(context).textTheme.bodySmall
                            ?.copyWith(color: tokens.textTertiary),
                      ),
                    ),
            ),
            const SizedBox(height: AppSpacing.xs),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                for (final label in const ['00', '06', '12', '18', '24'])
                  Text(
                    label,
                    style: Theme.of(context).textTheme.labelSmall
                        ?.copyWith(color: tokens.textTertiary, fontSize: 10),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _HourlyChartPainter extends CustomPainter {
  _HourlyChartPainter({
    required this.values,
    required this.accent,
    required this.area,
    required this.grid,
  });

  final List<int> values;
  final Color accent;
  final Color area;
  final Color grid;

  @override
  void paint(Canvas canvas, Size size) {
    if (values.length < 2 || size.width <= 0 || size.height <= 0) {
      return;
    }

    final gridPaint = Paint()
      ..color = grid
      ..strokeWidth = 1;
    for (var line = 0; line <= 4; line++) {
      final y = size.height * line / 4;
      canvas.drawLine(Offset(0, y), Offset(size.width, y), gridPaint);
    }

    var maxValue = 0;
    for (final value in values) {
      if (value > maxValue) maxValue = value;
    }
    if (maxValue <= 0) return;

    final points = <Offset>[];
    for (var index = 0; index < values.length; index++) {
      final x = index / (values.length - 1) * size.width;
      final ratio = values[index] / maxValue;
      final y = size.height - 4 - ratio * math.max(1, size.height - 16);
      points.add(Offset(x, y));
    }

    final line = Path()..moveTo(points.first.dx, points.first.dy);
    for (var index = 1; index < points.length; index++) {
      final previous = points[index - 1];
      final current = points[index];
      final midX = (previous.dx + current.dx) / 2;
      line.cubicTo(midX, previous.dy, midX, current.dy, current.dx, current.dy);
    }

    final fill = Path.from(line)
      ..lineTo(size.width, size.height)
      ..lineTo(0, size.height)
      ..close();
    canvas.drawPath(fill, Paint()..color = area);
    canvas.drawPath(
      line,
      Paint()
        ..color = accent
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2
        ..strokeCap = StrokeCap.round
        ..strokeJoin = StrokeJoin.round,
    );

    var peak = points.first;
    for (final point in points) {
      if (point.dy < peak.dy) peak = point;
    }
    canvas.drawCircle(peak, 3.5, Paint()..color = accent);
    canvas.drawCircle(
      peak,
      6,
      Paint()
        ..color = accent.withValues(alpha: 0.25)
        ..style = PaintingStyle.fill,
    );
  }

  @override
  bool shouldRepaint(covariant _HourlyChartPainter oldDelegate) =>
      oldDelegate.values != values ||
      oldDelegate.accent != accent ||
      oldDelegate.area != area ||
      oldDelegate.grid != grid;
}
