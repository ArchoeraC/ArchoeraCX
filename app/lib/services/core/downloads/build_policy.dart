import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../stores/providers.dart';
import 'download_policy.dart';

/// Reads the runtime region/compliance gate. Mainland-China networks default
/// to disabled; other regions return enabled without requiring the file.
final downloadPolicyProvider = FutureProvider<DownloadPolicyState>((ref) async {
  final client = ref.watch(coreClientProvider);
  if (client == null) {
    return DownloadPolicyState(
      enabled: !kReleaseMode,
      reason: 'CORE_UNAVAILABLE',
      configPath: '',
      noticeVersion: 1,
    );
  }
  return client.getDownloadPolicy();
});

/// Synchronous convenience for navigation/shortcut visibility.
final downloadEnabledProvider = Provider<bool>((ref) {
  final policy = ref.watch(downloadPolicyProvider).value;
  if (policy != null) return policy.enabled;
  // Keep the entry hidden until the runtime region check has completed. This
  // avoids briefly exposing downloads on a mainland-China network.
  return !kReleaseMode;
});
