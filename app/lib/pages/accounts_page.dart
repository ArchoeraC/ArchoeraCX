import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/account.dart';
import '../services/core/core_client.dart';
import '../stores/accounts.dart';
import '../stores/providers.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_section.dart';
import '../widgets/common/app_page_header.dart';

class AccountsPage extends ConsumerWidget {
  const AccountsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final accountsAsync = ref.watch(accountsProvider);
    final accounts = accountsAsync.value ?? const <Account>[];
    final current = ref.watch(currentAccountProvider);

    return SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AppPageHeader(
            title: l10n.accountsTitle,
            subtitle: l10n.accountsSubtitle,
            actions: [
              FilledButton.icon(
                onPressed: () => _addAccount(context, ref),
                icon: const Icon(Icons.add_rounded, size: 18),
                label: Text(l10n.addAccount),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
          LayoutBuilder(
            builder: (context, constraints) {
              final empty =
                  accounts.isEmpty &&
                  current == null &&
                  !accountsAsync.isLoading &&
                  !accountsAsync.hasError;
              if (empty) {
                return _EmptyAccountsPanel(
                  onMicrosoft: () => _addAccount(context, ref, 'microsoft'),
                  onOffline: () => _addAccount(context, ref, 'offline'),
                );
              }

              final wide = constraints.maxWidth >= 920;
              final summary = current != null
                  ? _CurrentAccountCard(
                      account: current,
                      onAdd: () => _addAccount(context, ref),
                      onRefresh: current.kind == AccountKind.microsoft
                          ? () => _refresh(context, ref, current)
                          : null,
                    )
                  : const _NoAccountPanel();
              final list = _AccountsListPanel(
                accounts: accounts,
                loading: accountsAsync.isLoading,
                error: accountsAsync.hasError && accounts.isEmpty
                    ? l10n.accountsLoadFailed('${accountsAsync.error}')
                    : null,
                onSelect: (account) => _select(context, ref, account),
                onRefresh: (account) => _refresh(context, ref, account),
                onRemove: (account) => _remove(context, ref, account),
              );

              if (wide) {
                return IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      SizedBox(width: 360, child: summary),
                      VerticalDivider(width: 1, color: tokens.border),
                      Expanded(child: list),
                    ],
                  ),
                );
              }

              return Column(
                children: [
                  summary,
                  const SizedBox(height: AppSpacing.md),
                  list,
                ],
              );
            },
          ),
        ],
      ),
    );
  }

  Future<void> _addAccount(
    BuildContext context,
    WidgetRef ref, [
    String? preferred,
  ]) async {
    final l10n = AppLocalizations.of(context);
    var choice = preferred;
    choice ??= await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.window_rounded),
              title: Text(l10n.accountMicrosoftTitle),
              subtitle: Text(l10n.accountMicrosoftDesc),
              onTap: () => Navigator.of(context).pop('microsoft'),
            ),
            ListTile(
              leading: const Icon(Icons.person_rounded),
              title: Text(l10n.accountAddOffline),
              subtitle: Text(l10n.accountOfflineDesc),
              onTap: () => Navigator.of(context).pop('offline'),
            ),
            const SizedBox(height: AppSpacing.sm),
          ],
        ),
      ),
    );

    if (!context.mounted) return;
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
      await client.loginOffline(name.trim());
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
    try {
      await ref.read(accountsProvider.notifier).select(account.id);
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
    final tokens = context.tokens;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          behavior: SnackBarBehavior.floating,
          backgroundColor: error ? tokens.danger : null,
          content: Text(message),
          duration: const Duration(seconds: 3),
        ),
      );
  }
}

class _AccountAvatar extends StatelessWidget {
  const _AccountAvatar({
    required this.account,
    required this.size,
    this.fallbackColor,
    this.fallbackIcon,
  });

  final Account account;
  final double size;
  final Color? fallbackColor;
  final IconData? fallbackIcon;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final fallback = Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: fallbackColor ?? tokens.accentSoft,
        shape: BoxShape.circle,
      ),
      clipBehavior: Clip.antiAlias,
      child: Center(
        child: fallbackIcon == null
            ? Text(
                account.displayName.characters.first.toUpperCase(),
                style: TextStyle(
                  color: tokens.accent,
                  fontSize: size * 0.4,
                  fontWeight: FontWeight.w500,
                ),
              )
            : Icon(fallbackIcon, color: tokens.accent, size: size * 0.5),
      ),
    );

    final skinUrl = account.skinUrl;
    if (skinUrl == null) return fallback;

    return ClipOval(
      child: Image.network(
        skinUrl,
        width: size,
        height: size,
        fit: BoxFit.cover,
        errorBuilder: (context, error, stackTrace) => fallback,
      ),
    );
  }
}

