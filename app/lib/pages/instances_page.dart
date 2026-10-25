import 'dart:async';

import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../app/shell.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/game_instance.dart';
import '../services/core/downloads/build_policy.dart';
import '../services/core/core_client.dart';
import '../services/core/downloads/task_update.dart';
import '../services/core/launch/launch_action.dart';
import '../stores/downloads.dart';
import '../stores/instances.dart';
import '../stores/providers.dart';
import '../stores/settings.dart';
import '../stores/tasks.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../utils/last_played.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_menu_button.dart';
import '../widgets/common/context_menu.dart';
import '../widgets/common/toast.dart';
import '../widgets/common/loader_mark.dart';
import '../widgets/instances/game_log_dialog.dart';
import '../widgets/instances/instance_duplicate_dialog.dart';
import '../widgets/instances/instance_mods_dialog.dart';
import '../widgets/instances/instance_settings_dialog.dart';
import '../widgets/instances/loader_install_dialog.dart';
import '../widgets/common/app_panel.dart';
import '../widgets/common/app_section.dart';

part 'instances/panels.dart';
part 'instances/dependency_check_dialog.dart';
part 'instances/instance_menu.dart';
part 'instances/instance_tiles.dart';
part 'instances/delete_dialog.dart';

class InstancesPage extends ConsumerStatefulWidget {
  const InstancesPage({super.key});

  @override
  ConsumerState<InstancesPage> createState() => _InstancesPageState();
}

