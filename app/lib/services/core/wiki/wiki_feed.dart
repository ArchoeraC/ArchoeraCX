/// One normalized row of the Wiki feed.
///
/// Sources are identified by [source] (`mojang-news`, `mojang-java`,
/// `minecraft-wiki`) so the page can group rows without parsing titles.
final class WikiFeedItem {
  const WikiFeedItem({
    required this.id,
    required this.source,
    required this.kind,
    required this.title,
    this.summary,
    this.url,
    this.publishedAt,
    this.author,
    this.tags = const <String>[],
  });

  final String id;
  final String source;

  /// `news`, `release`, `snapshot`, `edit`/`new` for wiki changes.
  final String kind;
  final String title;
  final String? summary;
  final String? url;
  final DateTime? publishedAt;
  final String? author;
  final List<String> tags;

  static WikiFeedItem? tryParse(Object? value) {
    if (value is! Map) return null;
    final id = _string(value['id']);
    final source = _string(value['source']);
    final title = _string(value['title']);
    if (id == null || source == null || title == null) return null;
    final rawTags = value['tags'];
    return WikiFeedItem(
      id: id,
      source: source,
      kind: _string(value['kind']) ?? 'news',
      title: title,
      summary: _string(value['summary']),
      url: _string(value['url']),
      publishedAt: DateTime.tryParse(_string(value['publishedAt']) ?? '')?.toLocal(),
      author: _string(value['author']),
      tags: rawTags is List
          ? List<String>.unmodifiable(rawTags.map(_string).whereType<String>())
          : const <String>[],
    );
  }

  static String? _string(Object? value) {
    if (value is String && value.trim().isNotEmpty) return value;
    return null;
  }
}

/// Per-source fetch result, so a partial failure stays visible.
final class WikiSourceStatus {
  const WikiSourceStatus({
    required this.id,
    required this.ok,
    required this.count,
    this.error,
  });

  final String id;
  final bool ok;
  final int count;
  final String? error;

  static WikiSourceStatus? tryParse(Object? value) {
    if (value is! Map) return null;
    final id = value['id'];
    if (id is! String) return null;
    final count = value['count'];
    return WikiSourceStatus(
      id: id,
      ok: value['ok'] == true,
      count: count is num ? count.toInt() : 0,
      error: value['error'] is String ? value['error'] as String : null,
    );
  }
}

/// Merged community feed with source health.
final class WikiFeed {
  const WikiFeed({
    required this.items,
    required this.sources,
    this.fetchedAt,
    this.cached = false,
  });

  final List<WikiFeedItem> items;
  final List<WikiSourceStatus> sources;
  final DateTime? fetchedAt;
  final bool cached;

  bool get isEmpty => items.isEmpty;

  List<WikiFeedItem> itemsFor(String source) =>
      items.where((item) => item.source == source).toList(growable: false);

  WikiSourceStatus? statusFor(String source) {
    for (final status in sources) {
      if (status.id == source) return status;
    }
    return null;
  }

  static WikiFeed? tryParse(Object? value) {
    if (value is! Map) return null;
    final items = value['items'];
    final sources = value['sources'];
    return WikiFeed(
      items: List<WikiFeedItem>.unmodifiable(
        (items is List ? items : const <Object?>[])
            .map(WikiFeedItem.tryParse)
            .whereType<WikiFeedItem>(),
      ),
      sources: List<WikiSourceStatus>.unmodifiable(
        (sources is List ? sources : const <Object?>[])
            .map(WikiSourceStatus.tryParse)
            .whereType<WikiSourceStatus>(),
      ),
      fetchedAt: DateTime.tryParse(value['fetchedAt'] as String? ?? '')?.toLocal(),
      cached: value['cached'] == true,
    );
  }
}
