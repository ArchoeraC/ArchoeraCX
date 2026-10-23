import 'dart:convert';

import '../core_transport.dart';
import 'wiki_feed.dart';

/// Wiki/community domain binding for the native core: `wiki.feed`.
mixin CoreClientWiki on CoreTransport {
  /// Fetch Minecraft community news, Java patch notes and wiki changes.
  ///
  /// [force] bypasses the module's five minute cache; [limit] caps rows per
  /// source and [sources] narrows the set (`mojang-news`, `mojang-java`,
  /// `minecraft-wiki`).
  Future<WikiFeed?> getWikiFeed({
    bool force = false,
    int? limit,
    List<String>? sources,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'wiki.feed';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'force': force,
        'limit': ?limit,
        'sources': ?sources,
      }),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return WikiFeed.tryParse(data);
  }
}
