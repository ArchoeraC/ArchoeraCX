import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/system_status.dart';
import '../services/core/wiki_feed.dart';
import 'providers.dart';
import 'settings.dart';

/// Community feed for the Wiki page.
///
/// The C# module owns fetching and caching; the store keeps the previous rows
/// on screen while a refresh is in flight (Riverpod keeps the last value on
/// invalidation), so the page never flashes an empty state.
final wikiFeedProvider = AsyncNotifierProvider<WikiFeedNotifier, WikiFeed?>(
  WikiFeedNotifier.new,
);

class WikiFeedNotifier extends AsyncNotifier<WikiFeed?> {
  /// Set by [refresh] so the rebuild bypasses the module cache.
  bool _force = false;

  @override
  Future<WikiFeed?> build() async {
    final client = ref.watch(coreClientProvider);
    if (client == null) return null;
    final force = _force;
    _force = false;
    return client.getWikiFeed(force: force);
  }

  /// Reloads the feed. [force] bypasses the module's five minute cache.
  Future<void> refresh({bool force = true}) async {
    _force = force;
    ref.invalidateSelf();
    try {
      await ref.read(wikiFeedProvider.future);
    } catch (_) {
      // The failure is already reflected in the provider state.
    }
  }
}

/// Machine load for the Wiki page.
///
/// The disk row follows the instance root from settings, so the number matches
/// where game files actually land.
final systemStatusProvider =
    AsyncNotifierProvider<SystemStatusNotifier, SystemStatus?>(
      SystemStatusNotifier.new,
    );

class SystemStatusNotifier extends AsyncNotifier<SystemStatus?> {
  @override
  Future<SystemStatus?> build() async {
    final client = ref.watch(coreClientProvider);
    if (client == null) return null;
    final root = ref.watch(
      appSettingsProvider.select((settings) => settings.value?.instancesRoot),
    );
    return client.getSystemStatus(path: root);
  }

  Future<void> refresh() async {
    ref.invalidateSelf();
    try {
      await ref.read(systemStatusProvider.future);
    } catch (_) {
      // The failure is already reflected in the provider state.
    }
  }
}
