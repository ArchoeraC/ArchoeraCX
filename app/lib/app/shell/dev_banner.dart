part of '../shell.dart';

class _DevelopmentBanner extends StatelessWidget {
  const _DevelopmentBanner({required this.l10n, required this.contentMaxWidth});

  final AppLocalizations l10n;
  final double contentMaxWidth;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    return Container(
      width: double.infinity,
      decoration: BoxDecoration(
        color: tokens.warning.withValues(alpha: 0.10),
        border: Border(
          bottom: BorderSide(color: tokens.warning.withValues(alpha: 0.35)),
        ),
      ),
      child: Center(
        child: ConstrainedBox(
          constraints: BoxConstraints(maxWidth: contentMaxWidth),
          child: Padding(
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.xl,
              vertical: AppSpacing.xxs,
            ),
            child: Row(
              children: [
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 6,
                    vertical: 2,
                  ),
                  decoration: ShapeDecoration(
                    color: tokens.warning.withValues(alpha: 0.16),
                    shape: BeveledRectangleBorder(
                      side: BorderSide(
                        color: tokens.warning.withValues(alpha: 0.5),
                      ),
                      borderRadius: BorderRadius.circular(tokens.radiusS),
                    ),
                  ),
                  child: Text(
                    l10n.developmentBuildBadge,
                    style: textTheme.labelSmall?.copyWith(
                      color: tokens.warning,
                      fontWeight: FontWeight.w700,
                      letterSpacing: 0.4,
                    ),
                  ),
                ),
                const SizedBox(width: AppSpacing.xs),
                Expanded(
                  child: Text(
                    l10n.developmentBuildWarning,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textSecondary,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class AppDestination {
  const AppDestination({
    required this.path,
    required this.branchIndex,
    required this.label,
    required this.icon,
    required this.selectedIcon,
  });

  final String path;
  final int branchIndex;
  final String label;
  final IconData icon;
  final IconData selectedIcon;
}
