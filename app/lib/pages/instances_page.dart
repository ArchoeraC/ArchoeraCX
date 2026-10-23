import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/game_instance.dart';
import '../services/core/build_policy.dart';
import '../services/core/core_client.dart';
import '../services/launch/launch_action.dart';
import '../stores/downloads.dart';
import '../stores/instances.dart';
import '../stores/providers.dart';
import '../stores/settings.dart';
import '../stores/tasks.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_progress_bar.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/toast.dart';
import '../widgets/common/loader_mark.dart';
import '../widgets/instances/game_log_dialog.dart';
import '../widgets/instances/instance_duplicate_dialog.dart';
import '../widgets/instances/instance_mods_dialog.dart';
import '../widgets/instances/instance_settings_dialog.dart';
import '../widgets/instances/loader_install_dialog.dart';
import '../widgets/download/pack_install_dialog.dart';
import '../widgets/common/app_panel.dart';
import '../widgets/common/app_section.dart';
import '../widgets/instances/create_instance_dialog.dart';

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

  Future<void> _importPack(AppLocalizations l10n) async {
    const typeGroup = XTypeGroup(
      label: 'Modrinth modpack',
      extensions: <String>['mrpack'],
    );
    final file = await openFile(acceptedTypeGroups: const [typeGroup]);
    if (file == null || !mounted) return;
    await showPackInstallDialog(
      context,
      localPath: file.path,
      title: l10n.packImportTitle,
      taskLabel: file.name,
    );
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
                    onPressed: () => showCreateInstanceDialog(context),
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
                  onOpenSettings: () => context.go('/settings'),
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
                              onDuplicate: () =>
                                  showInstanceDuplicateDialog(context, instance),
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

class _InstancesToolbar extends StatelessWidget {
  const _InstancesToolbar({
    required this.query,
    required this.count,
    required this.total,
    required this.onChanged,
  });

  final String query;
  final int count;
  final int total;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    final field = TextField(
      onChanged: onChanged,
      decoration: InputDecoration(
        hintText: l10n.searchInstances,
        prefixIcon: const Icon(AppIcons.search, size: 20),
        isDense: true,
      ),
    );
    final countLabel = Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(AppIcons.gridView, size: 16, color: tokens.textTertiary),
        const SizedBox(width: AppSpacing.xxs),
        Text(
          query.isEmpty ? l10n.instancesCount(total) : '$count / $total',
          style: Theme.of(context).textTheme.labelMedium
              ?.copyWith(color: tokens.textSecondary),
        ),
      ],
    );

    return AppSection(
      showTop: true,
      showBottom: true,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 720) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                field,
                const SizedBox(height: AppSpacing.xs),
                Row(
                  mainAxisAlignment: MainAxisAlignment.end,
                  children: [countLabel],
                ),
              ],
            );
          }

          return Row(
            children: [
              SizedBox(width: 380, child: field),
              const Spacer(),
              countLabel,
            ],
          );
        },
      ),
    );
  }
}

class _EmptyInstancesPanel extends StatelessWidget {
  const _EmptyInstancesPanel({required this.l10n});

  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    final intro = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(AppIcons.gridView, size: 30, color: tokens.accent),
        const SizedBox(height: AppSpacing.sm),
        Text(l10n.instanceEmptyTitle, style: textTheme.titleLarge),
        const SizedBox(height: AppSpacing.xs),
        ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: Text(
            l10n.instanceEmptyMessage,
            style: textTheme.bodyMedium?.copyWith(color: tokens.textSecondary),
          ),
        ),
        const SizedBox(height: AppSpacing.lg),
        Wrap(
          spacing: AppSpacing.xs,
          runSpacing: AppSpacing.xs,
          children: [
            FilledButton.icon(
              onPressed: () => showCreateInstanceDialog(context),
              icon: const Icon(AppIcons.add, size: 18),
              label: Text(l10n.createInstance),
            ),
            OutlinedButton.icon(
              onPressed: () => _showUnavailable(context, l10n),
              icon: const Icon(AppIcons.fileDownload, size: 18),
              label: Text(l10n.importPack),
            ),
          ],
        ),
      ],
    );

    final guide = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        _GuideRow(
          index: 1,
          title: l10n.instanceNameLabel,
          subtitle: l10n.instanceNameHint,
        ),
        _GuideRow(
          index: 2,
          title: l10n.instanceGameVersionLabel,
          subtitle: l10n.instanceLoaderLabel,
        ),
        _GuideRow(
          index: 3,
          title: l10n.instanceLocationLabel,
          subtitle: l10n.instanceLocationHint,
          last: true,
        ),
      ],
    );

    return AppSection(
      showBottom: true,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 760) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                intro,
                const SizedBox(height: AppSpacing.lg),
                Divider(height: 1, color: tokens.border),
                const SizedBox(height: AppSpacing.md),
                guide,
              ],
            );
          }

          return IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(child: intro),
                const SizedBox(width: AppSpacing.xl),
                VerticalDivider(width: 1, color: tokens.border),
                const SizedBox(width: AppSpacing.xl),
                SizedBox(width: 320, child: guide),
              ],
            ),
          );
        },
      ),
    );
  }

  void _showUnavailable(BuildContext context, AppLocalizations l10n) {
    toast(l10n.commonCoreNotConnected, type: ToastType.error);
  }
}

