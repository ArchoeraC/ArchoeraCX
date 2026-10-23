part of '../java_page.dart';

/// Current selection summary plus the automatic-selection switch.
class _CurrentJavaPanel extends StatelessWidget {
  const _CurrentJavaPanel({
    required this.auto,
    required this.selectedPath,
    required this.runtimes,
    required this.onAutoChanged,
  });

  final bool auto;
  final String? selectedPath;
  final List<JavaRuntime> runtimes;
  final ValueChanged<bool>? onAutoChanged;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    JavaRuntime? selected;
    for (final runtime in runtimes) {
      if (runtime.path == selectedPath) {
        selected = runtime;
        break;
      }
    }

    final title = auto
        ? l10n.settingsJavaAutoValue
        : (selected?.home ?? selectedPath ?? '—');
    final detail = auto
        ? l10n.settingsJavaAutoHint
        : selected == null
        ? selectedPath
        : l10n.javaStatusMeta(selected.displayVersion, selected.arch ?? '—');

    return AppPanel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Text(
                l10n.settingsJavaCurrent,
                style: textTheme.labelSmall?.copyWith(
                  color: tokens.textSecondary,
                  letterSpacing: 1.2,
                ),
              ),
              const Spacer(),
              Text(
                l10n.settingsJavaAuto,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
              const SizedBox(width: AppSpacing.xxs),
              Switch(value: auto, onChanged: onAutoChanged),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          Container(height: 1, color: tokens.border),
          const SizedBox(height: AppSpacing.md),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(
                auto ? AppIcons.autoPick : AppIcons.java,
                size: 18,
                color: tokens.accent,
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
                      style: textTheme.titleSmall,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      detail ?? '',
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
        ],
      ),
    );
  }
}

class _Toolbar extends StatelessWidget {
  const _Toolbar({
    required this.controller,
    required this.query,
    required this.onQueryChanged,
    required this.onAddFolder,
    required this.onPickExecutable,
  });

  final TextEditingController controller;
  final String query;
  final ValueChanged<String> onQueryChanged;
  final VoidCallback? onAddFolder;
  final VoidCallback? onPickExecutable;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    final searchField = TextField(
      controller: controller,
      onChanged: onQueryChanged,
      style: Theme.of(context).textTheme.bodySmall,
      decoration: InputDecoration(
        isDense: true,
        hintText: l10n.javaSearchHint,
        prefixIcon: Icon(AppIcons.search, size: 18, color: tokens.textTertiary),
        suffixIcon: query.isEmpty
            ? null
            : IconButton(
                tooltip: l10n.close,
                onPressed: () {
                  controller.clear();
                  onQueryChanged('');
                },
                icon: const Icon(AppIcons.close, size: 16),
              ),
      ),
    );

    final actions = <Widget>[
      OutlinedButton.icon(
        onPressed: onAddFolder,
        icon: const Icon(AppIcons.folderOpen, size: 18),
        label: Text(l10n.javaStatusAddFolder),
      ),
      OutlinedButton.icon(
        onPressed: onPickExecutable,
        icon: const Icon(AppIcons.terminal, size: 18),
        label: Text(l10n.javaStatusSelectExecutable),
      ),
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth < 720) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              searchField,
              const SizedBox(height: AppSpacing.xs),
              Wrap(
                spacing: AppSpacing.xs,
                runSpacing: AppSpacing.xs,
                children: actions,
              ),
            ],
          );
        }

        return Row(
          children: [
            Expanded(child: searchField),
            const SizedBox(width: AppSpacing.md),
            ...actions.map(
              (action) => Padding(
                padding: const EdgeInsets.only(left: AppSpacing.xs),
                child: action,
              ),
            ),
          ],
        );
      },
    );
  }
}

class _RuntimeGrid extends StatelessWidget {
  const _RuntimeGrid({
    required this.runtimes,
    required this.selectedPath,
    required this.onSelect,
  });

  final List<JavaRuntime> runtimes;
  final String? selectedPath;
  final ValueChanged<JavaRuntime> onSelect;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = constraints.maxWidth >= 1040 ? 2 : 1;
        final gap = AppSpacing.md;
        final width = (constraints.maxWidth - gap * (columns - 1)) / columns;

        return Wrap(
          spacing: gap,
          runSpacing: gap,
          children: [
            for (final runtime in runtimes)
              SizedBox(
                width: width,
                child: _RuntimeTile(
                  runtime: runtime,
                  selected: runtime.path == selectedPath,
                  onTap: () => onSelect(runtime),
                ),
              ),
          ],
        );
      },
    );
  }
}

