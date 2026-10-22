import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../services/core/loader_versions.dart';
import '../../stores/instances.dart';
import '../../stores/providers.dart';
import '../../stores/settings.dart';
import '../../theme/tokens.dart';
import 'loader_picker_panel.dart';
import '../common/toast.dart';
import '../../utils/app_paths.dart';

Future<GameInstance?> showCreateInstanceDialog(
  BuildContext context, {
  String? initialVersion,
}) {
  return showDialog<GameInstance>(
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
  String _loaderVersion = GameLoader.fabric.defaultVersion;
  List<LoaderVersionInfo> _loaderVersions = const [];
  bool _loadingVersions = false;
  String? _versionError;
  int _versionRequest = 0;
  String? _selectedRoot;
  bool _versionIsolation = false;
  bool _creating = false;

  @override
  void initState() {
    super.initState();
    final requested = widget.initialVersion;
    _gameVersion = requested == null || requested.isEmpty
        ? _baseGameVersions.first
        : requested;
    _nameController.addListener(_onNameChanged);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _loadLoaderVersions();
    });
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

  /// AlertDialog reserves ~128 px for its insets and content padding; keep a
  /// little slack so the two columns never overflow by a few pixels.
  double _dialogWidth(BuildContext context) {
    final available = MediaQuery.sizeOf(context).width - 140;
    final wide = available.clamp(360.0, 720.0);
    return _loader == GameLoader.vanilla ? available.clamp(360.0, 420.0) : wide;
  }

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
      scrollable: true,
      title: Text(l10n.instanceCreateTitle),
      content: TweenAnimationBuilder<double>(
        tween: Tween<double>(
          begin: _dialogWidth(context),
          end: _dialogWidth(context),
        ),
        duration: tokens.normal,
        curve: tokens.curve,
        builder: (context, width, _) => SizedBox(
          width: width,
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
                        hintText: _versionIsolation
                            ? '$selectedRoot/versions/${slugForInstance(_nameController.text)}'
                            : '$selectedRoot/${slugForInstance(_nameController.text)}',
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
              const SizedBox(height: AppSpacing.xs),
              CheckboxListTile(
                contentPadding: EdgeInsets.zero,
                value: _versionIsolation,
                onChanged: (value) =>
                    setState(() => _versionIsolation = value ?? false),
                title: Text(l10n.instanceVersionIsolation),
                subtitle: Text(l10n.instanceVersionIsolationHint),
                controlAffinity: ListTileControlAffinity.leading,
              ),
              const SizedBox(height: AppSpacing.sm),
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
                  if (value == null) return;
                  setState(() {
                    _gameVersion = value;
                    _loaderVersion = '';
                  });
                  _loadLoaderVersions();
                },
              ),
              const SizedBox(height: AppSpacing.md),
              LoaderPickerPanel(
                loaders: _loaders,
                selectedLoader: _loader,
                onLoaderSelected: (loader) {
                  if (loader == _loader) return;
                  setState(() {
                    _loader = loader;
                    _loaderVersion = '';
                  });
                  _loadLoaderVersions();
                },
                versions: _loaderVersions,
                selectedVersion: _loaderVersion,
                onVersionSelected: (version) =>
                    setState(() => _loaderVersion = version),
                loading: _loadingVersions,
                error: _versionError,
                onRetry: _loadLoaderVersions,
                gameVersion: _gameVersion,
                width: width,
              ),
              const SizedBox(height: AppSpacing.sm),
              Text(
                l10n.instanceCreateSummary(
                  _loader.labelFor(l10n),
                  _gameVersion,
                ),
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
          ),
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

  Future<void> _loadLoaderVersions() async {
    final loader = _loader;
    final game = _gameVersion;
    final request = ++_versionRequest;
    if (loader == GameLoader.vanilla) {
      setState(() {
        _loaderVersions = const [];
        _loaderVersion = '';
        _loadingVersions = false;
        _versionError = null;
      });
      return;
    }

    setState(() {
      _loadingVersions = true;
      _versionError = null;
    });
    final client = ref.read(coreClientProvider);
    if (client == null) {
      if (!mounted || request != _versionRequest) return;
      setState(() {
        _loadingVersions = false;
        _loaderVersion = loader.defaultVersion;
        _versionError = AppLocalizations.of(context).commonCoreNotConnected;
      });
      return;
    }

    try {
      final versions = await client.getLoaderVersions(
        kind: loader.name,
        gameVersion: game,
      );
      if (!mounted || request != _versionRequest) return;
      if (versions.isEmpty) {
        setState(() {
          _loaderVersions = const [];
          _loaderVersion = loader.defaultVersion;
          _loadingVersions = false;
          _versionError = AppLocalizations.of(context).loaderVersionEmpty;
        });
        return;
      }
      final preferred = versions.firstWhere(
        (item) => item.recommended,
        orElse: () => versions.first,
      );
      setState(() {
        _loaderVersions = versions;
        _loaderVersion = preferred.version;
        _loadingVersions = false;
        _versionError = null;
      });
    } catch (error) {
      if (!mounted || request != _versionRequest) return;
      setState(() {
        _loadingVersions = false;
        _loaderVersion = loader.defaultVersion;
        _versionError = AppLocalizations.of(context).loaderVersionLoadFailed;
      });
    }
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
    final slug = slugForInstance(name);
    final rootPath = _versionIsolation
        ? joinPath(joinPath(selectedRoot, 'versions'), slug)
        : (explicitRoot.isEmpty ? joinPath(selectedRoot, slug) : explicitRoot);

    setState(() => _creating = true);
    try {
      final created = await ref
          .read(instancesProvider.notifier)
          .create(
            name: name,
            gameVersion: _gameVersion,
            loader: _loader,
            loaderVersion: _loaderVersion.isEmpty ? null : _loaderVersion,
            rootPath: rootPath,
          );
      if (!mounted) return;
      Navigator.of(context).pop(created);
      toast(l10n.instanceCreated, type: ToastType.success);
    } catch (error) {
      if (mounted) {
        setState(() => _creating = false);
        toast(error.toString(), type: ToastType.error);
      }
    }
  }
}
