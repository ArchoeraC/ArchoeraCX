import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/core_transport.dart';
import '../services/core/stats/stats.dart';
import 'providers.dart';

/// One aggregated statistics snapshot.
///
/// The page invalidates this when it becomes active so a session that ended
/// while the tab was hidden is reflected without a polling timer.
final playStatsProvider = FutureProvider.autoDispose<PlayStats>((ref) async {
  final client = ref.watch(coreClientProvider);
  if (client == null) {
    throw CoreUnavailableException('The native core is not connected.');
  }
  return client.getPlayStats();
});