class _RuntimeTile extends StatelessWidget {
  const _RuntimeTile({
    required this.runtime,
    required this.selected,
    required this.onTap,
  });

  final JavaRuntime runtime;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final major = runtime.major;
    final badge = major == null ? l10n.javaStatusUnknownVersion : 'Java $major';

    return AppPanel(
      accent: selected,
      onTap: onTap,
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.md,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.xs,
              vertical: 3,
            ),
            decoration: ShapeDecoration(
              color: selected
                  ? tokens.accent.withValues(alpha: 0.14)
                  : tokens.surface2,
              shape: BeveledRectangleBorder(
                side: BorderSide(
                  color: selected
                      ? tokens.accent.withValues(alpha: 0.45)
                      : tokens.border,
                ),
                borderRadius: BorderRadius.circular(tokens.radiusS),
              ),
            ),
            child: Text(
              badge,
              style: textTheme.labelSmall?.copyWith(
                color: selected ? tokens.accent : tokens.textSecondary,
                letterSpacing: 0,
              ),
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Tooltip(
                  message: runtime.path,
                  child: Text(
                    runtime.home ?? runtime.path,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.labelLarge,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  [
                    runtime.displayVersion,
                    runtime.arch ?? '—',
                    if (runtime.vendor != null && runtime.vendor!.isNotEmpty)
                      runtime.vendor!,
                    if (runtime.source != null && runtime.source!.isNotEmpty)
                      runtime.source!,
                  ].join(' · '),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodySmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          if (selected)
            Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(AppIcons.check, size: 16, color: tokens.accent),
                const SizedBox(width: 2),
                Text(
                  l10n.javaSelectedBadge,
                  style: textTheme.labelSmall?.copyWith(color: tokens.accent),
                ),
              ],
            )
          else
            Icon(AppIcons.chevronRight, size: 18, color: tokens.textTertiary),
        ],
      ),
    );
  }
}

class _Notice extends StatelessWidget {
  const _Notice({
    required this.icon,
    required this.color,
    required this.message,
    this.details,
  });

  final IconData icon;
  final Color color;
  final String message;
  final String? details;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final detail = details;

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 16, color: color),
        const SizedBox(width: AppSpacing.xs),
        Expanded(
          child: Text(
            message,
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: textTheme.bodySmall?.copyWith(color: tokens.textSecondary),
          ),
        ),
        if (detail != null)
          Tooltip(
            message: detail,
            child: Icon(AppIcons.info, size: 16, color: tokens.textTertiary),
          ),
      ],
    );
  }
}

class _CustomPaths extends StatelessWidget {
  const _CustomPaths({
    required this.roots,
    required this.executables,
    required this.onRemoveRoot,
    required this.onRemoveExecutable,
  });

  final List<String> roots;
  final List<String> executables;
  final ValueChanged<String> onRemoveRoot;
  final ValueChanged<String> onRemoveExecutable;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          l10n.javaStatusCustomPaths,
          style: Theme.of(context).textTheme.labelSmall
              ?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(height: AppSpacing.xxs),
        Wrap(
          spacing: AppSpacing.xxs,
          runSpacing: AppSpacing.xxs,
          children: [
            for (final path in roots)
              _PathChip(
                path: path,
                removeLabel: l10n.javaStatusRemovePath(path),
                onDeleted: () => onRemoveRoot(path),
              ),
            for (final path in executables)
              _PathChip(
                path: path,
                removeLabel: l10n.javaStatusRemovePath(path),
                onDeleted: () => onRemoveExecutable(path),
              ),
          ],
        ),
      ],
    );
  }
}

class _PathChip extends StatelessWidget {
  const _PathChip({
    required this.path,
    required this.removeLabel,
    required this.onDeleted,
  });

  final String path;
  final String removeLabel;
  final VoidCallback onDeleted;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return InputChip(
      label: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 260),
        child: Text(
          path,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: textTheme.labelSmall,
        ),
      ),
      tooltip: path,
      deleteIcon: const Icon(AppIcons.close, size: 14),
      deleteButtonTooltipMessage: removeLabel,
      onDeleted: onDeleted,
      backgroundColor: tokens.surface2,
      side: BorderSide(color: tokens.border),
      visualDensity: VisualDensity.compact,
    );
  }
}
