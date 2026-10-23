part of '../instances_page.dart';

class _InstanceRow extends StatelessWidget {
  const _InstanceRow({
    required this.instance,
    required this.selected,
    required this.onSelect,
    required this.onLaunch,
    required this.onCheck,
    required this.onManageLoader,
    required this.onInstallGame,
    required this.downloadsEnabled,
    required this.onManageMods,
    required this.onDuplicate,
    required this.onViewLogs,
    required this.onOpenFolder,
    required this.onDelete,
  });

  final GameInstance instance;
  final bool selected;
  final VoidCallback onSelect;
  final VoidCallback onLaunch;
  final ValueChanged<String> onCheck;
  final VoidCallback onManageLoader;
  final VoidCallback onInstallGame;
  final bool downloadsEnabled;
  final VoidCallback onManageMods;
  final VoidCallback onDuplicate;
  final VoidCallback onViewLogs;
  final VoidCallback onOpenFolder;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    return Material(
      color: selected ? tokens.accentSoft : Colors.transparent,
      child: InkWell(
        onTap: onSelect,
        hoverColor: tokens.surface2,
        child: Container(
          decoration: BoxDecoration(
            border: Border(
              left: BorderSide(
                color: selected ? tokens.accent : Colors.transparent,
                width: 2,
              ),
            ),
          ),
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm,
            vertical: AppSpacing.sm,
          ),
          child: LayoutBuilder(
            builder: (context, constraints) {
              final wide = constraints.maxWidth >= 720;
              return Row(
                children: [
                  LoaderMark(loader: instance.loader, size: 24),
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
                        _MetaLine(instance: instance),
                      ],
                    ),
                  ),
                  if (wide) ...[
                    const SizedBox(width: AppSpacing.md),
                    Text(
                      lastPlayedLabel(context, l10n, instance.lastPlayed),
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.textTertiary,
                      ),
                    ),
                  ],
                  const SizedBox(width: AppSpacing.xs),
                  TextButton.icon(
                    onPressed: instance.missing ? null : onLaunch,
                    icon: const Icon(AppIcons.play, size: 18),
                    label: Text(l10n.launch),
                  ),
                  _InstanceMenu(
                    l10n: l10n,
                    downloadsEnabled: downloadsEnabled,
                    onCheck: onCheck,
                    onManageLoader: onManageLoader,
                    onSettings: () =>
                        showInstanceSettingsDialog(context, instance),
                    onInstallGame: onInstallGame,
                    onManageMods: onManageMods,
                    onDuplicate: onDuplicate,
                    onViewLogs: onViewLogs,
                    onOpenFolder: onOpenFolder,
                    onDelete: onDelete,
                  ),
                ],
              );
            },
          ),
        ),
      ),
    );
  }
}

/// Instance card used by the wide-window grid. It carries the same actions as
/// the list row so the two layouts stay behaviourally identical.
class _InstanceCard extends StatelessWidget {
  const _InstanceCard({
    required this.instance,
    required this.selected,
    required this.onSelect,
    required this.onLaunch,
    required this.onCheck,
    required this.onManageLoader,
    required this.onInstallGame,
    required this.downloadsEnabled,
    required this.onManageMods,
    required this.onDuplicate,
    required this.onViewLogs,
    required this.onOpenFolder,
    required this.onDelete,
  });

