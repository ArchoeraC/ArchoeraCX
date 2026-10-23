part of '../dashboard_page.dart';

class _LaunchHero extends StatelessWidget {
  const _LaunchHero({
    required this.l10n,
    required this.instance,
    required this.onLaunch,
    required this.onBrowse,
    required this.onCreate,
  });

  final AppLocalizations l10n;
  final GameInstance? instance;
  final VoidCallback onLaunch;
  final VoidCallback onBrowse;
  final VoidCallback onCreate;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final current = instance;
    final hasInstance = current != null;

    return AppPanel(
      padding: EdgeInsets.zero,
      accent: hasInstance,
      child: LayoutBuilder(
        builder: (context, constraints) {
          final showArt = constraints.maxWidth >= 760;
          final content = Padding(
            padding: EdgeInsets.all(showArt ? AppSpacing.xl : AppSpacing.lg),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Row(
                  children: [
                    Container(width: 22, height: 2, color: tokens.accent),
                    const SizedBox(width: AppSpacing.xs),
                    Text(
                      l10n.quickLaunch,
                      style: textTheme.labelLarge?.copyWith(
                        color: tokens.accent,
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: AppSpacing.md),
                Text(
                  current?.name ?? l10n.instanceEmptyTitle,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.displaySmall?.copyWith(
                    fontSize: showArt ? 44 : 34,
                    height: 1.08,
                    fontWeight: FontWeight.w500,
                  ),
                ),
                const SizedBox(height: AppSpacing.xs),
                Text(
                  hasInstance
                      ? l10n.quickLaunchSubtitle
                      : l10n.instanceEmptyMessage,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodyMedium?.copyWith(
                    color: tokens.textSecondary,
                  ),
                ),
                if (current != null) ...[
                  const SizedBox(height: AppSpacing.md),
                  _HeroMeta(
                    items: [
                      current.loaderLabelFor(l10n),
                      current.gameVersion,
                      l10n.instanceMods(current.modCount),
                      lastPlayedLabel(context, l10n, current.lastPlayed),
                    ],
                  ),
                ],
                const SizedBox(height: AppSpacing.lg),
                Wrap(
                  spacing: AppSpacing.xs,
                  runSpacing: AppSpacing.xs,
                  children: [
                    FilledButton.icon(
                      onPressed: hasInstance ? onLaunch : onCreate,
                      style: FilledButton.styleFrom(
                        minimumSize: const Size(0, 44),
                        padding: const EdgeInsets.symmetric(
                          horizontal: AppSpacing.lg,
                        ),
                      ),
                      icon: Icon(
                        hasInstance
                            ? AppIcons.play
                            : AppIcons.add,
                        size: 20,
                      ),
                      label: Text(
                        hasInstance ? l10n.launch : l10n.createInstance,
                      ),
                    ),
                    OutlinedButton.icon(
                      onPressed: onBrowse,
                      style: OutlinedButton.styleFrom(
                        minimumSize: const Size(0, 44),
                      ),
                      icon: const Icon(AppIcons.gridView, size: 18),
                      label: Text(l10n.viewAll),
                    ),
                  ],
                ),
              ],
            ),
          );

          if (!showArt) return content;

          return IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Expanded(child: content),
                Container(width: 1, color: tokens.border),
                SizedBox(width: 250, child: _HeroArt(instance: current)),
              ],
            ),
          );
        },
      ),
    );
  }
}

class _HeroArt extends StatelessWidget {
  const _HeroArt({required this.instance});

  final GameInstance? instance;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final loader = instance?.loader;

    return ClipRect(
      child: Stack(
        fit: StackFit.expand,
        children: [
          CustomPaint(
            painter: _GridPainter(color: tokens.border.withValues(alpha: 0.55)),
          ),
          Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (loader == null)
                  Icon(AppIcons.add, size: 76, color: tokens.accent)
                else
                  LoaderMark(loader: loader, size: 76),
                const SizedBox(height: AppSpacing.sm),
                Text(
                  loader?.labelFor(AppLocalizations.of(context)) ??
                      AppLocalizations.of(context).appName,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.labelMedium?.copyWith(
                    color: tokens.textSecondary,
                  ),
                ),
                if (instance != null) ...[
                  const SizedBox(height: AppSpacing.xxs),
                  Text(
                    instance!.gameVersion,
                    style: textTheme.headlineSmall?.copyWith(
                      color: tokens.textPrimary,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _GridPainter extends CustomPainter {
  const _GridPainter({required this.color});

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 1;
    const step = 28.0;
    for (var x = step; x < size.width; x += step) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), paint);
    }
    for (var y = step; y < size.height; y += step) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), paint);
    }
  }

  @override
  bool shouldRepaint(covariant _GridPainter oldDelegate) =>
      oldDelegate.color != color;
}

class _ShortcutStrip extends StatelessWidget {
  const _ShortcutStrip({
    required this.l10n,
    required this.instanceCount,
    required this.onInstances,
    required this.onDownload,
  });

