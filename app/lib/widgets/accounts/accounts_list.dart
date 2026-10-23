import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/account.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/app_panel.dart';
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

    return Material(
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
                    Icon(
                      AppIcons.completed,
                      size: 17,
                      color: tokens.accent,
                    ),
                  ],
                  PopupMenuButton<String>(
                    tooltip: l10n.accountsTitle,
                    icon: Icon(
                      AppIcons.more,
                      color: tokens.textSecondary,
                    ),
                    onSelected: (value) {
                      if (value == 'refresh') onRefresh?.call();
                      if (value == 'remove') onRemove();
                    },
                    itemBuilder: (context) => [
                      if (onRefresh != null)
                        PopupMenuItem(
                          value: 'refresh',
                          child: Row(
                            children: [
                              const Icon(AppIcons.refresh, size: 20),
                              const SizedBox(width: AppSpacing.xs),
                              Text(l10n.accountRefresh),
                            ],
                          ),
                        ),
                      PopupMenuItem(
                        value: 'remove',
                        child: Row(
                          children: [
                            Icon(
                              AppIcons.delete,
                              size: 20,
                              color: tokens.danger,
                            ),
                            const SizedBox(width: AppSpacing.xs),
                            Text(
                              l10n.accountRemove,
                              style: TextStyle(color: tokens.danger),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ],
              );
            },
          ),
        ),
      ),
    );
  }
}