  final GameInstance instance;
  final bool selected;
  final VoidCallback onSelect;
  final VoidCallback onLaunch;
  final ValueChanged<String> onCheck;
  final VoidCallback onManageLoader;
  final VoidCallback onInstallGame;
  final bool downloadsEnabled;
  final VoidCallback onManageMods;
  final VoidCallback onDuplicate;
  final VoidCallback onViewLogs;
  final VoidCallback onOpenFolder;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return AppPanel(
      accent: selected,
      onTap: onSelect,
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.sm,
        AppSpacing.sm,
        AppSpacing.xxs,
        AppSpacing.xxs,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              LoaderMark(loader: instance.loader, size: 26),
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
                    _MetaLine(instance: instance),
                  ],
                ),
              ),
              _InstanceMenu(
                l10n: l10n,
                downloadsEnabled: downloadsEnabled,
                onCheck: onCheck,
                onManageLoader: onManageLoader,
                onSettings: () => showInstanceSettingsDialog(context, instance),
                onInstallGame: onInstallGame,
                onManageMods: onManageMods,
                onDuplicate: onDuplicate,
                onViewLogs: onViewLogs,
                onOpenFolder: onOpenFolder,
                onDelete: onDelete,
              ),
            ],
          ),
          const Spacer(),
          Row(
            children: [
              Icon(AppIcons.time, size: 14, color: tokens.textTertiary),
              const SizedBox(width: AppSpacing.xxs),
              Expanded(
                child: Text(
                  lastPlayedLabel(context, l10n, instance.lastPlayed),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.labelSmall?.copyWith(
                    color: tokens.textTertiary,
                    letterSpacing: 0,
                  ),
                ),
              ),
              TextButton.icon(
                onPressed: instance.missing ? null : onLaunch,
                icon: const Icon(AppIcons.play, size: 18),
                label: Text(l10n.launch),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// Meta caption + overrides badge, identical in the row and card layouts; one
/// shared widget keeps the two copies from drifting apart.
class _MetaLine extends StatelessWidget {
  const _MetaLine({required this.instance});

  final GameInstance instance;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    return Row(
      children: [
        Flexible(
          child: Text(
            _instanceMeta(instance, l10n),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: textTheme.bodySmall?.copyWith(
              color: instance.missing ? tokens.danger : tokens.textTertiary,
            ),
          ),
        ),
        if (_hasOverrides(instance)) ...[
          const SizedBox(width: AppSpacing.xxs),
          _OverridesBadge(l10n: l10n, overrides: instance.overrides!),
        ],
      ],
    );
  }
}

String _instanceMeta(GameInstance instance, AppLocalizations l10n) {
  final parts = <String>[
    instance.gameVersion,
    instance.loader.labelFor(l10n),
    l10n.instanceMods(instance.modCount),
  ];
  if (instance.missing) parts.insert(0, l10n.instanceMissing);
  if (instance.custom) parts.insert(0, l10n.instanceCustom);
  return parts.join(' · ');
}

/// True when the instance carries at least one launch override.
bool _hasOverrides(GameInstance instance) {
  final overrides = instance.overrides;
  return overrides != null && !overrides.isEmpty;
}

/// Tiny marker next to the metadata that flags per-instance overrides without
/// taking space from the row.
class _OverridesBadge extends StatelessWidget {
  const _OverridesBadge({required this.l10n, required this.overrides});

  final AppLocalizations l10n;
  final InstanceOverrides overrides;

  @override
  Widget build(BuildContext context) {
    final fields = <String>[
      if (overrides.memoryMinMb != null || overrides.memoryMaxMb != null)
        l10n.instanceOverridesMemory,
      if (overrides.gcMode != null) l10n.instanceOverridesGc,
      if (overrides.jvmArgs != null) l10n.instanceOverridesJvm,
      if (overrides.gameArgs != null) l10n.instanceOverridesGameArgs,
      if (overrides.javaPath != null) l10n.instanceOverridesJava,
      if (overrides.windowWidth != null ||
          overrides.windowHeight != null ||
          overrides.fullscreen != null)
        l10n.instanceOverridesWindow,
    ];
    return Tooltip(
      message: fields.isEmpty
          ? l10n.instanceOverridesBadge
          : '${l10n.instanceOverridesBadge}\n${fields.join(' · ')}',
      child: Icon(
        AppIcons.settings,
        size: 14,
        color: context.tokens.textTertiary,
      ),
    );
  }
}
