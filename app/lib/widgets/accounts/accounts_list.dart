import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/account.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/app_panel.dart';
import '../common/app_menu_button.dart';
import '../common/context_menu.dart';
import 'account_avatar.dart';

/// Full-width account list: one header rule, flat rows, no nested cards.
class AccountsListSection extends StatelessWidget {
  const AccountsListSection({
    super.key,
    required this.accounts,
    required this.loading,
    required this.onSelect,
    required this.onRefresh,
    required this.onRemove,
    this.error,
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
    final textTheme = Theme.of(context).textTheme;

    return AppPanel(
      padding: EdgeInsets.zero,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.lg,
              vertical: AppSpacing.sm,
            ),
            child: Row(
              children: [
                Container(width: 14, height: 2, color: tokens.accent),
                const SizedBox(width: AppSpacing.xs),
                Text(
                  l10n.statAccounts,
                  style: textTheme.labelMedium?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
                const SizedBox(width: AppSpacing.xs),
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 6,
                    vertical: 1,
                  ),
                  decoration: ShapeDecoration(
                    color: tokens.surface2,
                    shape: BeveledRectangleBorder(
                      borderRadius: BorderRadius.only(
                        topRight: Radius.circular(tokens.bevelS * 0.5),
                        bottomLeft: Radius.circular(tokens.bevelS * 0.4),
                      ),
                    ),
                  ),
                  child: Text(
                    '${accounts.length}',
                    style: textTheme.labelSmall?.copyWith(
                      color: tokens.textSecondary,
                      letterSpacing: 0,
                    ),
                  ),
                ),
                const Spacer(),
                if (loading)
                  const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
              ],
            ),
          ),
          Divider(height: 1, color: tokens.border),
          if (error != null)
            Padding(
              padding: const EdgeInsets.symmetric(
                horizontal: AppSpacing.lg,
                vertical: AppSpacing.sm,
              ),
              child: Text(
                error!,
                style: textTheme.bodySmall?.copyWith(color: tokens.danger),
              ),
            ),
          if (accounts.isEmpty && !loading)
            Padding(
              padding: const EdgeInsets.symmetric(
                horizontal: AppSpacing.lg,
                vertical: AppSpacing.xl,
              ),
              child: Text(
                l10n.accountNoAccounts,
                textAlign: TextAlign.center,
                style: textTheme.bodyMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
            )
          else
            for (var i = 0; i < accounts.length; i++) ...[
              AccountRow(
                account: accounts[i],
                onSelect: () => onSelect(accounts[i]),
                onRefresh: accounts[i].kind == AccountKind.microsoft
                    ? () => onRefresh(accounts[i])
                    : null,
                onRemove: () => onRemove(accounts[i]),
              ),
              if (i < accounts.length - 1)
                Divider(
                  height: 1,
                  indent: AppSpacing.lg,
                  endIndent: AppSpacing.lg,
                  color: tokens.border,
                ),
            ],
        ],
      ),
    );
  }
}

class AccountRow extends StatelessWidget {
  const AccountRow({
    super.key,
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
    final textTheme = Theme.of(context).textTheme;
    final active = account.active;
    final meta = account.kind.labelFor(l10n);
    final menu = _AccountMenu(
      l10n: l10n,
      onRefresh: onRefresh,
      onRemove: onRemove,
    );

    return GestureDetector(
      // Right-clicking a row is the desktop shortcut for the same actions as
      // the "more" button.
      onSecondaryTapDown: (details) {
        onSelect();
        menu.showAt(context, details.globalPosition);
      },
      child: Material(
        color: active ? tokens.accentSoft : Colors.transparent,
        child: InkWell(
          onTap: onSelect,
          hoverColor: tokens.surface2,
          child: Container(
            decoration: BoxDecoration(
              border: Border(
                left: BorderSide(
                  color: active ? tokens.accent : Colors.transparent,
                  width: 2,
                ),
              ),
            ),
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.lg,
              vertical: AppSpacing.sm,
            ),
            child: LayoutBuilder(
              builder: (context, constraints) {
                final showActiveLabel = constraints.maxWidth >= 560;
                return Row(
                  children: [
                    AccountAvatar(account: account, size: 40),
                    const SizedBox(width: AppSpacing.sm),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            account.displayName,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: textTheme.titleSmall,
                          ),
                          const SizedBox(height: 1),
                          Text(
                            meta,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: textTheme.bodySmall?.copyWith(
                              color: tokens.textTertiary,
                            ),
                          ),
                        ],
                      ),
                    ),
                    if (active) ...[
                      const SizedBox(width: AppSpacing.xs),
                      if (showActiveLabel)
                        Text(
                          l10n.currentAccount,
                          style: textTheme.labelSmall?.copyWith(
                            color: tokens.accent,
                            letterSpacing: 0,
                          ),
                        ),
                      const SizedBox(width: AppSpacing.xxs),
                      Icon(AppIcons.completed, size: 17, color: tokens.accent),
                    ],
                    menu,
                  ],
                );
              },
            ),
          ),
        ),
      ),
    );
  }
}

/// Overflow menu shared by the account row's "more" button and its secondary
/// click. Keeping one item list and one dispatcher prevents the two entry
/// points from drifting apart.
class _AccountMenu extends StatelessWidget {
  const _AccountMenu({
    required this.l10n,
    required this.onRefresh,
    required this.onRemove,
  });

  final AppLocalizations l10n;
  final VoidCallback? onRefresh;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    return AppMenuButton<String>(
      tooltip: l10n.accountsTitle,
      icon: AppIcons.more,
      items: _items(),
      onSelected: _dispatch,
    );
  }

  Future<void> showAt(BuildContext context, Offset globalPosition) async {
    final value = await showContextMenuAt<String>(
      context,
      globalPosition: globalPosition,
      items: appMenuEntries(context, _items()),
    );
    if (value != null) _dispatch(value);
  }

  List<AppMenuEntry<String>> _items() {
    return [
      if (onRefresh != null)
        AppMenuEntry(
          value: 'refresh',
          label: l10n.accountRefresh,
          icon: AppIcons.refresh,
        ),
      AppMenuEntry(
        value: 'remove',
        label: l10n.accountRemove,
        icon: AppIcons.delete,
        danger: true,
      ),
    ];
  }

  void _dispatch(String value) {
    if (value == 'refresh') onRefresh?.call();
    if (value == 'remove') onRemove();
  }
}