class _GuideRow extends StatelessWidget {
  const _GuideRow({
    required this.index,
    required this.title,
    required this.subtitle,
    this.last = false,
  });

  final int index;
  final String title;
  final String subtitle;
  final bool last;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Padding(
      padding: EdgeInsets.only(bottom: last ? 0 : AppSpacing.sm),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 20,
            child: Text(
              '$index',
              style: textTheme.labelLarge?.copyWith(color: tokens.accent),
            ),
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
                  style: textTheme.labelLarge,
                ),
                const SizedBox(height: 1),
                Text(
                  subtitle,
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
    );
  }
}

class _InstanceRootCard extends StatelessWidget {
  const _InstanceRootCard({
    required this.roots,
    required this.defaultRoot,
    required this.onOpenSettings,
  });

  final List<String> roots;
  final String? defaultRoot;
  final VoidCallback onOpenSettings;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
      child: Row(
        children: [
          Icon(AppIcons.folder, size: 18, color: tokens.textSecondary),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  l10n.settingsInstanceRoots,
                  style: textTheme.labelMedium?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  roots.isEmpty
                      ? '—'
                      : roots.length == 1
                      ? roots.first
                      : '${defaultRoot ?? roots.first}  +${roots.length - 1}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodySmall?.copyWith(
                    color: tokens.textSecondary,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          TextButton(
            onPressed: onOpenSettings,
            child: Text(l10n.settingsStorage),
          ),
        ],
      ),
    );
  }
}

class _InlineNotice extends StatelessWidget {
  const _InlineNotice({
    required this.icon,
    required this.message,
    required this.color,
  });

  final IconData icon;
  final String message;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Padding(
      padding: const EdgeInsets.only(top: AppSpacing.xl),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 18, color: color),
          const SizedBox(width: AppSpacing.xs),
          Flexible(
            child: Text(
              message,
              style: Theme.of(context).textTheme.bodyMedium
                  ?.copyWith(color: tokens.textSecondary),
            ),
          ),
        ],
      ),
    );
  }
}

class _DependencyCheckDialog extends ConsumerStatefulWidget {
  const _DependencyCheckDialog({
    required this.l10n,
    required this.instance,
    required this.mode,
    required this.envelope,
  });

  final AppLocalizations l10n;
  final GameInstance instance;
  final String mode;
  final Map<String, Object?> envelope;

  @override
  ConsumerState<_DependencyCheckDialog> createState() =>
      _DependencyCheckDialogState();
}

