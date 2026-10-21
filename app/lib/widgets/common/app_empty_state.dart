import 'package:flutter/material.dart';

import '../../theme/tokens.dart';
import 'app_card.dart';

class AppEmptyState extends StatelessWidget {
  const AppEmptyState({
    super.key,
    required this.icon,
    required this.title,
    required this.message,
    this.primaryAction,
    this.secondaryAction,
  });

  final IconData icon;
  final String title;
  final String message;
  final Widget? primaryAction;
  final Widget? secondaryAction;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 520),
        child: AppCard(
          padding: const EdgeInsets.all(AppSpacing.xl),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 56,
                height: 56,
                decoration: BoxDecoration(
                  color: tokens.accentSoft,
                  borderRadius: BorderRadius.circular(tokens.radiusL),
                ),
                child: Icon(icon, color: tokens.accent, size: 28),
              ),
              const SizedBox(height: AppSpacing.lg),
              Text(
                title,
                style: textTheme.titleLarge,
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: AppSpacing.xs),
              Text(
                message,
                style: textTheme.bodyMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
                textAlign: TextAlign.center,
              ),
              if (primaryAction != null || secondaryAction != null) ...[
                const SizedBox(height: AppSpacing.lg),
                Wrap(
                  spacing: AppSpacing.xs,
                  runSpacing: AppSpacing.xs,
                  alignment: WrapAlignment.center,
                  children: [?primaryAction, ?secondaryAction],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
