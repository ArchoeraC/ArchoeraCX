part of '../settings_page.dart';

/// Storage section content, instance-root management and the "reveal in file
/// manager" helper.
extension _StoragePanelX on _SettingsPageState {
  Widget _storageContent(
    BuildContext context,
    AppLocalizations l10n,
    AppSettings? settings,
  ) {
    return _SettingsCard(
      children: [
        _InstanceRootsEditor(
          l10n: l10n,
          roots: settings?.roots ?? const <String>[],
          defaultRoot: settings?.instancesRoot,
          onAdd: () => _chooseInstancesRoot(context, l10n),
          onRemove: (root) => _removeInstancesRoot(context, l10n, root),
          onMakeDefault: (root) =>
              _makeDefaultInstancesRoot(context, l10n, root),
        ),
        _SettingRow(
          icon: AppIcons.storage,
          title: l10n.settingsDataRoot,
          value: settings?.dataRoot ?? '—',
        ),
        if (settings != null) ...[
          _AppearanceTile(
            icon: AppIcons.folder,
            title: l10n.settingsOpenDataRoot,
            subtitle: l10n.settingsOpenDataRootDesc,
            trailing: Icon(
              AppIcons.chevronRight,
              size: 18,
              color: context.tokens.textTertiary,
            ),
            onTap: () => _openPath(context, l10n, settings.dataRoot),
          ),
          _AppearanceTile(
            icon: AppIcons.folder,
            title: l10n.settingsOpenInstanceRoot,
            subtitle: l10n.settingsOpenInstanceRootDesc,
            trailing: Icon(
              AppIcons.chevronRight,
              size: 18,
              color: context.tokens.textTertiary,
            ),
            onTap: () => _openPath(context, l10n, settings.instancesRoot),
          ),
        ],
        _AppearanceTile(
          icon: AppIcons.delete,
          title: l10n.settingsCleanPartials,
          subtitle: l10n.settingsCleanPartialsDesc,
          trailing: Icon(
            AppIcons.chevronRight,
            size: 18,
            color: context.tokens.textTertiary,
          ),
          onTap: () => _cleanPartialDownloads(context, ref, l10n),
        ),
      ],
    );
  }

  Future<void> _chooseInstancesRoot(
    BuildContext context,
    AppLocalizations l10n,
  ) async {
    final selected = await getDirectoryPath(
      confirmButtonText: l10n.settingsChooseFolder,
    );
    if (selected == null || !context.mounted) return;

    try {
      await ref.read(appSettingsProvider.notifier).addInstanceRoot(selected);
      if (!context.mounted) return;
      await _syncInstances(context, l10n);
    } catch (error) {
      if (context.mounted) {
        _notify(context, error.toString(), type: ToastType.error);
      }
    }
  }

  Future<void> _removeInstancesRoot(
    BuildContext context,
    AppLocalizations l10n,
    String root,
  ) async {
    try {
      await ref.read(appSettingsProvider.notifier).removeInstanceRoot(root);
      if (!context.mounted) return;
      await _syncInstances(context, l10n);
    } catch (error) {
      if (context.mounted) {
        _notify(context, error.toString(), type: ToastType.error);
      }
    }
  }

  Future<void> _makeDefaultInstancesRoot(
    BuildContext context,
    AppLocalizations l10n,
    String root,
  ) async {
    try {
      await ref.read(appSettingsProvider.notifier).setDefaultInstanceRoot(root);
      if (!context.mounted) return;
      await _syncInstances(context, l10n);
    } catch (error) {
      if (context.mounted) {
        _notify(context, error.toString(), type: ToastType.error);
      }
    }
  }

  Future<void> _syncInstances(
    BuildContext context,
    AppLocalizations l10n,
  ) async {
    final result = await ref.read(instancesProvider.notifier).sync();
    if (!context.mounted) return;
    final message = StringBuffer(l10n.settingsRootsUpdated);
    if (result != null && result.adopted.isNotEmpty) {
      message.write(' ${l10n.instanceSyncAdopted(result.adopted.length)}');
    }
    if (result != null && result.missing.isNotEmpty) {
      message.write(' ${l10n.instanceSyncMissing(result.missing.length)}');
    }
    _notify(context, message.toString(), type: ToastType.success);
  }

  /// Asks the core to reveal [path] in the OS file manager.
  Future<void> _openPath(
    BuildContext context,
    AppLocalizations l10n,
    String path,
  ) async {
    final client = ref.read(coreClientProvider);
    final opened = client != null && await client.revealSystemPath(path);
    if (!opened && context.mounted) {
      _notify(context, l10n.settingsOpenFailed, type: ToastType.error);
    }
  }
}

class _InstanceRootsEditor extends StatelessWidget {
  const _InstanceRootsEditor({
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

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm,
            vertical: AppSpacing.xs,
          ),
          child: Row(
            children: [
              Icon(AppIcons.folder, size: 20, color: tokens.textSecondary),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(
                  l10n.settingsInstanceRoots,
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
              ),
              TextButton.icon(
                onPressed: onAdd,
                icon: const Icon(AppIcons.add, size: 17),
                label: Text(l10n.settingsAddRoot),
              ),
            ],
          ),
        ),
        for (final root in roots)
          ListTile(
            contentPadding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.sm,
            ),
            leading: Icon(
              AppIcons.folderOpen,
              size: 20,
              color: root == defaultRoot ? tokens.accent : tokens.textSecondary,
            ),
            title: Text(
              root,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodySmall,
            ),
            subtitle: root == defaultRoot
                ? Text(
                    l10n.settingsDefaultRoot,
                    style: Theme.of(context).textTheme.labelSmall
                        ?.copyWith(color: tokens.accent),
                  )
                : null,
            trailing: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (root != defaultRoot)
                  IconButton(
                    onPressed: () => onMakeDefault(root),
                    tooltip: l10n.settingsMakeDefault,
                    icon: const Icon(AppIcons.defaultItem, size: 19),
                  ),
                if (roots.length > 1)
                  IconButton(
                    onPressed: () => onRemove(root),
                    tooltip: l10n.settingsRemoveRoot,
                    icon: Icon(
                      AppIcons.removeItem,
                      size: 19,
                      color: tokens.danger,
                    ),
                  ),
              ],
            ),
          ),
      ],
    );
  }
}