class _CurrentAccountCard extends StatelessWidget {
  const _CurrentAccountCard({
    required this.account,
    required this.onAdd,
    this.onRefresh,
  });

  final Account account;
  final VoidCallback onAdd;
  final VoidCallback? onRefresh;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _AccountAvatar(account: account, size: 56),
              const SizedBox(width: AppSpacing.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      l10n.currentAccount,
                      style: Theme.of(context).textTheme.labelMedium
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                    const SizedBox(height: AppSpacing.xxs),
                    Text(
                      account.displayName,
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      account.kind.labelFor(l10n),
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.textSecondary),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
          Container(
            padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
            decoration: BoxDecoration(
              border: Border(
                top: BorderSide(color: tokens.border),
                bottom: BorderSide(color: tokens.border),
              ),
            ),
            child: Row(
              children: [
                Icon(Icons.shield_outlined, size: 18, color: tokens.success),
                const SizedBox(width: AppSpacing.xs),
                Expanded(
                  child: Text(
                    account.uuid ?? account.kind.labelFor(l10n),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodySmall
                        ?.copyWith(color: tokens.textSecondary),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: AppSpacing.md),
          Row(
            children: [
              if (onRefresh != null)
                OutlinedButton.icon(
                  onPressed: onRefresh,
                  icon: const Icon(Icons.refresh_rounded, size: 17),
                  label: Text(l10n.accountRefresh),
                ),
              if (onRefresh != null) const SizedBox(width: AppSpacing.xs),
              FilledButton.icon(
                onPressed: onAdd,
                icon: const Icon(Icons.add_rounded, size: 18),
                label: Text(l10n.addAccount),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _EmptyAccountsPanel extends StatelessWidget {
  const _EmptyAccountsPanel({
    required this.onMicrosoft,
    required this.onOffline,
  });

  final VoidCallback onMicrosoft;
  final VoidCallback onOffline;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    final intro = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(Icons.person_rounded, size: 30, color: tokens.accent),
        const SizedBox(height: AppSpacing.sm),
        Text(l10n.addAccount, style: textTheme.titleLarge),
        const SizedBox(height: AppSpacing.xs),
        ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: Text(
            l10n.accountsSubtitle,
            style: textTheme.bodyMedium?.copyWith(color: tokens.textSecondary),
          ),
        ),
        const SizedBox(height: AppSpacing.md),
        Text(
          l10n.accountNoAccounts,
          style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
        ),
      ],
    );

    final types = Column(
      children: [
        _AccountTypeTile(
          icon: Icons.window_rounded,
          title: l10n.accountMicrosoftTitle,
          subtitle: l10n.accountMicrosoftDesc,
          color: tokens.success,
          onTap: onMicrosoft,
        ),
        const SizedBox(height: AppSpacing.xs),
        _AccountTypeTile(
          icon: Icons.person_rounded,
          title: l10n.accountAddOffline,
          subtitle: l10n.accountOfflineDesc,
          color: tokens.accent,
          onTap: onOffline,
        ),
      ],
    );

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 760) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                intro,
                const SizedBox(height: AppSpacing.lg),
                Divider(height: 1, color: tokens.border),
                const SizedBox(height: AppSpacing.md),
                types,
              ],
            );
          }

          return IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(child: intro),
                const SizedBox(width: AppSpacing.xl),
                VerticalDivider(width: 1, color: tokens.border),
                const SizedBox(width: AppSpacing.xl),
                SizedBox(width: 360, child: types),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _AccountTypeTile extends StatelessWidget {
  const _AccountTypeTile({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.color,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final Color color;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
          decoration: BoxDecoration(
            border: Border(bottom: BorderSide(color: tokens.border)),
          ),
          child: Row(
            children: [
              Icon(icon, size: 22, color: color),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.titleSmall,
                    ),
                    Text(
                      subtitle,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.textTertiary,
                      ),
                    ),
                  ],
                ),
              ),
              Icon(
                Icons.chevron_right_rounded,
                size: 18,
                color: tokens.textTertiary,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _NoAccountPanel extends StatelessWidget {
  const _NoAccountPanel();

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.person_rounded, size: 26, color: tokens.accent),
          const SizedBox(height: AppSpacing.sm),
          Text(
            l10n.currentAccount,
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: AppSpacing.xs),
          Text(
            l10n.accountNoAccounts,
            style: Theme.of(context).textTheme.bodyMedium
                ?.copyWith(color: tokens.textSecondary),
          ),
        ],
      ),
    );
  }
}

class _AccountsListPanel extends StatelessWidget {
  const _AccountsListPanel({
    required this.accounts,
    required this.loading,
    required this.error,
    required this.onSelect,
    required this.onRefresh,
    required this.onRemove,
  });

