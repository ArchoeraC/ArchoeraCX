import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../theme/tokens.dart';
import '../common/app_panel.dart';
import 'account_avatar.dart';
import 'account_type_action.dart';

/// Single empty-state panel: intro on the left, the two add-account choices on
/// the right. Replaces the old duplicated intro + account-type tiles.
class EmptyAccountsPanel extends StatelessWidget {
  const EmptyAccountsPanel({
    super.key,
    required this.onMicrosoft,
    required this.onOffline,
    this.offlineHint,
  });

  final VoidCallback onMicrosoft;
  final VoidCallback onOffline;
  final String? offlineHint;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    final intro = Padding(
      padding: const EdgeInsets.all(AppSpacing.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          const AccountMark(icon: Icons.person_rounded, size: 48),
          const SizedBox(height: AppSpacing.md),
          Text(l10n.addAccount, style: textTheme.titleLarge),
          const SizedBox(height: AppSpacing.xs),
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 460),
            child: Text(
              l10n.accountNoAccounts,
              style: textTheme.bodyMedium?.copyWith(
                color: tokens.textSecondary,
              ),
            ),
          ),
        ],
      ),
    );

    final types = Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        AccountTypeAction(
          icon: Icons.window_rounded,
          title: l10n.accountMicrosoftTitle,
          subtitle: l10n.accountMicrosoftDesc,
          color: tokens.success,
          onTap: onMicrosoft,
        ),
        Divider(height: 1, color: tokens.border),
        AccountTypeAction(
          icon: Icons.person_rounded,
          title: l10n.accountAddOffline,
          subtitle: offlineHint ?? l10n.accountOfflineDesc,
          color: tokens.accent,
          onTap: onOffline,
        ),
      ],
    );

    return AppPanel(
      padding: EdgeInsets.zero,
      accent: true,
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 760) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                intro,
                Divider(height: 1, color: tokens.border),
                types,
              ],
            );
          }

          return IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Expanded(child: intro),
                Container(width: 1, color: tokens.border),
                SizedBox(width: 380, child: Center(child: types)),
              ],
            ),
          );
        },
      ),
    );
  }
}
