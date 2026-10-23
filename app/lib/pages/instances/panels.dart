part of '../instances_page.dart';

class _InstancesToolbar extends StatelessWidget {
  const _InstancesToolbar({
    required this.query,
    required this.count,
    required this.total,
    required this.onChanged,
  });

  final String query;
  final int count;
  final int total;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    final field = TextField(
      onChanged: onChanged,
      decoration: InputDecoration(
        hintText: l10n.searchInstances,
        prefixIcon: const Icon(AppIcons.search, size: 20),
        isDense: true,
      ),
    );
    final countLabel = Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(AppIcons.gridView, size: 16, color: tokens.textTertiary),
        const SizedBox(width: AppSpacing.xxs),
        Text(
          query.isEmpty ? l10n.instancesCount(total) : '$count / $total',
          style: Theme.of(context).textTheme.labelMedium
              ?.copyWith(color: tokens.textSecondary),
        ),
      ],
    );

    return AppSection(
      showTop: true,
      showBottom: true,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 720) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                field,
                const SizedBox(height: AppSpacing.xs),
                Row(
                  mainAxisAlignment: MainAxisAlignment.end,
                  children: [countLabel],
                ),
              ],
            );
          }

          return Row(
            children: [
              SizedBox(width: 380, child: field),
              const Spacer(),
              countLabel,
            ],
          );
        },
      ),
    );
  }
}

class _EmptyInstancesPanel extends StatelessWidget {
  const _EmptyInstancesPanel({required this.l10n});

  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    final intro = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(AppIcons.gridView, size: 30, color: tokens.accent),
        const SizedBox(height: AppSpacing.sm),
        Text(l10n.instanceEmptyTitle, style: textTheme.titleLarge),
        const SizedBox(height: AppSpacing.xs),
        ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: Text(
            l10n.instanceEmptyMessage,
            style: textTheme.bodyMedium?.copyWith(color: tokens.textSecondary),
          ),
        ),
        const SizedBox(height: AppSpacing.lg),
        Wrap(
          spacing: AppSpacing.xs,
          runSpacing: AppSpacing.xs,
          children: [
            FilledButton.icon(
              onPressed: () => showCreateInstanceDialog(context),
              icon: const Icon(AppIcons.add, size: 18),
              label: Text(l10n.createInstance),
            ),
            OutlinedButton.icon(
              onPressed: () => _showUnavailable(context, l10n),
              icon: const Icon(AppIcons.fileDownload, size: 18),
              label: Text(l10n.importPack),
            ),
          ],
        ),
      ],
    );

    final guide = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        _GuideRow(
          index: 1,
          title: l10n.instanceNameLabel,
          subtitle: l10n.instanceNameHint,
        ),
        _GuideRow(
          index: 2,
          title: l10n.instanceGameVersionLabel,
          subtitle: l10n.instanceLoaderLabel,
        ),
        _GuideRow(
          index: 3,
          title: l10n.instanceLocationLabel,
          subtitle: l10n.instanceLocationHint,
          last: true,
        ),
      ],
    );

    return AppSection(
      showBottom: true,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 760) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                intro,
                const SizedBox(height: AppSpacing.lg),
                Divider(height: 1, color: tokens.border),
                const SizedBox(height: AppSpacing.md),
                guide,
              ],
            );
          }

          return IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(child: intro),
                const SizedBox(width: AppSpacing.xl),
                VerticalDivider(width: 1, color: tokens.border),
                const SizedBox(width: AppSpacing.xl),
                SizedBox(width: 320, child: guide),
              ],
            ),
          );
        },
      ),
    );
  }

  void _showUnavailable(BuildContext context, AppLocalizations l10n) {
    toast(l10n.commonCoreNotConnected, type: ToastType.error);
  }
}

class _GuideRow extends StatelessWidget {
  const _GuideRow({
    required this.index,
    required this.title,
    required this.subtitle,
    this.last = false,
  });

  final int index;
  final String title;
  final String subtitle;
  final bool last;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Padding(
      padding: EdgeInsets.only(bottom: last ? 0 : AppSpacing.sm),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 20,
            child: Text(
              '$index',
              style: textTheme.labelLarge?.copyWith(color: tokens.accent),
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.labelLarge,
                ),
                const SizedBox(height: 1),
                Text(
                  subtitle,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodySmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _InstanceRootCard extends StatelessWidget {
  const _InstanceRootCard({
    required this.roots,
    required this.defaultRoot,
    required this.onOpenSettings,
  });

  final List<String> roots;
  final String? defaultRoot;
  final VoidCallback onOpenSettings;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
      child: Row(
        children: [
          Icon(AppIcons.folder, size: 18, color: tokens.textSecondary),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  l10n.settingsInstanceRoots,
                  style: textTheme.labelMedium?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  roots.isEmpty
                      ? '—'
                      : roots.length == 1
                      ? roots.first
                      : '${defaultRoot ?? roots.first}  +${roots.length - 1}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodySmall?.copyWith(
                    color: tokens.textSecondary,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          TextButton(
            onPressed: onOpenSettings,
            child: Text(l10n.settingsStorage),
          ),
        ],
      ),
    );
  }
}

class _InlineNotice extends StatelessWidget {
  const _InlineNotice({
    required this.icon,
    required this.message,
    required this.color,
  });

  final IconData icon;
  final String message;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Padding(
      padding: const EdgeInsets.only(top: AppSpacing.xl),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 18, color: color),
          const SizedBox(width: AppSpacing.xs),
          Flexible(
            child: Text(
              message,
              style: Theme.of(context).textTheme.bodyMedium
                  ?.copyWith(color: tokens.textSecondary),
            ),
          ),
        ],
      ),
    );
  }
}
