import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/core_event.dart';
import '../services/core/system/system_status.dart';
import '../services/core/wiki/wiki_feed.dart';
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
/// The C++ core samples the platform APIs and pushes a `system.status` domain
/// event every five seconds; this store only mirrors those pushes and asks for
/// one immediate snapshot when it starts. There is no Dart timer and no
/// per-frame polling. The disk row follows the instance root from settings so
/// the number matches where game files actually land.
final systemStatusProvider =
    AsyncNotifierProvider<SystemStatusNotifier, SystemStatus?>(
      SystemStatusNotifier.new,
    );

class SystemStatusNotifier extends AsyncNotifier<SystemStatus?> {
  StreamSubscription<CoreEvent>? _subscription;

  @override
  Future<SystemStatus?> build() async {
    final client = ref.watch(coreClientProvider);
    if (client == null) return null;

    final root = ref.watch(
      appSettingsProvider.select((settings) => settings.value?.instancesRoot),
    );

    final subscription = client.events
        .where(
          (event) =>
              event.type == CoreEventType.domain &&
              event.payload['kind'] == 'system.status',
        )
        .listen((event) {
          final status = SystemStatus.tryParse(event.payload);
          if (status != null) state = AsyncData(status);
        });
    _subscription = subscription;

    ref.onDispose(() {
      // A dependency rebuild disposes the previous subscription before the
      // new build runs, so capture it locally instead of reading the field.
      unawaited(subscription.cancel());
      if (identical(_subscription, subscription)) _subscription = null;
      unawaited(client.stopSystemStatusWatch());
    });

    // Idempotent on the core; failures only mean no automatic updates.
    unawaited(client.startSystemStatusWatch(intervalMs: 5000, path: root));
    return client.getSystemStatus(path: root);
  }

  /// One-shot refresh for the page button. The watch keeps running.
  Future<void> refresh() async {
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    final root = ref.read(appSettingsProvider).value?.instancesRoot;
    try {
      state = AsyncData(await client.getSystemStatus(path: root));
    } catch (error, stackTrace) {
      state = AsyncError(error, stackTrace);
    }
  }
}
