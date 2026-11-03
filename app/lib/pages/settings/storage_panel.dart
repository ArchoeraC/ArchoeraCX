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
        _AppearanceTile(
          icon: AppIcons.reset,
          title: l10n.settingsUnusedDependencies,
          subtitle: l10n.settingsUnusedDependenciesDesc,
          trailing: Icon(
            AppIcons.chevronRight,
            size: 18,
            color: context.tokens.textTertiary,
          ),
          onTap: () => _cleanUnusedDependencies(context, ref, l10n),
        ),
      ],
    );
  }

  /// Opens the unused-dependency stepper: scan, preview, typed confirmation
  /// and the verified deletion. The dialog owns every stage so a slow scan
  /// never leaves a stack of stacked dialogs behind.
  Future<void> _cleanUnusedDependencies(
    BuildContext context,
    WidgetRef ref,
    AppLocalizations l10n,
  ) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      _notify(context, l10n.commonCoreNotConnected, type: ToastType.error);
      return;
    }

    await showDialog<void>(
      context: context,
      builder: (dialogContext) =>
          _UnusedDependenciesDialog(client: client, l10n: l10n),
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

/// Stepper dialog for the unused-dependency cleanup: scan, preview, typed
/// confirmation and the verified deletion all live in one surface so a slow
/// scan cannot stack dialogs behind each other.
class _UnusedDependenciesDialog extends StatefulWidget {
  const _UnusedDependenciesDialog({required this.client, required this.l10n});

  final CoreClient client;
  final AppLocalizations l10n;

  @override
  State<_UnusedDependenciesDialog> createState() =>
      _UnusedDependenciesDialogState();
}

class _UnusedDependenciesDialogState extends State<_UnusedDependenciesDialog> {
  static const _scanning = 'scanning';
  static const _empty = 'empty';
  static const _preview = 'preview';
  static const _confirm = 'confirm';
  static const _deleting = 'deleting';
  static const _stageDone = 'done';
  static const _stageError = 'error';

  String _stage = _scanning;
  UnusedDependencyPlan? _plan;
  UnusedDependencyPlan? _result;
  String? _error;
  int _done = 0;
  int _total = 0;
  final TextEditingController _word = TextEditingController();

  @override
  void initState() {
    super.initState();
    _scan();
  }

  @override
  void dispose() {
    _word.dispose();
    super.dispose();
  }

  Future<void> _scan() async {
    try {
      final plan = await widget.client.planUnusedDependencies();
      if (!mounted) return;
      setState(() {
        _plan = plan;
        _stage = plan.totalFiles == 0 ? _empty : _preview;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = '$error';
        _stage = _stageError;
      });
    }
  }

  Future<void> _delete() async {
    setState(() {
      _stage = _deleting;
      _done = 0;
      _total = _plan?.totalFiles ?? 0;
    });
    try {
      final result = await widget.client.deleteUnusedDependencies(
        onProgress: (done, total) {
          if (!mounted) return;
          setState(() {
            _done = done;
            _total = total;
          });
        },
      );
      if (!mounted) return;
      setState(() {
        _result = result;
        _stage = _stageDone;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = '$error';
        _stage = _stageError;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = widget.l10n;
    return AlertDialog(
      icon: Icon(
        AppIcons.warning,
        color: _stage == _confirm || _stage == _deleting
            ? context.tokens.danger
            : context.tokens.accent,
      ),
      title: Text(l10n.settingsUnusedDependenciesTitle),
      content: _content(context, l10n),
      actions: _actions(context, l10n),
    );
  }

  Widget? _content(BuildContext context, AppLocalizations l10n) {
    switch (_stage) {
      case _scanning:
        return Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            const CircularProgressIndicator(),
            const SizedBox(width: AppSpacing.md),
            Text(l10n.settingsUnusedDependenciesScanning),
          ],
        );
      case _empty:
        return Text(l10n.settingsUnusedNone);
      case _preview:
        return _previewContent(context, l10n);
      case _confirm:
        return _confirmContent(l10n);
      case _deleting:
        return _progressContent();
      case _stageDone:
        return _doneContent(l10n);
    }
    return Text(_error ?? '');
  }

  Widget _previewContent(BuildContext context, AppLocalizations l10n) {
    final plan = _plan!;
    return SizedBox(
      width: 420,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.settingsUnusedDependenciesNote,
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: AppSpacing.sm),
          Flexible(
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  for (final root in plan.roots) _rootBlock(context, l10n, root),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _rootBlock(
    BuildContext context,
    AppLocalizations l10n,
    UnusedDependencyRootPlan root,
  ) {
    final tokens = context.tokens;
    return Padding(
      padding: const EdgeInsets.only(bottom: AppSpacing.xs),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            root.root,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context)
                .textTheme
                .labelLarge
                ?.copyWith(fontWeight: FontWeight.w600),
          ),
          if (root.skipped)
            Text(
              _skipText(l10n, root.reason),
              style: Theme.of(context)
                  .textTheme
                  .labelMedium
                  ?.copyWith(color: tokens.textTertiary),
            )
          else ...[
            Text(
              l10n.settingsUnusedInstances(root.instanceCount),
              style: Theme.of(context)
                  .textTheme
                  .labelMedium
                  ?.copyWith(color: tokens.textTertiary),
            ),
            if (root.libraries != null)
              _groupLine(context, l10n.settingsUnusedLibraries, root.libraries!),
            if (root.assets != null)
              _groupLine(context, l10n.settingsUnusedAssets, root.assets!),
            if (root.assets == null && root.libraries != null)
              Text(
                l10n.settingsUnusedAssetsSkipped,
                style: Theme.of(context)
                    .textTheme
                    .labelMedium
                    ?.copyWith(color: tokens.textTertiary),
              ),
          ],
        ],
      ),
    );
  }

  Widget _groupLine(
    BuildContext context,
    String label,
    UnusedDependencyGroupPlan group,
  ) {
    final tokens = context.tokens;
    return Padding(
      padding: const EdgeInsets.only(left: AppSpacing.sm),
      child: Text(
        '$label: ${group.files} · ${formatBytes(group.bytes)}',
        style: Theme.of(context)
            .textTheme
            .bodyMedium
            ?.copyWith(color: tokens.textPrimary),
      ),
    );
  }

  Widget _confirmContent(AppLocalizations l10n) {
    final plan = _plan!;
    final word = l10n.settingsUnusedConfirmWord;
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          l10n.settingsUnusedConfirmBody(
            plan.totalFiles,
            formatBytes(plan.totalBytes),
          ),
        ),
        const SizedBox(height: AppSpacing.md),
        TextField(
          controller: _word,
          autofocus: true,
          decoration: InputDecoration(
            hintText: l10n.settingsUnusedConfirmHint(word),
          ),
          onChanged: (_) => setState(() {}),
        ),
      ],
    );
  }

  Widget _progressContent() {
    final total = _total > 0 ? _total : 1;
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        AppProgressBar(value: _done / total),
        const SizedBox(height: AppSpacing.sm),
        Text('$_done / $_total'),
      ],
    );
  }

  Widget _doneContent(AppLocalizations l10n) {
    final result = _result!;
    final skipped = result.roots.where((root) => root.skipped).toList();
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          l10n.settingsUnusedDone(
            result.totalFiles,
            formatBytes(result.totalBytes),
          ),
        ),
        for (final root in skipped)
          Padding(
            padding: const EdgeInsets.only(top: AppSpacing.xs),
            child: Text(
              '${root.root}: ${_skipText(l10n, root.reason)}',
              style: Theme.of(context)
                  .textTheme
                  .labelMedium
                  ?.copyWith(color: context.tokens.textTertiary),
            ),
          ),
      ],
    );
  }

  String _skipText(AppLocalizations l10n, String? reason) => switch (reason) {
        'missing' => l10n.settingsUnusedSkipMissing,
        'no_instances' => l10n.settingsUnusedSkipNoInstances,
        'unreadable_profile' => l10n.settingsUnusedSkipProfile,
        _ => l10n.settingsUnusedSkipLimit,
      };

  List<Widget> _actions(BuildContext context, AppLocalizations l10n) {
    final tokens = context.tokens;
    switch (_stage) {
      case _scanning:
      case _deleting:
        return const [];
      case _empty:
      case _stageDone:
      case _stageError:
        return [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: Text(l10n.commonClose),
          ),
        ];
      case _preview:
        return [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: Text(l10n.commonCancel),
          ),
          FilledButton(
            onPressed: () => setState(() => _stage = _confirm),
            child: Text(l10n.commonNext),
          ),
        ];
      case _confirm:
        final matches =
            _word.text.trim().toUpperCase() ==
            l10n.settingsUnusedConfirmWord.toUpperCase();
        return [
          TextButton(
            onPressed: () => setState(() => _stage = _preview),
            child: Text(l10n.commonBack),
          ),
          FilledButton(
            style: FilledButton.styleFrom(
              backgroundColor: tokens.danger,
              foregroundColor: tokens.onAccent,
            ),
            onPressed: matches ? _delete : null,
            child: Text(l10n.commonDelete),
          ),
        ];
    }
    return const [];
  }
}
