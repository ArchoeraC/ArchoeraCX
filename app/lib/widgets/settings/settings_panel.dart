import 'package:flutter/material.dart';

import '../../theme/tokens.dart';
import '../common/app_panel.dart';

/// One focused settings group.
///
/// The panel is the only bordered surface inside the content pane; rows are
/// separated by hairline rules instead of individual cards.
class SettingsPanel extends StatelessWidget {
  const SettingsPanel({super.key, required this.child, this.accent = false});

  final Widget child;
  final bool accent;

  @override
  Widget build(BuildContext context) {
    return AppPanel(padding: EdgeInsets.zero, accent: accent, child: child);
  }
}

/// Hairline rule between settings rows.
///
/// Rules are inset to the panel's content padding so that embedded sections
/// (for example the Java scan block) line up with the same left edge.
class SettingsDivider extends StatelessWidget {
  const SettingsDivider({super.key, this.inset = true});

  final bool inset;

  @override
  Widget build(BuildContext context) {
    return Divider(
      height: 1,
      thickness: 1,
      indent: inset ? AppSpacing.md : 0,
      endIndent: inset ? AppSpacing.md : 0,
      color: context.tokens.border,
    );
  }
}

/// A single settings row: icon, title/subtitle, trailing value or control.
class SettingsRow extends StatelessWidget {
  const SettingsRow({
    super.key,
    required this.icon,
    required this.title,
    this.subtitle,
    this.value,
    this.valueColor,
    this.trailing,
    this.onTap,
    this.valueMaxWidth = 280,
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final String? value;
  final Color? valueColor;
  final Widget? trailing;
  final VoidCallback? onTap;
  final double valueMaxWidth;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final interactive = onTap != null;
    final valueText = value;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.md,
            vertical: AppSpacing.sm,
          ),
          child: LayoutBuilder(
            builder: (context, constraints) {
              // Keep the value honest on narrow windows: never let it crowd
              // the title out, never let it run past the row.
              final cap = (constraints.maxWidth * 0.45).clamp(
                0.0,
                valueMaxWidth,
              );

              return Row(
                children: [
                  Icon(icon, size: 18, color: tokens.textSecondary),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(
                          title,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: textTheme.bodyMedium,
                        ),
                        if (subtitle != null) ...[
                          const SizedBox(height: 2),
                          Text(
                            subtitle!,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: textTheme.bodySmall?.copyWith(
                              color: tokens.textTertiary,
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                  if (valueText != null) ...[
                    const SizedBox(width: AppSpacing.sm),
                    ConstrainedBox(
                      constraints: BoxConstraints(maxWidth: cap),
                      child: Tooltip(
                        message: valueText,
                        child: Text(
                          valueText,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          textAlign: TextAlign.end,
                          style: textTheme.bodySmall?.copyWith(
                            color: valueColor ?? tokens.textSecondary,
                          ),
                        ),
                      ),
                    ),
                  ],
                  if (trailing != null) ...[
                    const SizedBox(width: AppSpacing.xs),
                    trailing!,
                  ] else if (interactive) ...[
                    const SizedBox(width: AppSpacing.xxs),
                    Icon(
                      Icons.chevron_right_rounded,
                      size: 18,
                      color: tokens.textTertiary,
                    ),
                  ],
                ],
              );
            },
          ),
        ),
      ),
    );
  }
}
