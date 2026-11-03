import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/account.dart';
import '../services/core/downloads/region_policy.dart';
import 'accounts.dart';
import 'providers.dart';

/// Product-wide account policy.
///
/// Release builds require a verified Microsoft/Minecraft account before
/// offline or other local identities may be added or selected. Debug builds
/// keep the permissive behaviour used by development and tests.
@immutable
class AccountPolicy {
  const AccountPolicy({
    required this.requirePremium,
    required this.hasPremium,
    this.microsoftOnly = false,
    this.region = 'UNKNOWN',
    this.regionSource = 'unknown',
  });

  final bool requirePremium;
  final bool hasPremium;
  final bool microsoftOnly;
  final String region;
  final String regionSource;

  bool get canUseOtherAccounts =>
      !microsoftOnly && (!requirePremium || hasPremium);

  /// Third-party Yggdrasil accounts are real online identities; they are only
  /// blocked when the region requires a Microsoft account, not by the local
  /// "premium first" gate that protects offline profiles.
  bool get canUseAuthlib => !microsoftOnly;
}

final regionPolicyProvider = FutureProvider<RegionPolicyState>((ref) async {
  final client = ref.watch(coreClientProvider);
  if (client == null) {
    return RegionPolicyState(
      region: kReleaseMode ? 'GLOBAL' : 'CN',
      microsoftOnly: kReleaseMode,
      source: 'fallback',
    );
  }
  return client.getRegionPolicy();
});

final accountPolicyProvider = Provider<AccountPolicy>((ref) {
  final accounts = ref.watch(accountsProvider).value ?? const <Account>[];
  final region = ref.watch(regionPolicyProvider).value;
  final hasPremium = accounts.any(
    (account) =>
        account.kind == AccountKind.microsoft &&
        account.uuid?.isNotEmpty == true,
  );
  return AccountPolicy(
    requirePremium: kReleaseMode,
    hasPremium: hasPremium,
    microsoftOnly: region?.microsoftOnly ?? kReleaseMode,
    region: region?.region ?? 'UNKNOWN',
    regionSource: region?.source ?? 'unknown',
  );
});
