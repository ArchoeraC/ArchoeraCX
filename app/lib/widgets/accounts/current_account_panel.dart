import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/account.dart';
import '../../theme/tokens.dart';
import '../common/app_panel.dart';
import 'account_avatar.dart';
import 'account_visuals.dart';

/// Hero for the signed-in account.
///
/// One large panel replaces the previous split summary: identity, session
/// metadata and actions sit on the left, a flat type mark on the right.
class CurrentAccountPanel extends StatelessWidget {
  const CurrentAccountPanel({super.key, required this.account, this.onRefresh});

  final Account account;
  final VoidCallback? onRefresh;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return AppPanel(
      padding: EdgeInsets.zero,
      accent: true,
      child: LayoutBuilder(
        builder: (context, constraints) {
          final wide = constraints.maxWidth >= 820;
          final content = Padding(
            padding: EdgeInsets.all(wide ? AppSpacing.xl : AppSpacing.lg),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Row(
                  children: [
                    Container(width: 22, height: 2, color: tokens.accent),
                    const SizedBox(width: AppSpacing.xs),
                    Text(
                      l10n.currentAccount,
                      style: textTheme.labelLarge?.copyWith(
                        color: tokens.accent,
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: AppSpacing.md),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.center,
                  children: [
                    AccountAvatar(account: account, size: wide ? 72 : 56),
                    const SizedBox(width: AppSpacing.md),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            account.displayName,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: wide
                                ? textTheme.displaySmall?.copyWith(
                                    fontSize: 34,
                                    height: 1.15,
                                  )
                                : textTheme.titleLarge,
                          ),
                          const SizedBox(height: AppSpacing.xs),
                          _AccountMeta(account: account),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: AppSpacing.lg),
                Wrap(
                  spacing: AppSpacing.xs,
                  runSpacing: AppSpacing.xs,
                  children: [
                    if (onRefresh != null)
                      OutlinedButton.icon(
                        onPressed: onRefresh,
                        style: OutlinedButton.styleFrom(
                          minimumSize: const Size(0, 44),
                        ),
                        icon: const Icon(Icons.refresh_rounded, size: 18),
                        label: Text(l10n.accountRefresh),
                      ),
                  ],
                ),
              ],
            ),
          );

          if (!wide) return content;

          return IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Expanded(child: content),
                Container(width: 1, color: tokens.border),
                SizedBox(width: 220, child: _KindArt(account: account)),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _AccountMeta extends StatelessWidget {
  const _AccountMeta({required this.account});

  final Account account;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final color = account.kind.colorFor(tokens);

    return Wrap(
      spacing: AppSpacing.xs,
      runSpacing: AppSpacing.xxs,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
          decoration: ShapeDecoration(
            color: color.withValues(alpha: 0.12),
            shape: BeveledRectangleBorder(
              side: BorderSide(color: color.withValues(alpha: 0.4)),
              borderRadius: BorderRadius.only(
                topRight: Radius.circular(tokens.bevelS * 0.55),
                bottomLeft: Radius.circular(tokens.bevelS * 0.4),
              ),
            ),
          ),
          child: Text(
            account.kind.labelFor(l10n),
            style: textTheme.labelSmall?.copyWith(
              color: color,
              letterSpacing: 0,
            ),
          ),
        ),
        if (account.uuid != null)
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 340),
            child: Text(
              account.uuid!,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
          ),
      ],
    );
  }
}

class _KindArt extends StatelessWidget {
  const _KindArt({required this.account});

  final Account account;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final color = account.kind.colorFor(tokens);

    return ClipRect(
      child: Stack(
        fit: StackFit.expand,
        children: [
          CustomPaint(
            painter: _HeroGridPainter(
              color: tokens.border.withValues(alpha: 0.5),
            ),
          ),
          Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(account.kind.icon, size: 56, color: color),
                const SizedBox(height: AppSpacing.sm),
                Text(
                  account.kind.labelFor(l10n),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.labelMedium
                      ?.copyWith(color: tokens.textSecondary),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _HeroGridPainter extends CustomPainter {
  const _HeroGridPainter({required this.color});

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 1;
    const step = 26.0;
    for (var x = step; x < size.width; x += step) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), paint);
    }
    for (var y = step; y < size.height; y += step) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), paint);
    }
  }

  @override
  bool shouldRepaint(covariant _HeroGridPainter oldDelegate) =>
      oldDelegate.color != color;
}
