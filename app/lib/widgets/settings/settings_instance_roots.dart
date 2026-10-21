import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../theme/tokens.dart';
import 'settings_panel.dart';

/// Instance storage roots inside the storage settings panel.
class SettingsInstanceRootsEditor extends StatelessWidget {
  const SettingsInstanceRootsEditor({
    super.key,
    required this.l10n,
    required this.roots,
    required this.defaultRoot,
    required this.onAdd,
    required this.onRemove,
    required this.onMakeDefault,
  });

  final AppLocalizations l10n;
  final List<String> roots;
  final String? defaultRoot;
  final VoidCallback onAdd;
  final ValueChanged<String> onRemove;
  final ValueChanged<String> onMakeDefault;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.md,
            AppSpacing.xs,
            AppSpacing.xs,
            AppSpacing.xs,
          ),
          child: Row(
            children: [
              Icon(
                Icons.folder_outlined,
                size: 18,
                color: tokens.textSecondary,
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Text(
                  l10n.settingsInstanceRoots,
                  style: textTheme.bodyMedium,
                ),
              ),
              TextButton.icon(
                onPressed: onAdd,
                style: TextButton.styleFrom(
                  visualDensity: VisualDensity.compact,
                  padding: const EdgeInsets.symmetric(
                    horizontal: AppSpacing.xs,
                  ),
                  minimumSize: const Size(0, 36),
                ),
                icon: const Icon(Icons.add_rounded, size: 17),
                label: Text(l10n.settingsAddRoot),
              ),
            ],
          ),
        ),
        const SettingsDivider(),
        if (roots.isEmpty)
          Padding(
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.md,
              vertical: AppSpacing.sm,
            ),
            child: Text(
              '—',
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
          )
        else
          for (var i = 0; i < roots.length; i++) ...[
            _RootRow(
              l10n: l10n,
              root: roots[i],
              isDefault: roots[i] == defaultRoot,
              canRemove: roots.length > 1,
              onRemove: () => onRemove(roots[i]),
              onMakeDefault: () => onMakeDefault(roots[i]),
            ),
            if (i < roots.length - 1) const SettingsDivider(),
          ],
      ],
    );
  }
}

class _RootRow extends StatelessWidget {
  const _RootRow({
    required this.l10n,
    required this.root,
    required this.isDefault,
    required this.canRemove,
    required this.onRemove,
    required this.onMakeDefault,
  });

  final AppLocalizations l10n;
  final String root;
  final bool isDefault;
  final bool canRemove;
  final VoidCallback onRemove;
  final VoidCallback onMakeDefault;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Padding(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.md,
        AppSpacing.xxs,
        AppSpacing.xs,
        AppSpacing.xxs,
      ),
      child: Row(
        children: [
          Icon(
            Icons.folder_open_rounded,
            size: 18,
            color: isDefault ? tokens.accent : tokens.textSecondary,
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Tooltip(
                  message: root,
                  child: Text(
                    root,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textSecondary,
                    ),
                  ),
                ),
                if (isDefault)
                  Padding(
                    padding: const EdgeInsets.only(top: 1),
                    child: Text(
                      l10n.settingsDefaultRoot,
                      style: textTheme.labelSmall?.copyWith(
                        color: tokens.accent,
                      ),
                    ),
                  ),
              ],
            ),
          ),
          if (!isDefault)
            IconButton(
              onPressed: onMakeDefault,
              tooltip: l10n.settingsMakeDefault,
              iconSize: 18,
              visualDensity: VisualDensity.compact,
              constraints: const BoxConstraints(minWidth: 36, minHeight: 36),
              padding: EdgeInsets.zero,
              icon: const Icon(Icons.star_outline_rounded),
            ),
          if (canRemove)
            IconButton(
              onPressed: onRemove,
              tooltip: l10n.settingsRemoveRoot,
              iconSize: 18,
              visualDensity: VisualDensity.compact,
              constraints: const BoxConstraints(minWidth: 36, minHeight: 36),
              padding: EdgeInsets.zero,
              icon: Icon(
                Icons.remove_circle_outline_rounded,
                color: tokens.danger,
              ),
            ),
        ],
      ),
    );
  }
}
