part of '../settings_page.dart';

class _SettingsCard extends StatelessWidget {
  const _SettingsCard({required this.children});

  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return AppSection(
      showTop: true,
      showBottom: true,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Column(
        children: [
          for (var i = 0; i < children.length; i++) ...[
            children[i],
            if (i < children.length - 1)
              Divider(
                height: 1,
                indent: AppSpacing.sm,
                endIndent: AppSpacing.sm,
                color: context.tokens.border,
              ),
          ],
        ],
      ),
    );
  }
}

class _SettingRow extends StatelessWidget {
  const _SettingRow({
    required this.icon,
    required this.title,
    this.subtitle,
    this.value,
    this.onTap,
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final String? value;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs),
      onTap: onTap,
      leading: Icon(icon, size: 20, color: tokens.textSecondary),
      title: Text(title, style: Theme.of(context).textTheme.bodyMedium),
      subtitle: subtitle == null
          ? null
          : Text(
              subtitle!,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textTertiary),
            ),
      trailing: value == null
          ? null
          : ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 380),
              child: Text(
                value!,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ),
    );
  }
}

/// Settings group: a small section label above a filled row card, with an
/// optional note underneath. Mirrors the section/title/note rhythm used by the
/// other Archoera apps while keeping the launcher's angular panel shape.
class _SettingsGroup extends StatelessWidget {
  const _SettingsGroup({
    required this.title,
    required this.children,
    this.note,
  });

  final String title;
  final List<Widget> children;
  final String? note;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final noteText = note;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.only(left: 2, bottom: AppSpacing.xs),
          child: Text(
            title,
            style: textTheme.labelSmall?.copyWith(
              color: tokens.textSecondary,
              letterSpacing: 1.2,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
        AppSection(
          showTop: true,
          showBottom: true,
          padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
          child: Column(
            children: [
              for (var index = 0; index < children.length; index++) ...[
                children[index],
                if (index < children.length - 1)
                  Divider(
                    height: 1,
                    indent: AppSpacing.sm,
                    endIndent: AppSpacing.sm,
                    color: tokens.border,
                  ),
              ],
            ],
          ),
        ),
        if (noteText != null)
          Padding(
            padding: const EdgeInsets.only(left: 2, top: AppSpacing.xs),
            child: Text(
              noteText,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
          ),
      ],
    );
  }
}

/// One appearance option: icon badge, title with a short description, and a
/// trailing control. The whole row is tappable when [onTap] is set.
class _AppearanceTile extends StatelessWidget {
  const _AppearanceTile({
    required this.icon,
    required this.title,
    this.subtitle,
    this.trailing,
    this.onTap,
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final Widget? trailing;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final subtitleText = subtitle;
    final control = trailing;

    final content = Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        children: [
          Container(
            width: 34,
            height: 34,
            decoration: BoxDecoration(
              color: tokens.accent.withValues(alpha: 0.10),
              borderRadius: BorderRadius.circular(tokens.radiusM),
            ),
            child: Icon(icon, size: 18, color: tokens.accent),
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: textTheme.titleSmall),
                if (subtitleText != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    subtitleText,
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
          if (control != null) ...[
            const SizedBox(width: AppSpacing.sm),
            control,
          ],
        ],
      ),
    );

    if (onTap == null) return content;
    return Material(
      color: Colors.transparent,
      child: InkWell(onTap: onTap, child: content),
    );
  }
}

void _notify(
  BuildContext context,
  String message, {
  ToastType type = ToastType.info,
}) {
  toast(message, type: type);
}