class _DependencyCheckDialogState
    extends ConsumerState<_DependencyCheckDialog> {
  late Map<String, Object?> _envelope;
  bool _busy = false;
  bool _repaired = false;
  String? _error;
  String? _phase;
  int _done = 0;
  int _total = 0;

  @override
  void initState() {
    super.initState();
    _envelope = widget.envelope;
  }

  bool get _canRepair => true;

  void _cancelRepair() {
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    for (final task in ref.read(taskCenterProvider)) {
      if (task.isTerminal || task.method != 'loader.autoRepair') continue;
      try {
        client.cancel(task.taskId);
        setState(() => _phase = widget.l10n.instanceCheckRepairing);
      } catch (error) {
        if (mounted) setState(() => _error = error.toString());
      }

      return;
    }
  }

  Future<void> _repair() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(() => _error = widget.l10n.commonCoreNotConnected);
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
      _phase = widget.l10n.instanceCheckRepairing;
      _done = 0;
      _total = 0;
    });

    try {
      await client.repairInstanceDependencies(
        widget.instance.id,
        label: widget.instance.name,
        onDetailedProgress: (done, total, message) {
          if (!mounted) return;
          setState(() {
            _done = done;
            _total = total;
            if (message != null && message.isNotEmpty) _phase = message;
          });
        },
        onProgress: (done, total) {
          if (!mounted) return;
          setState(() {
            _done = done;
            _total = total;
          });
        },
      );

      final envelope = await client.checkLoader(
        root: widget.instance.rootPath,
        kind: widget.instance.loader.name,
        gameVersion: widget.instance.gameVersion,
        loaderVersion: widget.instance.loaderVersion,
        mode: widget.mode,
      );
      if (!mounted) return;
      setState(() {
        _envelope = envelope;
        _busy = false;
        _repaired = true;
        _phase = null;
      });
    } catch (error) {
      if (!mounted) return;
      final l10n = widget.l10n;
      setState(() {
        _busy = false;
        _phase = null;
        _error = switch (error) {
          CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
            l10n.launchJavaMissing,
          CoreException(code: 'DOWNLOAD_DISABLED') =>
            l10n.downloadUnavailableTitle,
          _ => '${l10n.instanceCheckRepairFailed} $error',
        };
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = widget.l10n;
    final tokens = context.tokens;
    final data = _envelope['data'] is Map
        ? _envelope['data'] as Map<Object?, Object?>
        : const <Object?, Object?>{};
    final verification = data['verification'] is Map
        ? data['verification'] as Map<Object?, Object?>
        : const <Object?, Object?>{};
    final missing = verification['missing'] is List
        ? (verification['missing'] as List).length
        : 0;
    final mismatched = verification['mismatched'] is List
        ? (verification['mismatched'] as List).length
        : 0;
    final launchFiles = data['launchFiles'] is Map
        ? data['launchFiles'] as Map<Object?, Object?>
        : const <Object?, Object?>{};
    final launchOk = launchFiles['ok'] != false;
    final launchIssue =
        launchFiles['message']?.toString() ?? launchFiles['code']?.toString();
    final ok =
        _envelope['ok'] == true && verification['ok'] == true && launchOk;
    final error = _envelope['error'] is Map
        ? _envelope['error'] as Map<Object?, Object?>
        : null;
    final errorCode = error?['code']?.toString();
    final errorMessage = errorCode == 'MANIFEST_MISSING'
        ? l10n.instanceManifestMissing
        : error?['message']?.toString() ?? l10n.instanceCheckFailed;
    final downloadsEnabled = ref.watch(downloadEnabledProvider);
    final progress = _total > 0 ? (_done / _total).clamp(0.0, 1.0) : null;

    return AlertDialog(
      title: Text(l10n.instanceCheckTitle),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              widget.mode == 'full'
                  ? l10n.instanceCheckFull
                  : l10n.instanceCheckQuick,
              style: Theme.of(context).textTheme.labelLarge
                  ?.copyWith(color: tokens.accent),
            ),
            const SizedBox(height: AppSpacing.sm),
            if (ok)
              Text(
                _repaired ? l10n.instanceCheckRepairDone : l10n.instanceCheckOk,
                style: TextStyle(color: tokens.success),
              )
            else if (error != null)
              Text(errorMessage, style: TextStyle(color: tokens.danger))
            else ...[
              Text(
                l10n.instanceCheckFailed,
                style: TextStyle(color: tokens.danger),
              ),
              if (missing > 0) Text(l10n.instanceCheckMissing(missing)),
              if (mismatched > 0)
                Text(l10n.instanceCheckMismatched(mismatched)),
              if (!launchOk && launchIssue != null)
                Text(l10n.instanceCheckLaunchFiles(launchIssue)),
            ],
            if (!ok && _canRepair) ...[
              const SizedBox(height: AppSpacing.sm),
              Text(
                l10n.instanceCheckRepairHint,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
            if (_busy) ...[
              const SizedBox(height: AppSpacing.md),
              AppProgressBar(value: progress),
              const SizedBox(height: AppSpacing.xxs),
              Text(
                _phase ?? l10n.instanceCheckRepairing,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: AppSpacing.sm),
              Text(
                _error!,
                maxLines: 4,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(color: tokens.danger),
              ),
            ],
          ],
        ),
      ),
      actions: [
        if (_busy)
          OutlinedButton(
            onPressed: _cancelRepair,
            child: Text(l10n.commonCancel),
          ),
        if (!ok && _canRepair && downloadsEnabled)
          FilledButton.icon(
            onPressed: _busy ? null : _repair,
            icon: const Icon(AppIcons.repair, size: 18),
            label: Text(
              _busy ? l10n.instanceCheckRepairing : l10n.instanceCheckRepair,
            ),
          ),
        TextButton(
          onPressed: _busy ? null : () => Navigator.of(context).pop(),
          child: Text(l10n.commonClose),
        ),
      ],
    );
  }
}

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
                        Row(
                          children: [
                            Flexible(
                              child: Text(
                                _instanceMeta(instance, l10n),
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: textTheme.bodySmall?.copyWith(
                                  color: instance.missing
                                      ? tokens.danger
                                      : tokens.textTertiary,
                                ),
                              ),
                            ),
                            if (_hasOverrides(instance)) ...[
                              const SizedBox(width: AppSpacing.xxs),
                              _OverridesBadge(l10n: l10n, overrides: instance.overrides!),
                            ],
                          ],
                        ),
                      ],
                    ),
                  ),
                  if (wide) ...[
                    const SizedBox(width: AppSpacing.md),
                    Text(
                      _lastPlayedLabel(context, l10n, instance.lastPlayed),
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

class _InstanceMenu extends StatelessWidget {
  const _InstanceMenu({
    required this.l10n,
    required this.downloadsEnabled,
    required this.onCheck,
    required this.onManageLoader,
    required this.onSettings,
    required this.onInstallGame,
    required this.onManageMods,
    required this.onDuplicate,
    required this.onViewLogs,
    required this.onOpenFolder,
    required this.onDelete,
  });

  final AppLocalizations l10n;
  final bool downloadsEnabled;
  final ValueChanged<String> onCheck;
  final VoidCallback onManageLoader;
  final VoidCallback onSettings;
  final VoidCallback onInstallGame;
  final VoidCallback onManageMods;
  final VoidCallback onDuplicate;
  final VoidCallback onViewLogs;
  final VoidCallback onOpenFolder;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return PopupMenuButton<String>(
      tooltip: l10n.instanceManage,
      icon: Icon(AppIcons.more, color: tokens.textSecondary),
      onSelected: (value) {
        if (value == 'delete') onDelete();
        if (value == 'manageLoader') onManageLoader();
        if (value == 'settings') onSettings();
        if (value == 'installGame') onInstallGame();
        if (value == 'manageMods') onManageMods();
        if (value == 'duplicate') onDuplicate();
        if (value == 'viewLogs') onViewLogs();
        if (value == 'openFolder') onOpenFolder();
        if (value == 'checkQuick') onCheck('quick');
        if (value == 'checkFull') onCheck('full');
      },
      itemBuilder: (context) => [
        if (downloadsEnabled) ...[
          PopupMenuItem(
            value: 'manageLoader',
            child: Row(
              children: [
                const Icon(AppIcons.repair, size: 20),
                const SizedBox(width: AppSpacing.xs),
                Flexible(
                  child: Text(
                    l10n.instanceManageLoader,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            ),
          ),
          PopupMenuItem(
            value: 'installGame',
            child: Row(
              children: [
                const Icon(AppIcons.download, size: 20),
                const SizedBox(width: AppSpacing.xs),
                Flexible(
                  child: Text(
                    l10n.instanceInstallGame,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            ),
          ),
        ],
        PopupMenuItem(
          value: 'settings',
          child: Row(
            children: [
              const Icon(AppIcons.settings, size: 20),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Text(
                  l10n.instanceSettingsTitle,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
        ),
        PopupMenuItem(
          value: 'duplicate',
          child: Row(
            children: [
              const Icon(AppIcons.copy, size: 20),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Text(
                  l10n.instanceDuplicate,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
        ),
        PopupMenuItem(
          value: 'manageMods',
          child: Row(
            children: [
              const Icon(AppIcons.mod, size: 20),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Text(
                  l10n.instanceManageMods,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
        ),
        PopupMenuItem(
          value: 'viewLogs',
          child: Row(
            children: [
              const Icon(AppIcons.news, size: 20),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Text(
                  l10n.instanceViewLogs,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
        ),
        PopupMenuItem(
          value: 'openFolder',
          child: Row(
            children: [
              const Icon(AppIcons.folder, size: 20),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Text(
                  l10n.instanceOpenFolder,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
        ),
        PopupMenuItem(
          value: 'checkQuick',
          child: Row(
            children: [
              const Icon(AppIcons.quick, size: 20),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Text(
                  l10n.instanceCheckQuick,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
        ),
        PopupMenuItem(
          value: 'checkFull',
          child: Row(
            children: [
              const Icon(AppIcons.checklist, size: 20),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Text(
                  l10n.instanceCheckFull,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
        ),
        PopupMenuItem(
          value: 'delete',
          child: Row(
            children: [
              Icon(AppIcons.delete, color: tokens.danger),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Text(
                  l10n.instanceDelete,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
        ),
      ],
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
                    Row(
                      children: [
                        Flexible(
                          child: Text(
                            _instanceMeta(instance, l10n),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: textTheme.bodySmall?.copyWith(
                              color: instance.missing
                                  ? tokens.danger
                                  : tokens.textTertiary,
                            ),
                          ),
                        ),
                        if (_hasOverrides(instance)) ...[
                          const SizedBox(width: AppSpacing.xxs),
                          _OverridesBadge(l10n: l10n, overrides: instance.overrides!),
                        ],
                      ],
                    ),
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
                  _lastPlayedLabel(context, l10n, instance.lastPlayed),
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

/// First confirmation before deleting an instance. Pops `true` when the user
/// also wants the local files removed, `false` for registry-only deletion and
/// `null` on cancel.
class _InstanceDeleteDialog extends StatefulWidget {
  const _InstanceDeleteDialog({required this.l10n, required this.instance});

  final AppLocalizations l10n;
  final GameInstance instance;

  @override
  State<_InstanceDeleteDialog> createState() => _InstanceDeleteDialogState();
}

class _InstanceDeleteDialogState extends State<_InstanceDeleteDialog> {
  // Off by default: deleting files is the irreversible part of this action.
  bool _deleteFiles = false;

  bool get _canDeleteFiles =>
      !widget.instance.missing && widget.instance.rootPath.isNotEmpty;

  @override
  Widget build(BuildContext context) {
    final l10n = widget.l10n;
    final tokens = context.tokens;
    final instance = widget.instance;

    return AlertDialog(
      title: Text(l10n.instanceDeleteConfirm(instance.name)),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(l10n.instanceDeleteBody),
            const SizedBox(height: AppSpacing.sm),
            CheckboxListTile(
              value: _deleteFiles,
              onChanged: _canDeleteFiles
                  ? (value) => setState(() => _deleteFiles = value ?? false)
                  : null,
              contentPadding: EdgeInsets.zero,
              controlAffinity: ListTileControlAffinity.leading,
              dense: true,
              title: Text(l10n.instanceDeleteFiles),
              subtitle: Text(l10n.instanceDeleteFilesHint),
            ),
            if (instance.rootPath.isNotEmpty)
              Text(
                instance.rootPath,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
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
          style: FilledButton.styleFrom(
            backgroundColor: tokens.danger,
            foregroundColor: tokens.onAccent,
          ),
          onPressed: () => Navigator.of(context).pop(_deleteFiles),
          child: Text(l10n.commonDelete),
        ),
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

String _lastPlayedLabel(
  BuildContext context,
  AppLocalizations l10n,
  DateTime? lastPlayed,
) {
  if (lastPlayed == null) return l10n.instanceLastPlayedNever;

  final now = DateTime.now();
  final today = DateTime(now.year, now.month, now.day);
  final date = DateTime(lastPlayed.year, lastPlayed.month, lastPlayed.day);
  if (!date.isBefore(today)) return l10n.lastPlayedToday;
  if (!date.isBefore(today.subtract(const Duration(days: 1)))) {
    return l10n.lastPlayedYesterday;
  }

  return MaterialLocalizations.of(context).formatShortDate(lastPlayed);
}