  final AppLocalizations l10n;
  final int instanceCount;
  final VoidCallback onInstances;
  final VoidCallback? onDownload;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final cells = <Widget>[
      _ShortcutCell(
        icon: AppIcons.gridView,
        title: l10n.navInstances,
        value: l10n.instancesCount(instanceCount),
        onTap: onInstances,
      ),
      if (onDownload != null)
        _ShortcutCell(
          icon: AppIcons.download,
          title: l10n.dashboardBrowseContent,
          value: l10n.sourceModrinth,
          onTap: onDownload!,
        ),
    ];

    return AppPanel(
      padding: EdgeInsets.zero,
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth >= 720) {
            return IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (var i = 0; i < cells.length; i++) ...[
                    if (i > 0) VerticalDivider(width: 1, color: tokens.border),
                    Expanded(child: cells[i]),
                  ],
                ],
              ),
            );
          }

          return Column(
            children: [
              for (var i = 0; i < cells.length; i++) ...[
                if (i > 0) Divider(height: 1, color: tokens.border),
                cells[i],
              ],
            ],
          );
        },
      ),
    );
  }
}

class _ShortcutCell extends StatelessWidget {
  const _ShortcutCell({
    required this.icon,
    required this.title,
    required this.value,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String value;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.md,
            vertical: AppSpacing.sm,
          ),
          child: Row(
            children: [
              Icon(icon, size: 19, color: tokens.accent),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.labelMedium?.copyWith(
                        color: tokens.textTertiary,
                      ),
                    ),
                    const SizedBox(height: 1),
                    Text(
                      value,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.titleSmall,
                    ),
                  ],
                ),
              ),
              Icon(
                AppIcons.chevronRight,
                size: 18,
                color: tokens.textTertiary,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _RecentInstancesSection extends StatelessWidget {
  const _RecentInstancesSection({
    required this.l10n,
    required this.instances,
    required this.selectedId,
    required this.loading,
    required this.onBrowse,
    required this.onSelect,
  });

  final AppLocalizations l10n;
  final List<GameInstance> instances;
  final String? selectedId;
  final bool loading;
  final VoidCallback onBrowse;
  final ValueChanged<GameInstance> onSelect;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final recent = instances.take(4).toList(growable: false);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(l10n.recentInstances, style: textTheme.titleMedium),
            ),
            TextButton(onPressed: onBrowse, child: Text(l10n.viewAll)),
          ],
        ),
        const SizedBox(height: AppSpacing.xs),
        if (loading)
          const Padding(
            padding: EdgeInsets.all(AppSpacing.lg),
            child: Center(child: CircularProgressIndicator()),
          )
        else if (recent.isEmpty)
          Text(
            l10n.instanceEmptyMessage,
            style: textTheme.bodySmall?.copyWith(color: tokens.textSecondary),
          )
        else
          LayoutBuilder(
            builder: (context, constraints) {
              final maxColumns = constraints.maxWidth >= 1500
                  ? 4
                  : constraints.maxWidth >= 1040
                  ? 3
                  : constraints.maxWidth >= 620
                  ? 2
                  : 1;
              // Fill the row when fewer instances than columns are available.
              final columns = recent.length < maxColumns
                  ? recent.length
                  : maxColumns;
              final width =
                  (constraints.maxWidth - AppSpacing.sm * (columns - 1)) /
                  columns;
              return Wrap(
                spacing: AppSpacing.sm,
                runSpacing: AppSpacing.sm,
                children: [
                  for (final instance in recent)
                    SizedBox(
                      width: width,
                      child: _InstanceTile(
                        l10n: l10n,
                        instance: instance,
                        selected: instance.id == selectedId,
                        onTap: () => onSelect(instance),
                      ),
                    ),
                ],
              );
            },
          ),
      ],
    );
  }
}

class _InstanceTile extends StatelessWidget {
  const _InstanceTile({
    required this.l10n,
    required this.instance,
    required this.selected,
    required this.onTap,
  });

  final AppLocalizations l10n;
  final GameInstance instance;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    return AppPanel(
      padding: const EdgeInsets.all(AppSpacing.sm),
      accent: selected,
      onTap: onTap,
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            color: tokens.surface2,
            child: Center(child: LoaderMark(loader: instance.loader, size: 24)),
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  instance.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.titleSmall,
                ),
                const SizedBox(height: 2),
                Text(
                  '${instance.loaderLabelFor(l10n)} · '
                  '${l10n.instanceMods(instance.modCount)}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodySmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  lastPlayedLabel(context, l10n, instance.lastPlayed),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.labelSmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              ],
            ),
          ),
          if (selected)
            Icon(AppIcons.check, size: 18, color: tokens.accent),
        ],
      ),
    );
  }
}

class _HeroMeta extends StatelessWidget {
  const _HeroMeta({required this.items});

  final List<String> items;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final style = Theme.of(context).textTheme.bodySmall
        ?.copyWith(color: tokens.textSecondary);

    return Wrap(
      spacing: AppSpacing.xs,
      runSpacing: AppSpacing.xxs,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        for (var i = 0; i < items.length; i++) ...[
          if (i > 0)
            Text('·', style: style?.copyWith(color: tokens.textTertiary)),
          Text(items[i], style: style),
        ],
      ],
    );
  }
}
