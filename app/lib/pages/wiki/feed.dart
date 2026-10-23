part of '../wiki_page.dart';

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
            icon: AppIcons.offline,
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
            icon: AppIcons.news,
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
                AppIcons.openExternal,
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
      opened = await launchWebUrl(url, mode: LaunchMode.externalApplication);
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
        Icon(AppIcons.error, size: 16, color: tokens.danger),
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
