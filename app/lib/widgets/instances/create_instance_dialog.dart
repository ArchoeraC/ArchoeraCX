import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../stores/instances.dart';
import '../../stores/settings.dart';
import '../../theme/tokens.dart';
import '../common/loader_mark.dart';
import '../common/toast.dart';
import '../../utils/app_paths.dart';

Future<void> showCreateInstanceDialog(
  BuildContext context, {
  String? initialVersion,
}) {
  return showDialog<void>(
    context: context,
    builder: (context) => CreateInstanceDialog(initialVersion: initialVersion),
  );
}

class CreateInstanceDialog extends ConsumerStatefulWidget {
  const CreateInstanceDialog({super.key, this.initialVersion});

  final String? initialVersion;

  @override
  ConsumerState<CreateInstanceDialog> createState() =>
      _CreateInstanceDialogState();
}

class _CreateInstanceDialogState extends ConsumerState<CreateInstanceDialog> {
  static const _baseGameVersions = ['1.21.4', '1.21.1', '1.20.1', '1.19.4'];
  static const _loaders = [
    GameLoader.vanilla,
    GameLoader.fabric,
    GameLoader.quilt,
    GameLoader.forge,
    GameLoader.neoforge,
  ];

  final _nameController = TextEditingController();
  final _rootController = TextEditingController();
  late String _gameVersion;
  GameLoader _loader = GameLoader.fabric;
  String? _selectedRoot;
  bool _creating = false;

  @override
  void initState() {
    super.initState();
    final requested = widget.initialVersion;
    _gameVersion = requested == null || requested.isEmpty
        ? _baseGameVersions.first
        : requested;
    _nameController.addListener(_onNameChanged);
  }

  @override
  void dispose() {
    _nameController
      ..removeListener(_onNameChanged)
      ..dispose();
    _rootController.dispose();
    super.dispose();
  }

  void _onNameChanged() => setState(() {});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final settings = ref.watch(appSettingsProvider).value;
    final roots = settings?.roots ?? <String>[fallbackInstancesRoot()];
    final selectedRoot = _selectedRoot != null && roots.contains(_selectedRoot)
        ? _selectedRoot!
        : roots.first;
    final canCreate = _nameController.text.trim().isNotEmpty;

    return AlertDialog(
      title: Text(l10n.instanceCreateTitle),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextField(
              controller: _nameController,
              autofocus: true,
              textInputAction: TextInputAction.done,
              onSubmitted: canCreate ? (_) => _create(l10n) : null,
              decoration: InputDecoration(
                labelText: l10n.instanceNameLabel,
                hintText: l10n.instanceNameHint,
              ),
            ),
            const SizedBox(height: AppSpacing.md),
            DropdownButtonFormField<String>(
              initialValue: selectedRoot,
              isExpanded: true,
              decoration: InputDecoration(labelText: l10n.instanceRootLabel),
              items: [
                for (final root in roots)
                  DropdownMenuItem(
                    value: root,
                    child: Text(
                      root,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
              ],
              onChanged: (value) {
                if (value != null) setState(() => _selectedRoot = value);
              },
            ),
            const SizedBox(height: AppSpacing.md),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: TextField(
                    controller: _rootController,
                    decoration: InputDecoration(
                      labelText: l10n.instanceLocationLabel,
                      hintText:
                          '$selectedRoot/${slugForInstance(_nameController.text)}',
                      helperText: l10n.instanceLocationHint,
                    ),
                  ),
                ),
                const SizedBox(width: AppSpacing.xs),
                IconButton(
                  onPressed: _chooseRoot,
                  tooltip: l10n.instanceChooseLocation,
                  icon: const Icon(Icons.folder_open_rounded),
                ),
              ],
            ),
            const SizedBox(height: AppSpacing.md),
            DropdownButtonFormField<String>(
              initialValue: _gameVersion,
              decoration: InputDecoration(
                labelText: l10n.instanceGameVersionLabel,
              ),
              items: [
                for (final version in [
                  if (!_baseGameVersions.contains(_gameVersion)) _gameVersion,
                  ..._baseGameVersions,
                ])
                  DropdownMenuItem(value: version, child: Text(version)),
              ],
              onChanged: (value) {
                if (value != null) setState(() => _gameVersion = value);
              },
            ),
            const SizedBox(height: AppSpacing.md),
            DropdownButtonFormField<GameLoader>(
              initialValue: _loader,
              decoration: InputDecoration(labelText: l10n.instanceLoaderLabel),
              items: [
                for (final loader in _loaders)
                  DropdownMenuItem(
                    value: loader,
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        LoaderMark(loader: loader, size: 18),
                        const SizedBox(width: AppSpacing.xs),
                        Flexible(
                          child: Text(
                            loader == GameLoader.vanilla
                                ? loader.labelFor(l10n)
                                : l10n.loaderVersionValue(
                                    loader.labelFor(l10n),
                                    loader.defaultVersion,
                                  ),
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                      ],
                    ),
                  ),
              ],
              onChanged: (value) {
                if (value != null) setState(() => _loader = value);
              },
            ),
            const SizedBox(height: AppSpacing.sm),
            Text(
              l10n.instanceCreateSummary(_loader.labelFor(l10n), _gameVersion),
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textTertiary),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.commonCancel),
        ),
        FilledButton(
          onPressed: canCreate && !_creating ? () => _create(l10n) : null,
          child: _creating
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : Text(l10n.commonCreate),
        ),
      ],
    );
  }

  Future<void> _chooseRoot() async {
    final selected = await getDirectoryPath(
      confirmButtonText: AppLocalizations.of(context).instanceChooseLocation,
    );
    if (selected != null && mounted) {
      _rootController.text = selected;
      setState(() {});
    }
  }

  Future<void> _create(AppLocalizations l10n) async {
    final name = _nameController.text.trim();
    if (name.isEmpty || _creating) return;

    final settings = ref.read(appSettingsProvider).value;
    final roots = settings?.roots ?? <String>[fallbackInstancesRoot()];
    final selectedRoot = _selectedRoot != null && roots.contains(_selectedRoot)
        ? _selectedRoot!
        : roots.first;
    final explicitRoot = _rootController.text.trim();
    final rootPath = explicitRoot.isEmpty
        ? joinPath(selectedRoot, slugForInstance(name))
        : explicitRoot;

    setState(() => _creating = true);
    try {
      await ref
          .read(instancesProvider.notifier)
          .create(
            name: name,
            gameVersion: _gameVersion,
            loader: _loader,
            rootPath: rootPath,
          );
      if (!mounted) return;
      Navigator.of(context).pop();
      toast(l10n.instanceCreated, type: ToastType.success);
    } catch (error) {
      if (mounted) {
        setState(() => _creating = false);
        toast(error.toString(), type: ToastType.error);
      }
    }
  }
}