class _InstancesPageState extends ConsumerState<InstancesPage> {
  String _query = '';
  bool _syncing = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _sync(silent: true);
    });
  }

  Future<void> _sync({bool silent = false}) async {
    if (_syncing) return;
    setState(() => _syncing = true);
    try {
      final result = await ref.read(instancesProvider.notifier).sync();
      if (!mounted) return;
      if (!silent ||
          (result != null &&
              (result.adopted.isNotEmpty || result.updated.isNotEmpty))) {
        final l10n = AppLocalizations.of(context);
        final message = StringBuffer(l10n.instanceSyncDone);
        if (result != null && result.adopted.isNotEmpty) {
          message.write(' ${l10n.instanceSyncAdopted(result.adopted.length)}');
        }
        if (result != null && result.missing.isNotEmpty) {
          message.write(' ${l10n.instanceSyncMissing(result.missing.length)}');
        }
        _notify(context, message.toString(), type: ToastType.success);
      }
    } catch (error) {
      if (mounted && !silent) {
        _notify(context, error.toString(), type: ToastType.error);
      }
    } finally {
      if (mounted) setState(() => _syncing = false);
    }
  }

  /// Queues a local `.mrpack` import in the background.
  ///
  /// The file is inspected first so an invalid pack is rejected immediately;
  /// the install itself becomes a native task, so the page never holds a modal
  /// dialog open while the pack downloads and unpacks.
  Future<void> _importPack(AppLocalizations l10n) async {
    const typeGroup = XTypeGroup(
      label: 'Modrinth modpack',
      extensions: <String>['mrpack'],
    );
    final file = await openFile(acceptedTypeGroups: const [typeGroup]);
    if (file == null || !mounted) return;

    try {
      final inspection = await ref
          .read(downloadsProvider.notifier)
          .installModpackFromFile(
            file.path,
            label: file.name,
            successMessage: l10n.packInstallDone,
            failureMessage: l10n.packInstallFailed,
            cancelledMessage: l10n.downloadCancelled(file.name),
            inspectFailedMessage: l10n.packInspectFailed,
            describeError: (error) => switch (error) {
              CoreException(
                code: 'PACK_INVALID' ||
                    'PACK_UNSUPPORTED' ||
                    'PACK_ARCHIVE_INVALID' ||
                    'PACK_ARCHIVE_BOMB' ||
                    'PACK_SYMLINK_ENTRY' ||
                    'PACK_PATH_ESCAPE' ||
                    'PACK_TOO_LARGE' ||
                    'PACK_DUPLICATE_FILE',
              ) =>
                l10n.packInspectFailed,
              CoreException(code: 'PACK_LOADER_UNSUPPORTED') =>
                l10n.packLoaderUnsupported,
              CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
                l10n.packJavaMissing,
              _ => null,
            },
          );
      if (!mounted || inspection == null) return;
      toast(l10n.downloadQueued(inspection.name), type: ToastType.success);
    } catch (error) {
      if (!mounted) return;
      toast('${l10n.packInstallFailed}\n$error', type: ToastType.error);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final instancesAsync = ref.watch(instancesProvider);
    final downloadsEnabled = ref.watch(downloadEnabledProvider);
    final instances = instancesAsync.value ?? const <GameInstance>[];
    final selectedId = ref.watch(selectedInstanceIdProvider);
    final query = _query.trim().toLowerCase();
    final filtered = query.isEmpty
        ? instances
        : instances
              .where(
                (instance) =>
                    instance.name.toLowerCase().contains(query) ||
                    instance.gameVersion.toLowerCase().contains(query) ||
                    instance.loader
                        .labelFor(AppLocalizations.of(context))
                        .toLowerCase()
                        .contains(query),
              )
              .toList(growable: false);

    return CustomScrollView(
      slivers: [
        SliverToBoxAdapter(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              AppPageHeader(
                title: l10n.instancesTitle,
                subtitle: l10n.instancesSubtitle,
                actions: [
                  OutlinedButton.icon(
                    onPressed: _syncing ? null : () => _sync(),
                    icon: _syncing
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(AppIcons.refresh, size: 18),
                    label: Text(l10n.refresh),
                  ),
                  OutlinedButton.icon(
                    onPressed: downloadsEnabled
                        ? () => _importPack(l10n)
                        : null,
                    icon: const Icon(AppIcons.fileDownload, size: 18),
                    label: Text(l10n.importPack),
                  ),
                  FilledButton.icon(
                    // Instance creation starts from the download catalogue:
                    // the version entry owns the create-and-download flow.
                    onPressed: () => openAppBranch(
                      context,
                      AppBranch.download,
                      fallbackPath: '/download',
                    ),
                    icon: const Icon(AppIcons.add, size: 18),
                    label: Text(l10n.createInstance),
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.md),
              _InstancesToolbar(
                query: _query,
                count: filtered.length,
                total: instances.length,
                onChanged: (value) => setState(() => _query = value),
              ),
            ],
          ),
        ),
        if (instancesAsync.isLoading && instances.isEmpty)
          const SliverToBoxAdapter(
            child: Padding(
              padding: EdgeInsets.only(top: AppSpacing.xl),
              child: Center(child: CircularProgressIndicator()),
            ),
          )
        else if (instancesAsync.hasError && instances.isEmpty)
          SliverToBoxAdapter(
            child: _InlineNotice(
              icon: AppIcons.error,
              message: l10n.instancesLoadFailed('${instancesAsync.error}'),
              color: tokens.danger,
            ),
          )
        else if (instances.isEmpty)
          SliverToBoxAdapter(
            child: Column(
              children: [
                _EmptyInstancesPanel(l10n: l10n),
                _InstanceRootCard(
                  roots:
                      ref.watch(appSettingsProvider).value?.roots ?? const [],
                  defaultRoot: ref
                      .watch(appSettingsProvider)
                      .value
                      ?.instancesRoot,
                  onOpenSettings: () => openAppBranch(
                    context,
                    AppBranch.settings,
                    fallbackPath: '/settings',
                  ),
                ),
              ],
            ),
          )
        else if (filtered.isEmpty)
          SliverToBoxAdapter(
            child: _InlineNotice(
              icon: AppIcons.searchEmpty,
              message: l10n.instanceNoMatches,
              color: tokens.textSecondary,
            ),
          )
        else
          SliverToBoxAdapter(
            child: LayoutBuilder(
              builder: (context, constraints) {
                // Rows get very wide on a fullscreen window; switch to a card
                // grid so actions stay near their instance and the page uses
                // the available width.
                final maxColumns = constraints.maxWidth >= 2200
                    ? 5
                    : constraints.maxWidth >= 1850
                    ? 4
                    : constraints.maxWidth >= 1500
                    ? 3
                    : constraints.maxWidth >= 1020
                    ? 2
                    : 1;
                // Fill the row when there are fewer instances than columns,
                // instead of leaving a large empty half-page.
                final columns = filtered.length < maxColumns
                    ? filtered.length
                    : maxColumns;
                final rows = <Widget>[
                  for (var i = 0; i < filtered.length; i++) ...[
                    _InstanceRow(
                      instance: filtered[i],
                      selected: filtered[i].id == selectedId,
                      onSelect: () => ref
                          .read(selectedInstanceIdProvider.notifier)
                          .select(filtered[i].id),
                      onLaunch: () => _launch(filtered[i]),
                      onCheck: (mode) => _check(filtered[i], mode, l10n),
                      onManageLoader: () =>
                          showLoaderInstallDialog(context, filtered[i]),
                      downloadsEnabled: downloadsEnabled,
                      onInstallGame: () => _installGame(filtered[i], l10n),
                      onManageMods: () =>
                          showInstanceModsDialog(context, filtered[i]),
                      onDuplicate: () =>
                          showInstanceDuplicateDialog(context, filtered[i]),
                      onViewLogs: () => showGameLogDialog(context, filtered[i]),
                      onOpenFolder: () =>
                          _openInstanceFolder(filtered[i], l10n),
                      onDelete: () => _delete(filtered[i], l10n),
                    ),
                    if (i < filtered.length - 1)
                      Divider(height: 1, color: tokens.border),
                  ],
                ];

                if (columns == 1) {
                  return Column(
                    children: [
                      ...rows,
                      const SizedBox(height: AppSpacing.lg),
                    ],
                  );
                }

                const gap = AppSpacing.sm;
                final cardWidth =
                    (constraints.maxWidth - gap * (columns - 1)) / columns;
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Wrap(
                      spacing: gap,
                      runSpacing: gap,
                      children: [
                        for (final instance in filtered)
                          SizedBox(
                            width: cardWidth,
                            height: 136,
                            child: _InstanceCard(
                              instance: instance,
                              selected: instance.id == selectedId,
                              onSelect: () => ref
                                  .read(selectedInstanceIdProvider.notifier)
                                  .select(instance.id),
                              onLaunch: () => _launch(instance),
                              onCheck: (mode) => _check(instance, mode, l10n),
                              onManageLoader: () =>
                                  showLoaderInstallDialog(context, instance),
                              downloadsEnabled: downloadsEnabled,
                              onInstallGame: () => _installGame(instance, l10n),
                              onManageMods: () =>
                                  showInstanceModsDialog(context, instance),
                              onDuplicate: () => showInstanceDuplicateDialog(
                                context,
                                instance,
                              ),
                              onViewLogs: () =>
                                  showGameLogDialog(context, instance),
                              onOpenFolder: () =>
                                  _openInstanceFolder(instance, l10n),
                              onDelete: () => _delete(instance, l10n),
                            ),
                          ),
                      ],
                    ),
                    const SizedBox(height: AppSpacing.lg),
                  ],
                );
              },
            ),
          ),
      ],
    );
  }

  /// Queues the vanilla game files in the background; the task center owns
  /// progress, so the page never blocks on a modal dialog.
  Future<void> _installGame(
    GameInstance instance,
    AppLocalizations l10n,
  ) async {
    try {
      await ref
          .read(downloadsProvider.notifier)
          .installGameFiles(
            instance,
            successMessage: l10n.instanceInstallGameDone,
            failureMessage: l10n.instanceInstallGameFailed,
            cancelledMessage: l10n.downloadCancelled(instance.name),
          );
      if (!mounted) return;
      toast(l10n.downloadQueued(instance.name), type: ToastType.success);
    } catch (error) {
      if (!mounted) return;
      toast('${l10n.instanceInstallGameFailed}\n$error', type: ToastType.error);
    }
  }

  void _launch(GameInstance instance) {
    ref.read(selectedInstanceIdProvider.notifier).select(instance.id);
    launchInstance(context, ref, instance);
  }

  Future<void> _delete(GameInstance instance, AppLocalizations l10n) async {
    final deleteFiles = await showDialog<bool>(
      context: context,
      builder: (context) =>
          _InstanceDeleteDialog(l10n: l10n, instance: instance),
    );
    if (deleteFiles == null || !mounted) return;

    // Removing files is irreversible, so it needs its own confirmation that
    // spells out the folder being deleted.
    if (deleteFiles) {
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (context) {
          final tokens = context.tokens;
          return AlertDialog(
            icon: Icon(AppIcons.warning, color: tokens.danger),
            title: Text(l10n.instanceDeleteFilesConfirmTitle),
            content: Text(
              l10n.instanceDeleteFilesConfirmBody(instance.rootPath),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(false),
                child: Text(l10n.commonCancel),
              ),
              FilledButton(
                style: FilledButton.styleFrom(
                  backgroundColor: tokens.danger,
                  foregroundColor: tokens.onAccent,
                ),
                onPressed: () => Navigator.of(context).pop(true),
                child: Text(l10n.instanceDeleteFilesConfirmAction),
              ),
            ],
          );
        },
      );
      if (confirmed != true || !mounted) return;
    }

    final outcome = await ref
        .read(instancesProvider.notifier)
        .delete(instance.id, deleteFiles: deleteFiles);
    if (!mounted) return;

    if (!deleteFiles) {
      _notify(context, l10n.instanceDeleteDone, type: ToastType.success);
      return;
    }
    if (outcome?.filesDeleted == true) {
      _notify(context, l10n.instanceDeleteDoneFiles, type: ToastType.success);
      return;
    }
    _notify(
      context,
      _filesKeptMessage(l10n, outcome?.filesSkippedReason),
      type: ToastType.warning,
    );
  }

  String _filesKeptMessage(AppLocalizations l10n, String? reason) =>
      switch (reason) {
        'missing' => l10n.instanceDeleteFilesKeptMissing,
        'outside_root' => l10n.instanceDeleteFilesKeptOutsideRoot,
        'shared_root' ||
        'nested_instance' ||
        'not_managed' => l10n.instanceDeleteFilesKeptShared,
        'symlink' || 'unsafe_path' => l10n.instanceDeleteFilesKeptUnsafe,
        _ => l10n.instanceDeleteFilesKeptFailed,
      };

  Future<void> _check(
    GameInstance instance,
    String mode,
    AppLocalizations l10n,
  ) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      _notify(context, l10n.commonCoreNotConnected, type: ToastType.error);
      return;
    }

    try {
      final envelope = await client.checkLoader(
        root: instance.rootPath,
        kind: instance.loader.name,
        gameVersion: instance.gameVersion,
        loaderVersion: instance.loaderVersion,
        mode: mode,
      );
      if (!mounted) return;
      await showDialog<void>(
        context: context,
        builder: (context) => _DependencyCheckDialog(
          l10n: l10n,
          instance: instance,
          mode: mode,
          envelope: envelope,
        ),
      );
    } catch (error) {
      if (mounted) _notify(context, error.toString(), type: ToastType.error);
    }
  }

  void _notify(
    BuildContext context,
    String message, {
    ToastType type = ToastType.info,
  }) {
    toast(message, type: type);
  }

  /// Reveals the instance folder through the core; the UI never touches paths
  /// itself.
  Future<void> _openInstanceFolder(
    GameInstance instance,
    AppLocalizations l10n,
  ) async {
    final client = ref.read(coreClientProvider);
    final opened =
        client != null && await client.openSystemPath(instance.rootPath);
    if (!opened && mounted) {
      _notify(context, l10n.settingsOpenFailed, type: ToastType.error);
    }
  }
}