  final List<Account> accounts;
  final bool loading;
  final String? error;
  final ValueChanged<Account> onSelect;
  final ValueChanged<Account> onRefresh;
  final ValueChanged<Account> onRemove;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  l10n.accountsTitle,
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              if (loading)
                const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              else
                Text(
                  '${accounts.length}',
                  style: Theme.of(context).textTheme.labelMedium
                      ?.copyWith(color: tokens.textTertiary),
                ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          if (error != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
              child: Text(
                error!,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.danger),
              ),
            )
          else if (accounts.isEmpty && !loading)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
              child: Column(
                children: [
                  Text(
                    l10n.accountNoAccounts,
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.bodyMedium
                        ?.copyWith(color: tokens.textSecondary),
                  ),
                ],
              ),
            )
          else
            for (var i = 0; i < accounts.length; i++) ...[
              _AccountTile(
                account: accounts[i],
                onSelect: () => onSelect(accounts[i]),
                onRefresh: accounts[i].kind == AccountKind.microsoft
                    ? () => onRefresh(accounts[i])
                    : null,
                onRemove: () => onRemove(accounts[i]),
              ),
              if (i < accounts.length - 1)
                Divider(height: 1, color: tokens.border),
            ],
        ],
      ),
    );
  }
}

class _AccountTile extends StatelessWidget {
  const _AccountTile({
    required this.account,
    required this.onSelect,
    required this.onRemove,
    this.onRefresh,
  });

  final Account account;
  final VoidCallback onSelect;
  final VoidCallback onRemove;
  final VoidCallback? onRefresh;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final color = switch (account.kind) {
      AccountKind.microsoft => tokens.success,
      AccountKind.offline => tokens.accent,
      AccountKind.authlib => tokens.warning,
    };
    final icon = switch (account.kind) {
      AccountKind.microsoft => Icons.window_rounded,
      AccountKind.offline => Icons.person_rounded,
      AccountKind.authlib => Icons.hub_rounded,
    };

    return Material(
      color: account.active ? tokens.accentSoft : Colors.transparent,
      child: InkWell(
        onTap: onSelect,
        hoverColor: tokens.surface2,
        child: Container(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.xs,
            vertical: AppSpacing.sm,
          ),
          decoration: BoxDecoration(
            border: Border(
              left: BorderSide(
                color: account.active ? tokens.accent : Colors.transparent,
                width: 2,
              ),
            ),
          ),
          child: Row(
            children: [
              _AccountAvatar(
                account: account,
                size: 40,
                fallbackColor: color.withValues(alpha: 0.14),
                fallbackIcon: icon,
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      account.displayName,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    Text(
                      account.uuid ?? account.kind.labelFor(l10n),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                  ],
                ),
              ),
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (account.active)
                    Text(
                      l10n.currentAccount,
                      style: Theme.of(context).textTheme.labelSmall
                          ?.copyWith(color: tokens.accent, letterSpacing: 0),
                    ),
                  PopupMenuButton<String>(
                    tooltip: l10n.accountsTitle,
                    onSelected: (value) {
                      if (value == 'refresh') onRefresh?.call();
                      if (value == 'remove') onRemove();
                    },
                    itemBuilder: (context) => [
                      if (onRefresh != null)
                        PopupMenuItem(
                          value: 'refresh',
                          child: ListTile(
                            contentPadding: EdgeInsets.zero,
                            leading: const Icon(
                              Icons.refresh_rounded,
                              size: 20,
                            ),
                            title: Text(l10n.accountRefresh),
                          ),
                        ),
                      PopupMenuItem(
                        value: 'remove',
                        child: ListTile(
                          contentPadding: EdgeInsets.zero,
                          leading: const Icon(
                            Icons.delete_outline_rounded,
                            size: 20,
                          ),
                          title: Text(l10n.accountRemove),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Device-code dialog. It starts the flow, displays the user code, and waits
/// for the C# module to complete the token chain. Closing it cancels the flow.
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

  @override
  void initState() {
    super.initState();
    unawaited(_begin());
  }

  Future<void> _begin() async {
    try {
      final start = await widget.client.startMicrosoftLogin();
      if (!mounted) {
        await _cancelFlow(start.flowId);
        return;
      }
      setState(() {
        _start = start;
        _flowId = start.flowId;
        _busy = false;
      });
      final account = await widget.client.pollMicrosoftLogin(start.flowId);
      if (!mounted) return;
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
            else if (start != null) ...[
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
        if (start != null)
          TextButton.icon(
            onPressed: () async {
              await Clipboard.setData(ClipboardData(text: start.userCode));
              if (context.mounted) {
                ScaffoldMessenger.of(context)
                  ..hideCurrentSnackBar()
                  ..showSnackBar(
                    SnackBar(
                      behavior: SnackBarBehavior.floating,
                      content: Text(l10n.accountDeviceCodeCopied),
                    ),
                  );
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
