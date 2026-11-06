import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/core_transport.dart';
import '../services/core/stats/stats.dart';
import 'providers.dart';

/// Aggregated play history for the stats branch.
///
/// Loaded when the branch is first built (branches are created lazily) and kept
/// afterwards, so re-entering the page repaints the last snapshot while
/// `stats.get` refreshes in the background. Riverpod keeps the previous value
/// across an invalidation, which is what keeps the branch transition free of
/// the placeholder skeleton: those three flat, near-opaque blocks read as an
/// opaque flash over the wallpaper when they appear for a single frame.
final playStatsProvider = AsyncNotifierProvider<PlayStatsNotifier, PlayStats>(
  PlayStatsNotifier.new,
);

class PlayStatsNotifier extends AsyncNotifier<PlayStats> {
  @override
  Future<PlayStats> build() {
    final client = ref.watch(coreClientProvider);
    if (client == null) {
      throw CoreUnavailableException('The native core is not connected.');
    }
    return client.getPlayStats();
  }

  /// Reloads the snapshot without dropping the one currently on screen.
  Future<void> refresh() async {
    ref.invalidateSelf();
    try {
      await ref.read(playStatsProvider.future);
    } catch (_) {
      // The failure is already reflected in the provider state.
    }
  }
}
