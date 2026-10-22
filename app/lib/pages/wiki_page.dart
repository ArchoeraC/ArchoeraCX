import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/system_status.dart';
import '../services/core/wiki_feed.dart';
import '../stores/wiki.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_panel.dart';
import '../widgets/common/toast.dart';

/// Community page: what happened around Minecraft, plus what this machine is
/// doing right now.
///
/// Both halves come from C# (`wiki.feed`, `system.status`); the page only lays
/// out already-normalized data and never fetches or polls on its own. Refresh
/// is explicit — no timers, in line with the wake-driven core contract.
class WikiPage extends ConsumerWidget {
  const WikiPage({super.key});

  static const newsSource = 'mojang-news';
  static const javaSource = 'mojang-java';
  static const wikiSource = 'minecraft-wiki';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final feed = ref.watch(wikiFeedProvider);
    final status = ref.watch(systemStatusProvider);
    final busy = feed.isLoading || status.isLoading;

    return SingleChildScrollView(
      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppPageHeader(
            title: l10n.navWiki,
            subtitle: l10n.wikiSubtitle,
            actions: [
              OutlinedButton.icon(
                onPressed: busy ? null : () => _refresh(ref),
                icon: busy
                    ? const SizedBox(
                        width: 14,
                        height: 14,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.refresh_rounded, size: 18),
                label: Text(l10n.wikiRefresh),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
          _SystemLoadPanel(status: status),
          const SizedBox(height: AppSpacing.xl),
          _FeedPanel(
            feed: feed,
            onRetry: () => ref.read(wikiFeedProvider.notifier).refresh(),
          ),
        ],
      ),
    );
  }

  void _refresh(WidgetRef ref) {
    ref.read(wikiFeedProvider.notifier).refresh();
    ref.read(systemStatusProvider.notifier).refresh();
  }
}

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
        : l10n.wikiDiskFree(_formatBytes(status.diskFreeBytes!));

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

/// Community feed: one chronological list with a per-source health strip.
class _FeedPanel extends StatelessWidget {
  const _FeedPanel({required this.feed, required this.onRetry});

  final AsyncValue<WikiFeed?> feed;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final value = feed.value;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(l10n.wikiFeedTitle, style: textTheme.titleMedium),
                  const SizedBox(height: 2),
                  Text(
                    l10n.wikiFeedSubtitle,
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                ],
              ),
            ),
            if (value != null)
              Text(
                l10n.wikiUpdatedAt(_formatClockFromDate(value.fetchedAt)),
                style: textTheme.labelSmall?.copyWith(
                  color: tokens.textTertiary,
                  letterSpacing: 0,
                ),
              ),
          ],
        ),
        const SizedBox(height: AppSpacing.sm),
        if (value != null)
          Wrap(
            spacing: AppSpacing.xs,
            runSpacing: AppSpacing.xxs,
            children: [
              for (final source in value.sources)
                _SourceChip(
                  label: _sourceLabel(l10n, source.id),
                  status: source,
                ),
            ],
          ),
        const SizedBox(height: AppSpacing.md),
        if (feed.hasError)
          AppEmptyState(
            icon: Icons.cloud_off_rounded,
            title: l10n.wikiFeedFailed,
            message: feed.error.toString(),
            primaryAction: OutlinedButton(
              onPressed: onRetry,
              child: Text(l10n.wikiRetry),
            ),
          )
        else if (value == null)
          const Padding(
            padding: EdgeInsets.symmetric(vertical: AppSpacing.xl),
            child: Center(child: CircularProgressIndicator()),
          )
        else if (value.isEmpty)
          AppEmptyState(
            icon: Icons.article_outlined,
            title: l10n.wikiFeedEmpty,
            message: l10n.wikiFeedSubtitle,
            primaryAction: OutlinedButton(
              onPressed: onRetry,
              child: Text(l10n.wikiRetry),
            ),
          )
        else
          _FeedList(items: value.items),
      ],
    );
  }
}

class _FeedList extends StatelessWidget {
  const _FeedList({required this.items});

