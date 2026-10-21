import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../theme/tokens.dart';
import 'account_type_action.dart';

/// Bottom sheet that offers the two account types.
class AddAccountSheet extends StatelessWidget {
  const AddAccountSheet({
    super.key,
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

    return SafeArea(
      top: false,
      child: Align(
        alignment: Alignment.topCenter,
        heightFactor: 1,
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 560),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(
                  AppSpacing.lg,
                  AppSpacing.md,
                  AppSpacing.lg,
                  AppSpacing.sm,
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(l10n.addAccount, style: textTheme.titleMedium),
                    const SizedBox(height: 2),
                    Text(
                      l10n.accountsSubtitle,
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.textTertiary,
                      ),
                    ),
                  ],
                ),
              ),
              Divider(height: 1, color: tokens.border),
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
                subtitle: l10n.accountOfflineDesc,
                color: tokens.accent,
                onTap: onOffline,
              ),
              Divider(height: 1, color: tokens.border),
              const SizedBox(height: AppSpacing.xs),
            ],
          ),
        ),
      ),
    );
  }
}
