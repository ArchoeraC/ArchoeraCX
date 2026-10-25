import 'dart:async';

import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/game_instance.dart';
import '../../services/core/core_client.dart';
import '../../services/core/loaders/loader_versions.dart';
import '../../stores/downloads.dart';
import '../../stores/providers.dart';
import '../../stores/settings.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/app_dropdown.dart';
import '../common/toast.dart';
import 'loader_version_picker.dart';

/// Loader installation moved out of the Download hub and into instance
/// management: it always writes into one concrete instance root.
Future<void> showLoaderInstallDialog(
  BuildContext context,
  GameInstance instance,
) {
  return showDialog<void>(
    context: context,
    builder: (_) => LoaderInstallDialog(instance: instance),
  );
}

class LoaderInstallDialog extends ConsumerStatefulWidget {
  const LoaderInstallDialog({super.key, required this.instance});

  final GameInstance instance;

  @override
  ConsumerState<LoaderInstallDialog> createState() =>
      _LoaderInstallDialogState();
}

class _LoaderInstallDialogState extends ConsumerState<LoaderInstallDialog> {
  late final TextEditingController _gameController;
  late final TextEditingController _loaderController;
  late String _kind;
  String? _installerPath;
  bool _busy = false;
  String? _message;
  List<LoaderVersionInfo> _versions = const [];
  bool _loadingVersions = false;
  String? _versionError;
  int _versionRequest = 0;
  Timer? _gameDebounce;

  /// Per-install override of the global "share loader files" preference.
  /// Null follows the setting until the user touches the checkbox.
  bool? _shareLoaderFiles;