  final List<WikiFeedItem> items;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        // One wide column of news lines is hard to scan on fullscreen; split
        // into balanced columns as the window grows.
        final columns = constraints.maxWidth >= 2100
            ? 3
            : constraints.maxWidth >= 1200
            ? 2
            : 1;
        if (columns == 1 || items.length < columns * 2) {
          return _FeedColumn(items: items);
        }

        final chunks = _split(items, columns);
        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (var index = 0; index < chunks.length; index++) ...[
              if (index > 0) const SizedBox(width: AppSpacing.xl),
              Expanded(child: _FeedColumn(items: chunks[index])),
            ],
          ],
        );
      },
    );
  }
}

/// Splits items into [columns] consecutive chunks of nearly equal size.
List<List<WikiFeedItem>> _split(List<WikiFeedItem> items, int columns) {
  final result = <List<WikiFeedItem>>[];
  final base = items.length ~/ columns;
  final remainder = items.length % columns;
  var start = 0;
  for (var index = 0; index < columns; index++) {
    final size = base + (index < remainder ? 1 : 0);
    result.add(items.sublist(start, start + size));
    start += size;
  }
  return result;
}

class _FeedColumn extends StatelessWidget {
  const _FeedColumn({required this.items});

  final List<WikiFeedItem> items;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return DecoratedBox(
      decoration: BoxDecoration(
        border: Border(top: BorderSide(color: tokens.border)),
      ),
      child: Column(
        children: [
          for (var index = 0; index < items.length; index++) ...[
            _FeedRow(item: items[index]),
            if (index != items.length - 1)
              Container(height: 1, color: tokens.border.withValues(alpha: 0.6)),
          ],
        ],
      ),
    );
  }
}

class _FeedRow extends StatelessWidget {
  const _FeedRow({required this.item});

  final WikiFeedItem item;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final kind = _kindLabel(l10n, item.kind);
    final meta = <String>[
      _sourceLabel(l10n, item.source),
      ?kind,
      _formatRelative(item.publishedAt, l10n),
    ].join('  ·  ');
    final tags = [
      if (item.author != null) '@${item.author}',
      ...item.tags.where((tag) => tag != item.kind),
    ].join('  ·  ');

    final content = Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  meta,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.labelSmall?.copyWith(
                    color: tokens.textTertiary,
                    letterSpacing: 0,
                  ),
                ),
                const SizedBox(height: AppSpacing.xxs),
                Text(
                  item.title,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.titleSmall?.copyWith(height: 1.35),
                ),
                if (item.summary != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    item.summary!,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textSecondary,
                    ),
                  ),
                ],
                if (tags.isNotEmpty) ...[
                  const SizedBox(height: AppSpacing.xxs),
                  Text(
                    tags,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.labelSmall?.copyWith(
                      color: tokens.textTertiary,
                      letterSpacing: 0,
                    ),
                  ),
                ],
              ],
            ),
          ),
          if (item.url != null)
            Padding(
              padding: const EdgeInsets.only(left: AppSpacing.sm, top: 2),
              child: Icon(
                Icons.north_east_rounded,
                size: 15,
                color: tokens.textTertiary,
              ),
            ),
        ],
      ),
    );

    final url = item.url;
    if (url == null) return content;

    return Tooltip(
      message: l10n.wikiOpenInBrowser,
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: () => _open(context, l10n, url),
          hoverColor: tokens.surface2,
          child: content,
        ),
      ),
    );
  }

  static Future<void> _open(
    BuildContext context,
    AppLocalizations l10n,
    String url,
  ) async {
    var opened = false;
    try {
      final uri = Uri.tryParse(url);
      if (uri != null) {
        opened = await launchUrl(uri, mode: LaunchMode.externalApplication);
      }
    } catch (_) {
      opened = false;
    }
    if (!opened) {
      toast(l10n.wikiOpenLinkFailed, type: ToastType.error);
    }
  }
}

class _SourceChip extends StatelessWidget {
  const _SourceChip({required this.label, required this.status});

