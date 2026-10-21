import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../stores/providers.dart';
import 'download_policy.dart';

/// Compile-time distribution policy. The native module owns the authoritative
/// gate; this constant is only kept for build diagnostics.
const bool kChinaBuild = bool.fromEnvironment(
  'ARCHOERACX_CHINA_BUILD',
  defaultValue: false,
);

/// Reads the local compliance.json gate. China builds default to disabled;
/// global/dev builds return enabled without requiring the file.
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
final downloadEnabledProvider = Provider<bool>(
  (ref) => ref.watch(downloadPolicyProvider).value?.enabled ?? false,
);
