import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../l10n/generated/app_localizations.dart';
import '../models/account.dart';
import '../services/core/core_client.dart';
import '../stores/account_policy.dart';
import '../stores/accounts.dart';
import '../stores/preferences.dart';
import '../stores/providers.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../widgets/accounts/accounts_list.dart';
import '../widgets/accounts/accounts_status_panel.dart';
import '../widgets/accounts/add_account_sheet.dart';
import '../widgets/accounts/current_account_panel.dart';
import '../widgets/accounts/empty_accounts_panel.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/confirm.dart';
import '../widgets/common/safe_url.dart';
import '../widgets/common/toast.dart';

part 'accounts/device_login_dialog.dart';

class AccountsPage extends ConsumerWidget {
  const AccountsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final accountsAsync = ref.watch(accountsProvider);
    final accounts = accountsAsync.value ?? const <Account>[];
    final current = ref.watch(currentAccountProvider);
    final policy = ref.watch(accountPolicyProvider);
    final blockedHint = policy.microsoftOnly
        ? l10n.accountMicrosoftOnlyHint
        : l10n.accountPremiumRequiredHint;
    final releaseNotice = policy.microsoftOnly
        ? l10n.accountMicrosoftOnlyHint
        : l10n.accountVerifiedRequiredNotice;
    final loadFailed = accountsAsync.hasError && accounts.isEmpty;
    final empty =
        accounts.isEmpty &&
        current == null &&
        !accountsAsync.isLoading &&
        !accountsAsync.hasError;