  final String label;
  final WikiSourceStatus status;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final ok = status.ok;
    final color = ok ? tokens.textSecondary : tokens.danger;

    final chip = Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: 3,
      ),
      decoration: ShapeDecoration(
        color: ok ? tokens.surface2 : tokens.danger.withValues(alpha: 0.10),
        shape: BeveledRectangleBorder(
          side: BorderSide(
            color: ok ? tokens.border : tokens.danger.withValues(alpha: 0.40),
          ),
          borderRadius: BorderRadius.circular(tokens.radiusS),
        ),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 5,
            height: 5,
            color: ok ? tokens.success : tokens.danger,
          ),
          const SizedBox(width: 6),
          Text(
            '$label · ${ok ? status.count : l10n.wikiUnavailable}',
            style: Theme.of(context).textTheme.labelSmall
                ?.copyWith(color: color, letterSpacing: 0),
          ),
        ],
      ),
    );

    final error = status.error;
    if (ok || error == null) return chip;
    return Tooltip(message: l10n.wikiSourceError(error), child: chip);
  }
}

class _InlineFailure extends StatelessWidget {
  const _InlineFailure({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Row(
      children: [
        Icon(Icons.error_outline_rounded, size: 16, color: tokens.danger),
        const SizedBox(width: AppSpacing.xs),
        Expanded(
          child: Text(
            message,
            style: textTheme.bodySmall?.copyWith(color: tokens.textSecondary),
          ),
        ),
      ],
    );
  }
}

String _sourceLabel(AppLocalizations l10n, String source) => switch (source) {
  WikiPage.newsSource => l10n.wikiSourceNews,
  WikiPage.javaSource => l10n.wikiSourceJava,
  WikiPage.wikiSource => l10n.wikiSourceWiki,
  _ => source,
};

String? _kindLabel(AppLocalizations l10n, String kind) => switch (kind) {
  'release' => l10n.wikiKindRelease,
  'snapshot' => l10n.wikiKindSnapshot,
  'edit' => l10n.wikiKindEdit,
  'new' => l10n.wikiKindNew,
  'news' => null,
  _ => null,
};

String _formatPercent(double? percent) =>
    percent == null ? '—' : '${percent.round()}%';

String _formatDecimal(double value) {
  final rounded = (value * 100).round() / 100;
  return rounded == rounded.roundToDouble()
      ? rounded.toStringAsFixed(0)
      : rounded.toStringAsFixed(2);
}

String _formatGb(int megabytes) => (megabytes / 1024).toStringAsFixed(1);

String _formatBytes(int bytes) {
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  var value = bytes.toDouble();
  var unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  // Gigabytes and up keep one decimal; bytes/small units stay whole.
  final digits = unit == 0 || (unit < 3 && value >= 100) ? 0 : 1;
  return '${value.toStringAsFixed(digits)} ${units[unit]}';
}

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

String _formatClockFromDate(DateTime? time) {
  if (time == null) return '';
  final local = time.toLocal();
  final now = DateTime.now();
  final clock =
      '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
  if (local.year == now.year &&
      local.month == now.month &&
      local.day == now.day) {
    return clock;
  }
  return '${local.month.toString().padLeft(2, '0')}-${local.day.toString().padLeft(2, '0')} $clock';
}

String _formatRelative(DateTime? time, AppLocalizations l10n) {
  if (time == null) return '';
  final difference = DateTime.now().difference(time);
  if (difference.inMinutes < 1) return l10n.wikiTimeJustNow;
  if (difference.inMinutes < 60) {
    return l10n.wikiTimeMinutesAgo(difference.inMinutes);
  }
  if (difference.inHours < 24) return l10n.wikiTimeHoursAgo(difference.inHours);
  if (difference.inDays < 7) return l10n.wikiTimeDaysAgo(difference.inDays);
  return '${time.year}-${time.month.toString().padLeft(2, '0')}-'
      '${time.day.toString().padLeft(2, '0')}';
}