  @override
  void initState() {
    super.initState();
    _gameController = TextEditingController(text: widget.instance.gameVersion);
    _loaderController = TextEditingController(
      text: widget.instance.loaderVersion,
    );
    _kind = widget.instance.loader == GameLoader.neoforge
        ? 'neoforge'
        : 'forge';
    _gameController.addListener(_onGameChanged);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _loadVersions();
    });
  }

  void _onGameChanged() {
    _gameDebounce?.cancel();
    setState(() {
      _versions = const [];
      _loaderController.text = '';
      _versionError = null;
    });
    _gameDebounce = Timer(const Duration(milliseconds: 600), () {
      if (mounted) _loadVersions();
    });
  }

  @override
  void dispose() {
    _gameDebounce?.cancel();
    _gameController
      ..removeListener(_onGameChanged)
      ..dispose();
    _loaderController.dispose();
    super.dispose();
  }

  Future<void> _loadVersions() async {
    final game = _gameController.text.trim();
    final request = ++_versionRequest;
    if (game.isEmpty) {
      setState(() {
        _versions = const [];
        _loadingVersions = false;
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
        _versionError = AppLocalizations.of(context).commonCoreNotConnected;
      });
      return;
    }

    try {
      final versions = await client.getLoaderVersions(
        kind: _kind,
        gameVersion: game,
      );
      if (!mounted || request != _versionRequest) return;
      if (versions.isEmpty) {
        setState(() {
          _versions = const [];
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
        _versions = versions;
        _loadingVersions = false;
        _versionError = null;
        _loaderController.text = preferred.version;
      });
    } catch (error) {
      if (!mounted || request != _versionRequest) return;
      setState(() {
        _loadingVersions = false;
        _versionError = AppLocalizations.of(context).loaderVersionLoadFailed;
      });
    }
  }

  Future<void> _pickVersion(AppLocalizations l10n) async {
    if (_versions.isEmpty) {
      setState(() => _versionError ??= l10n.loaderVersionEmpty);
      return;
    }
    final selected = await showLoaderVersionPicker(
      context,
      versions: _versions,
      selected: _loaderController.text.trim(),
      gameVersion: _gameController.text.trim(),
    );
    if (selected != null && mounted) {
      setState(() => _loaderController.text = selected);
    }
  }

  Future<void> _chooseInstaller(AppLocalizations l10n) async {
    final file = await openFile(
      acceptedTypeGroups: <XTypeGroup>[
        XTypeGroup(label: l10n.loaderInstallerLabel, extensions: const ['jar']),
      ],
    );
    if (file == null || !mounted) return;
    setState(() {
      _installerPath = file.path;
      _message = null;
    });
  }

  Future<void> _install(AppLocalizations l10n) async {
    final root = widget.instance.rootPath;
    final installer = _installerPath;
    if (installer == null || installer.isEmpty) {
      setState(() => _message = l10n.loaderInstallPickInstaller);
      return;
    }
    final game = _gameController.text.trim();
    final loaderVersion = _loaderController.text.trim();
    if (root.isEmpty || game.isEmpty || loaderVersion.isEmpty) {
      setState(() => _message = l10n.loaderInstallFailed);
      return;
    }

    final label =
        '${_kind[0].toUpperCase()}${_kind.substring(1)} $loaderVersion';
    setState(() {
      _busy = true;
      _message = null;
    });

    // Pre-flight (Java selection + plan) is awaited so a missing runtime is
    // reported here; everything after that runs in the background queue, so
    // this dialog closes instead of waiting out the transfer and processors.
    try {
      await ref
          .read(downloadsProvider.notifier)
          .installLoader(
            instance: widget.instance,
            kind: _kind,
            gameVersion: game,
            loaderVersion: loaderVersion,
            installerPath: installer,
            shareLoaderFiles: _shareLoaderFiles,
            successMessage: l10n.loaderInstallDone,
            failureMessage: l10n.loaderInstallFailed,
            cancelledMessage: l10n.downloadCancelled(widget.instance.name),
            describeError: (error) => switch (error) {
              CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
                l10n.launchJavaMissing,
              CoreException(code: 'DOWNLOAD_DISABLED') =>
                l10n.downloadUnavailableTitle,
              _ => null,
            },
          );
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _message = switch (error) {
          CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
            l10n.launchJavaMissing,
          CoreException(code: 'DOWNLOAD_DISABLED') =>
            l10n.downloadUnavailableTitle,
          _ => error.toString(),
        };
      });
      return;
    }

    if (!mounted) return;
    Navigator.of(context).pop();
    toast(l10n.downloadQueued(label), type: ToastType.success);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return Dialog(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxWidth: 620,
          maxHeight: MediaQuery.sizeOf(context).height * 0.82,
        ),
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      l10n.loaderInstallTitle,
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                  ),
                  IconButton(
                    onPressed: _busy ? null : () => Navigator.of(context).pop(),
                    tooltip: l10n.commonClose,
                    icon: const Icon(AppIcons.close),
                  ),
                ],
              ),
              Text(
                '${widget.instance.name} · ${widget.instance.rootPath}',
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
              const SizedBox(height: AppSpacing.md),
              Wrap(
                spacing: AppSpacing.sm,
                runSpacing: AppSpacing.sm,
                children: [
                  SizedBox(
                    width: 180,
                    child: AppDropdownFormField<String>(
                      initialValue: _kind,
                      decoration: InputDecoration(
                        labelText: l10n.loaderKindLabel,
                      ),
                      items: [
                        DropdownMenuItem(
                          value: 'forge',
                          child: Text(l10n.loaderForge),
                        ),
                        DropdownMenuItem(
                          value: 'neoforge',
                          child: Text(l10n.loaderNeoForge),
                        ),
                      ],
                      onChanged: _busy
                          ? null
                          : (value) {
                              if (value == null) return;
                              setState(() {
                                _kind = value;
                                _versions = const [];
                                _loaderController.text = '';
                              });
                              _loadVersions();
                            },
                    ),
                  ),
                  SizedBox(
                    width: 160,
                    child: TextField(
                      controller: _gameController,
                      enabled: !_busy,
                      decoration: InputDecoration(
                        labelText: l10n.loaderGameVersionLabel,
                      ),
                    ),
                  ),
                  SizedBox(
                    width: 220,
                    child: TextField(
                      controller: _loaderController,
                      enabled: !_busy,
                      decoration: InputDecoration(
                        labelText: l10n.loaderVersionLabel,
                        suffixIcon: IconButton(
                          onPressed: _busy || _loadingVersions
                              ? null
                              : () => _pickVersion(l10n),
                          tooltip: l10n.loaderVersionSelect,
                          icon: _loadingVersions
                              ? const SizedBox(
                                  width: 15,
                                  height: 15,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                )
                              : const Icon(AppIcons.list, size: 19),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.md),
              // Stacked instead of a Row: the installer path and the localized
              // button label together do not fit a 620px dialog on every
              // language, and a squeezed button overflows its own row.
              InputDecorator(
                decoration: InputDecoration(
                  labelText: l10n.loaderInstallerLabel,
                ),
                child: Text(
                  _installerPath ?? '—',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: _installerPath == null
                        ? tokens.textTertiary
                        : tokens.textSecondary,
                  ),
                ),
              ),
              const SizedBox(height: AppSpacing.xs),
              Align(
                alignment: Alignment.centerRight,
                child: OutlinedButton.icon(
                  onPressed: _busy ? null : () => _chooseInstaller(l10n),
                  icon: const Icon(AppIcons.modpackFile, size: 18),
                  label: Text(l10n.loaderChooseInstaller),
                ),
              ),
              const SizedBox(height: AppSpacing.sm),
              // Follows the global setting until the user changes it here;
              // the choice applies to this install only.
              CheckboxListTile(
                value:
                    _shareLoaderFiles ??
                    (ref.watch(appSettingsProvider).value?.shareLoaderFiles ??
                        true),
                onChanged: _busy
                    ? null
                    : (value) => setState(() => _shareLoaderFiles = value),
                contentPadding: EdgeInsets.zero,
                controlAffinity: ListTileControlAffinity.leading,
                dense: true,
                title: Text(
                  l10n.loaderShareFiles,
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
                subtitle: Text(
                  l10n.loaderShareFilesHint,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ),
              if (_message != null) ...[
                const SizedBox(height: AppSpacing.sm),
                Text(
                  _message!,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.danger),
                ),
              ],
              const SizedBox(height: AppSpacing.lg),
              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  TextButton(
                    onPressed: _busy ? null : () => Navigator.of(context).pop(),
                    child: Text(l10n.commonClose),
                  ),
                  const SizedBox(width: AppSpacing.xs),
                  FilledButton.icon(
                    onPressed: _busy ? null : () => _install(l10n),
                    icon: _busy
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(AppIcons.repair, size: 18),
                    label: Text(
                      _busy ? l10n.loaderInstalling : l10n.loaderInstallAction,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
