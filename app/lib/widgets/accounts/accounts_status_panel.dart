import 'package:flutter/material.dart';

import '../../theme/tokens.dart';
import '../common/app_panel.dart';
import 'account_avatar.dart';

/// Inline loading / error panel for the accounts page.
class AccountsStatusPanel extends StatelessWidget {
  const AccountsStatusPanel({
    super.key,
    required this.icon,
    required this.message,
    this.busy = false,
    this.danger = false,
    this.onRetry,
    this.retryLabel,
  });

  final IconData icon;
  final String message;
  final bool busy;
  final bool danger;
  final VoidCallback? onRetry;
  final String? retryLabel;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final color = danger ? tokens.danger : tokens.accent;

    return AppPanel(
      child: Row(
        children: [
          AccountMark(icon: icon, color: color),
          const SizedBox(width: AppSpacing.md),
          Expanded(
            child: Text(
              message,
              style: textTheme.bodyMedium?.copyWith(
                color: danger ? tokens.danger : tokens.textSecondary,
              ),
            ),
          ),
          if (busy) ...[
            const SizedBox(width: AppSpacing.md),
            const SizedBox(
              width: 16,
              height: 16,
              child: CircularProgressIndicator(strokeWidth: 2),
            ),
          ] else if (onRetry != null && retryLabel != null) ...[
            const SizedBox(width: AppSpacing.sm),
            TextButton(onPressed: onRetry, child: Text(retryLabel!)),
          ],
        ],
      ),
    );
  }
}
