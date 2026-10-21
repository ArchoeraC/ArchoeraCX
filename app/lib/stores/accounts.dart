import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/account.dart';
import '../services/core/core_client.dart';
import 'providers.dart';

/// Accounts are owned by C#; this provider only mirrors the metadata list.
final accountsProvider = AsyncNotifierProvider<AccountsNotifier, List<Account>>(
  AccountsNotifier.new,
);

final currentAccountProvider = Provider<Account?>((ref) {
  final accounts = ref.watch(accountsProvider).value ?? const <Account>[];
  for (final account in accounts) {
    if (account.active) return account;
  }
  return null;
});

class AccountsNotifier extends AsyncNotifier<List<Account>> {
  @override
  Future<List<Account>> build() => _client.listAccounts();

  CoreClient get _client {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      throw const CoreException(
        status: 0,
        code: 'CORE_UNAVAILABLE',
        message: 'The native core is not available.',
      );
    }
    return client;
  }

  Future<void> reload() async {
    state = const AsyncLoading();
    state = await AsyncValue.guard(_client.listAccounts);
  }

  Future<void> select(String id) async {
    await _client.selectAccount(id);
    await reload();
  }

  Future<void> refresh(String id) async {
    await _client.refreshAccount(id);
    await reload();
  }

  Future<void> remove(String id) async {
    await _client.removeAccount(id);
    await reload();
  }
}
