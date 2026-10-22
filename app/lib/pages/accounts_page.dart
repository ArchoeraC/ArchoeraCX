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
import '../stores/providers.dart';
import '../theme/tokens.dart';
import '../widgets/accounts/accounts_list.dart';
import '../widgets/accounts/accounts_status_panel.dart';
import '../widgets/accounts/add_account_sheet.dart';
import '../widgets/accounts/current_account_panel.dart';
import '../widgets/accounts/empty_accounts_panel.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/toast.dart';

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
        icon: Icons.person_rounded,
        message: l10n.accountsTitle,
        busy: true,
      );
    } else if (loadFailed) {
      body = AccountsStatusPanel(
        icon: Icons.error_outline_rounded,
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
      body = Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (current != null) ...[
            CurrentAccountPanel(
              account: current,
              onRefresh: current.kind == AccountKind.microsoft
                  ? () => _refresh(context, ref, current)
                  : null,
            ),
            const SizedBox(height: AppSpacing.lg),
          ],
          AccountsListSection(
            accounts: accounts,
            loading: accountsAsync.isLoading,
            error: accountsAsync.hasError
                ? l10n.accountsLoadFailed('${accountsAsync.error}')
                : null,
            onSelect: (account) => _select(context, ref, account),
            onRefresh: (account) => _refresh(context, ref, account),
            onRemove: (account) => _remove(context, ref, account),
          ),
        ],
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
                icon: const Icon(Icons.add_rounded, size: 18),
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
                    Icons.verified_user_outlined,
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
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(l10n.accountRemove),
        content: Text(l10n.accountRemoveConfirm(account.displayName)),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(l10n.commonCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(l10n.commonDelete),
          ),
        ],
      ),
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

  static String _message(Object error) {
    if (error is CoreException) {
      return error.detail == null || error.detail!.isEmpty
          ? error.message
          : '${error.message} (${error.detail})';
    }
    return error.toString();
  }

  static void _notify(
    BuildContext context,
    String message, {
    bool error = false,
  }) {
    toast(message, type: error ? ToastType.error : ToastType.success);
  }
}

/// Microsoft sign-in dialog. It starts the authorization-code flow, opens the
/// system browser, and waits for the C# module to complete the token chain.
/// When the backend falls back to device code, the code UI is shown instead.
/// Closing it cancels the flow.
class DeviceLoginDialog extends StatefulWidget {
  const DeviceLoginDialog({super.key, required this.client});

  final CoreClient client;

  @override
  State<DeviceLoginDialog> createState() => _DeviceLoginDialogState();
}

class _DeviceLoginDialogState extends State<DeviceLoginDialog> {
  MicrosoftDeviceCodeStart? _start;
  String? _error;
  String? _flowId;
  bool _busy = true;
  bool _completed = false;
  bool _browserLaunchFailed = false;

  @override
  void initState() {
    super.initState();
    unawaited(_begin());
  }

  Future<void> _begin() async {
    try {
      final start = await widget.client.startMicrosoftLogin(
        flow: 'authorization_code',
      );
      if (!mounted) {
        await _cancelFlow(start.flowId);
        return;
      }
      setState(() {
        _start = start;
        _flowId = start.flowId;
        _busy = false;
      });
      if (start.isAuthorizationCode) {
        await _openAuthorizationUrl(start);
      }
      final account = await widget.client.pollMicrosoftLogin(start.flowId);
      if (!mounted) return;
      final l10n = AppLocalizations.of(context);
      unawaited(
        widget.client.showSystemNotification(
          title: l10n.accountLoginSuccessNotificationTitle,
          body: l10n.accountLoginSuccessNotificationBody(account.displayName),
        ),
      );
      _completed = true;
      Navigator.of(context).pop(account);
    } catch (error) {
      if (!mounted) return;
      if (error is CoreException && error.code == 'E_CANCELLED') {
        Navigator.of(context).pop();
        return;
      }
      setState(() {
        _busy = false;
        _error = _formatError(error);
      });
    }
  }

