import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/account.dart';
import 'accounts.dart';

/// Product-wide account policy.
///
/// Release builds require a verified Microsoft/Minecraft account before
/// offline or other local identities may be added or selected. Debug builds
/// keep the permissive behaviour used by development and tests.
@immutable
class AccountPolicy {
  const AccountPolicy({required this.requirePremium, required this.hasPremium});

  final bool requirePremium;
  final bool hasPremium;

  bool get canUseOtherAccounts => !requirePremium || hasPremium;
}

final accountPolicyProvider = Provider<AccountPolicy>((ref) {
  final accounts = ref.watch(accountsProvider).value ?? const <Account>[];
  final hasPremium = accounts.any(
    (account) =>
        account.kind == AccountKind.microsoft &&
        account.uuid?.isNotEmpty == true,
  );
  return AccountPolicy(requirePremium: kReleaseMode, hasPremium: hasPremium);
});