    final Widget body;
    if (accountsAsync.isLoading && accounts.isEmpty) {
      body = AccountsStatusPanel(
        icon: AppIcons.accounts,
        message: l10n.accountsTitle,
        busy: true,
      );
    } else if (loadFailed) {
      body = AccountsStatusPanel(
        icon: AppIcons.error,
        message: l10n.accountsLoadFailed('${accountsAsync.error}'),
        danger: true,
        onRetry: () => ref.read(accountsProvider.notifier).reload(),
        retryLabel: l10n.refresh,
      );
    } else if (empty) {
      body = EmptyAccountsPanel(
        onMicrosoft: () => _addAccount(context, ref, 'microsoft'),
        onOffline: () => _addAccount(context, ref, 'offline'),
        offlineHint: policy.canUseOtherAccounts ? null : blockedHint,
        showOffline: !policy.microsoftOnly,
        emptyMessage: policy.microsoftOnly
            ? l10n.accountNoAccountsMicrosoftOnly
            : null,
      );
    } else {
      final currentAccount = current;
      final panel = currentAccount == null
          ? null
          : CurrentAccountPanel(
              account: currentAccount,
              onRefresh: currentAccount.kind == AccountKind.microsoft
                  ? () => _refresh(context, ref, currentAccount)
                  : null,
            );
      final list = AccountsListSection(
        accounts: accounts,
        loading: accountsAsync.isLoading,
        error: accountsAsync.hasError
            ? l10n.accountsLoadFailed('${accountsAsync.error}')
            : null,
        onSelect: (account) => _select(context, ref, account),
        onRefresh: (account) => _refresh(context, ref, account),
        onRemove: (account) => _remove(context, ref, account),
      );

      body = LayoutBuilder(
        builder: (context, constraints) {
          // On a fullscreen window the hero alone leaves the right half empty;
          // place the account list beside it instead of underneath.
          if (panel == null || constraints.maxWidth < 1400) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (panel != null) ...[
                  panel,
                  const SizedBox(height: AppSpacing.lg),
                ],
                list,
              ],
            );
          }

          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(flex: 3, child: panel),
              const SizedBox(width: AppSpacing.lg),
              Expanded(flex: 2, child: list),
            ],
          );
        },
      );
    }

    return SingleChildScrollView(
      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppPageHeader(
            title: l10n.accountsTitle,
            subtitle: policy.microsoftOnly
                ? l10n.accountsSubtitleMicrosoftOnly
                : l10n.accountsSubtitle,
            actions: [
              FilledButton.icon(
                onPressed: () => _addAccount(context, ref),
                icon: const Icon(AppIcons.add, size: 18),
                label: Text(l10n.addAccount),
              ),
            ],
          ),
          if (policy.microsoftOnly ||
              (policy.requirePremium && !policy.hasPremium)) ...[
            Container(
              padding: const EdgeInsets.all(AppSpacing.sm),
              decoration: BoxDecoration(
                color: context.tokens.warning.withValues(alpha: 0.10),
                border: Border.all(
                  color: context.tokens.warning.withValues(alpha: 0.45),
                ),
              ),
              child: Row(
                children: [
                  Icon(
                    AppIcons.warning,
                    size: 18,
                    color: context.tokens.warning,
                  ),
                  const SizedBox(width: AppSpacing.xs),
                  Expanded(
                    child: Text(
                      releaseNotice,
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: context.tokens.warning),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: AppSpacing.md),
          ],
          body,
        ],
      ),
    );
  }

  Future<void> _addAccount(
    BuildContext context,
    WidgetRef ref, [
    String? preferred,
  ]) async {
    var choice = preferred;
    choice ??= await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (sheetContext) => AddAccountSheet(
        onMicrosoft: () => Navigator.of(sheetContext).pop('microsoft'),
        onOffline: () => Navigator.of(sheetContext).pop('offline'),
        offlineHint: ref.read(accountPolicyProvider).canUseOtherAccounts
            ? null
            : (ref.read(accountPolicyProvider).microsoftOnly
                  ? AppLocalizations.of(context).accountMicrosoftOnlyHint
                  : AppLocalizations.of(context).accountPremiumRequiredHint),
      ),
    );

    if (!context.mounted) return;
    if (choice == 'offline' &&
        !ref.read(accountPolicyProvider).canUseOtherAccounts) {
      _notify(
        context,
        AppLocalizations.of(context).accountPremiumRequired,
        error: true,
      );
      return;
    }
    if (choice == 'microsoft') {
      await _loginMicrosoft(context, ref);
    } else if (choice == 'offline') {
      await _loginOffline(context, ref);
    }
  }

  Future<void> _loginMicrosoft(BuildContext context, WidgetRef ref) async {
    final l10n = AppLocalizations.of(context);
    final client = ref.read(coreClientProvider);
    if (client == null) {
      _notify(context, l10n.commonCoreNotConnected, error: true);
      return;
    }

    final account = await showDialog<Account>(
      context: context,
      barrierDismissible: false,
      builder: (context) => DeviceLoginDialog(client: client),
    );
    if (account == null || !context.mounted) return;

    await ref.read(accountsProvider.notifier).reload();
    if (context.mounted) {
      _notify(context, l10n.accountSelected);
    }
  }

  Future<void> _loginOffline(BuildContext context, WidgetRef ref) async {
    final l10n = AppLocalizations.of(context);
    if (!ref.read(accountPolicyProvider).canUseOtherAccounts) {
      final policy = ref.read(accountPolicyProvider);
      _notify(
        context,
        policy.microsoftOnly
            ? l10n.accountMicrosoftOnly
            : l10n.accountPremiumRequired,
        error: true,
      );
      return;
    }
    final client = ref.read(coreClientProvider);
    if (client == null) {
      _notify(context, l10n.commonCoreNotConnected, error: true);
      return;
    }

    final controller = TextEditingController();
    final name = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(l10n.accountAddOffline),
        content: TextField(
          controller: controller,
          autofocus: true,
          maxLength: 16,
          decoration: InputDecoration(
            labelText: l10n.accountOfflineNameLabel,
            hintText: l10n.accountOfflineNameHint,
          ),
          onSubmitted: (value) {
            if (value.trim().length >= 3) Navigator.of(context).pop(value);
          },
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: Text(l10n.commonCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(controller.text),
            child: Text(l10n.commonAdd),
          ),
        ],
      ),
    );
    controller.dispose();

    if (name == null || name.trim().length < 3 || !context.mounted) return;
    try {
      final policy = ref.read(accountPolicyProvider);
      await client.loginOffline(
        name.trim(),
        requirePremium: policy.requirePremium,
        requireMicrosoft: policy.microsoftOnly,
      );
      await ref.read(accountsProvider.notifier).reload();
      if (context.mounted) _notify(context, l10n.accountSelected);
    } catch (error) {
      if (context.mounted) {
        _notify(context, l10n.accountLoginFailed(_message(error)), error: true);
      }
    }
  }

  Future<void> _select(
    BuildContext context,
    WidgetRef ref,
    Account account,
  ) async {
    if (account.active) return;
    final l10n = AppLocalizations.of(context);
    if (account.kind != AccountKind.microsoft &&
        !ref.read(accountPolicyProvider).canUseOtherAccounts) {
      _notify(context, l10n.accountPremiumRequired, error: true);
      return;
    }
    try {
      await ref
          .read(accountsProvider.notifier)
          .select(
            account.id,
            requirePremium: ref.read(accountPolicyProvider).requirePremium,
          );
      if (context.mounted) _notify(context, l10n.accountSelected);
    } catch (error) {
      if (context.mounted) {
        _notify(context, _message(error), error: true);
      }
    }
  }

  Future<void> _refresh(
    BuildContext context,
    WidgetRef ref,
    Account account,
  ) async {
    final l10n = AppLocalizations.of(context);
    try {
      await ref.read(accountsProvider.notifier).refresh(account.id);
      if (context.mounted) _notify(context, l10n.accountSessionRefreshed);
    } catch (error) {
      if (context.mounted) {
        _notify(context, l10n.accountLoginFailed(_message(error)), error: true);
      }
    }
  }

  Future<void> _remove(
    BuildContext context,
    WidgetRef ref,
    Account account,
  ) async {
    final l10n = AppLocalizations.of(context);
    final confirmed = await confirmDestructive(
      context,
      l10n,
      title: l10n.accountRemove,
      body: l10n.accountRemoveConfirm(account.displayName),
      confirmLabel: l10n.commonDelete,
    );
    if (confirmed != true || !context.mounted) return;

    try {
      await ref.read(accountsProvider.notifier).remove(account.id);
      if (context.mounted) _notify(context, l10n.accountRemoved);
    } catch (error) {
      if (context.mounted) {
        _notify(context, _message(error), error: true);
      }
    }
  }

  static void _notify(
    BuildContext context,
    String message, {
    bool error = false,
  }) {
    toast(message, type: error ? ToastType.error : ToastType.success);
  }
}

/// Presentable text for a CoreClient failure: the message plus the backend
/// detail when present. One shared copy for the page and the sign-in dialog.
String _message(Object error) {
  if (error is CoreException) {
    return error.detail == null || error.detail!.isEmpty
        ? error.message
        : '${error.message} (${error.detail})';
  }
  return error.toString();
}