  /// Opens the authorization URL in the system browser. Failures are shown in
  /// the dialog instead of thrown, and the URL stays visible for manual use.
  Future<void> _openAuthorizationUrl(MicrosoftDeviceCodeStart start) async {
    final authorizationUrl = start.authorizationUrl;
    if (authorizationUrl == null || authorizationUrl.isEmpty) return;
    var opened = false;
    try {
      opened = await widget.client.openExternalUrl(authorizationUrl);
    } catch (_) {
      opened = false;
    }
    if (!opened) {
      // Older cores or an unavailable desktop portal fall back to Flutter's
      // system-browser bridge instead of leaving the user without a browser.
      try {
        final uri = Uri.tryParse(authorizationUrl);
        if (uri != null) {
          opened = await launchUrl(uri, mode: LaunchMode.externalApplication);
        }
      } catch (_) {
        opened = false;
      }
    }
    if (!mounted) return;
    setState(() => _browserLaunchFailed = !opened);
  }

  Future<void> _cancelFlow(String flowId) async {
    try {
      await widget.client.cancelMicrosoftLogin(flowId);
    } catch (_) {
      // The flow may already have completed or expired.
    }
  }

  Future<void> _cancel() async {
    _completed = true;
    final flowId = _flowId;
    if (flowId != null) {
      await _cancelFlow(flowId);
    }
    if (mounted) Navigator.of(context).pop();
  }

  @override
  void dispose() {
    if (!_completed && _flowId != null) {
      unawaited(_cancelFlow(_flowId!));
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final start = _start;

    return AlertDialog(
      title: Text(l10n.accountMicrosoftTitle),
      content: SizedBox(
        width: 380,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (start == null && _error == null)
              Row(
                children: [
                  const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(child: Text(l10n.accountDeviceCodeWaiting)),
                ],
              )
            else if (start != null && start.isAuthorizationCode) ...[
              Text(
                l10n.accountBrowserSignInHint,
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              const SizedBox(height: AppSpacing.md),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(AppSpacing.md),
                decoration: BoxDecoration(
                  color: tokens.surface2,
                  borderRadius: BorderRadius.circular(tokens.radiusM),
                ),
                child: SelectableText(
                  start.authorizationUrl ?? '',
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.accent),
                ),
              ),
              if (_browserLaunchFailed) ...[
                const SizedBox(height: AppSpacing.sm),
                Text(
                  l10n.accountBrowserOpenFailed,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.danger),
                ),
              ],
              const SizedBox(height: AppSpacing.md),
              Row(
                children: [
                  const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(
                    child: Text(
                      l10n.accountBrowserWaitingCallback,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
                ],
              ),
            ] else if (start != null) ...[
              Text(
                l10n.accountDeviceCodeHint(start.verificationUri),
                style: Theme.of(context).textTheme.bodyMedium,
              ),
              const SizedBox(height: AppSpacing.md),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(AppSpacing.md),
                decoration: BoxDecoration(
                  color: tokens.surface2,
                  borderRadius: BorderRadius.circular(tokens.radiusM),
                ),
                child: SelectableText(
                  start.userCode,
                  textAlign: TextAlign.center,
                  style: Theme.of(context).textTheme.headlineSmall
                      ?.copyWith(fontWeight: FontWeight.w500, letterSpacing: 4),
                ),
              ),
              const SizedBox(height: AppSpacing.sm),
              SelectableText(
                start.verificationUri,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.accent),
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                l10n.accountDeviceCodeExpires((start.expiresIn / 60).ceil()),
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
              const SizedBox(height: AppSpacing.md),
              Row(
                children: [
                  const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(
                    child: Text(
                      l10n.accountDeviceCodeWaiting,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ),
                ],
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: AppSpacing.sm),
              Text(
                _error!,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.danger),
              ),
            ],
          ],
        ),
      ),
      actions: [
        if (start != null && start.isAuthorizationCode)
          TextButton.icon(
            onPressed: () => _openAuthorizationUrl(start),
            icon: const Icon(Icons.open_in_new_rounded, size: 18),
            label: Text(l10n.accountOpenBrowser),
          ),
        if (start != null && !start.isAuthorizationCode)
          TextButton.icon(
            onPressed: () async {
              await Clipboard.setData(ClipboardData(text: start.userCode));
              if (context.mounted) {
                toast(l10n.accountDeviceCodeCopied, type: ToastType.success);
              }
            },
            icon: const Icon(Icons.copy_rounded, size: 18),
            label: Text(l10n.commonCopy),
          ),
        TextButton(
          onPressed: _busy && _start == null ? null : _cancel,
          child: Text(l10n.accountCancelSignIn),
        ),
      ],
    );
  }

  static String _formatError(Object error) {
    if (error is CoreException) {
      return error.detail == null || error.detail!.isEmpty
          ? error.message
          : '${error.message} (${error.detail})';
    }
    return error.toString();
  }
}
