import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../models/game_instance.dart';
import '../stores/instances.dart';
import '../stores/providers.dart';
import '../stores/settings.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_section.dart';
import '../widgets/instances/create_instance_dialog.dart';

class InstancesPage extends ConsumerStatefulWidget {
  const InstancesPage({super.key});

  @override
  ConsumerState<InstancesPage> createState() => _InstancesPageState();
}

class _InstancesPageState extends ConsumerState<InstancesPage> {
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final instancesAsync = ref.watch(instancesProvider);
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
                    instance.loader.label.toLowerCase().contains(query),
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
                    onPressed: () =>
                        _notify(context, l10n.commonCoreNotConnected),
                    icon: const Icon(Icons.file_download_outlined, size: 18),
                    label: Text(l10n.importPack),
                  ),
                  FilledButton.icon(
                    onPressed: () => showCreateInstanceDialog(context),
                    icon: const Icon(Icons.add_rounded, size: 18),
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
              icon: Icons.error_outline_rounded,
              message: '${instancesAsync.error}',
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
              icon: Icons.search_off_rounded,
              message: l10n.instanceNoMatches,
              color: tokens.textSecondary,
            ),
          )
        else
          SliverToBoxAdapter(
            child: Column(
              children: [
                for (var i = 0; i < filtered.length; i++) ...[
                  _InstanceRow(
                    instance: filtered[i],
                    selected: filtered[i].id == selectedId,
                    onSelect: () => ref
                        .read(selectedInstanceIdProvider.notifier)
                        .select(filtered[i].id),
                    onLaunch: () => _launch(filtered[i]),
                    onCheck: (mode) => _check(filtered[i], mode, l10n),
                    onDelete: () => _delete(filtered[i], l10n),
                  ),
                  if (i < filtered.length - 1)
                    Divider(height: 1, color: tokens.border),
                ],
                const SizedBox(height: AppSpacing.lg),
              ],
            ),
          ),
      ],
    );
  }

  void _launch(GameInstance instance) {
    ref.read(selectedInstanceIdProvider.notifier).select(instance.id);
    _notify(context, AppLocalizations.of(context).instanceLaunchNotReady);
  }

  Future<void> _delete(GameInstance instance, AppLocalizations l10n) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(l10n.instanceDelete),
        content: Text(l10n.instanceDeleteConfirm(instance.name)),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(l10n.commonCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(l10n.commonDelete),
          ),
        ],
      ),
    );

    if (confirmed == true && mounted) {
      await ref.read(instancesProvider.notifier).delete(instance.id);
    }
  }

  Future<void> _check(
    GameInstance instance,
    String mode,
    AppLocalizations l10n,
  ) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      _notify(context, l10n.commonCoreNotConnected);
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
        builder: (context) =>
            _DependencyCheckDialog(l10n: l10n, mode: mode, envelope: envelope),
      );
    } catch (error) {
      if (mounted) _notify(context, error.toString());
    }
  }

  void _notify(BuildContext context, String message) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          behavior: SnackBarBehavior.floating,
          content: Text(message),
          duration: const Duration(seconds: 2),
        ),
      );
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
        prefixIcon: const Icon(Icons.search_rounded, size: 20),
        isDense: true,
      ),
    );
    final countLabel = Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(Icons.grid_view_rounded, size: 16, color: tokens.textTertiary),
        const SizedBox(width: AppSpacing.xxs),
        Text(
          query.isEmpty ? '$total ${l10n.statInstances}' : '$count / $total',
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
                Align(alignment: Alignment.centerRight, child: countLabel),
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
        Icon(Icons.grid_view_rounded, size: 30, color: tokens.accent),
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
              icon: const Icon(Icons.add_rounded, size: 18),
              label: Text(l10n.createInstance),
            ),
            OutlinedButton.icon(
              onPressed: () => _showUnavailable(context, l10n),
              icon: const Icon(Icons.file_download_outlined, size: 18),
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
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          behavior: SnackBarBehavior.floating,
          content: Text(l10n.commonCoreNotConnected),
          duration: const Duration(seconds: 2),
        ),
      );
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
          Icon(Icons.folder_outlined, size: 18, color: tokens.textSecondary),
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

class _DependencyCheckDialog extends StatelessWidget {
  const _DependencyCheckDialog({
    required this.l10n,
    required this.mode,
    required this.envelope,
  });

  final AppLocalizations l10n;
  final String mode;
  final Map<String, Object?> envelope;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final data = envelope['data'] is Map
        ? envelope['data'] as Map<Object?, Object?>
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
    final ok = envelope['ok'] == true && verification['ok'] == true;
    final error = envelope['error'] is Map
        ? envelope['error'] as Map<Object?, Object?>
        : null;
    final errorCode = error?['code']?.toString();
    final errorMessage = errorCode == 'MANIFEST_MISSING'
        ? l10n.instanceManifestMissing
        : error?['message']?.toString() ?? l10n.instanceCheckFailed;

    return AlertDialog(
      title: Text(l10n.instanceCheckTitle),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            mode == 'full' ? l10n.instanceCheckFull : l10n.instanceCheckQuick,
            style: Theme.of(context).textTheme.labelLarge
                ?.copyWith(color: tokens.accent),
          ),
          const SizedBox(height: AppSpacing.sm),
          if (ok)
            Text(l10n.instanceCheckOk, style: TextStyle(color: tokens.success))
          else if (error != null)
            Text(errorMessage, style: TextStyle(color: tokens.danger))
          else ...[
            Text(
              l10n.instanceCheckFailed,
              style: TextStyle(color: tokens.danger),
            ),
            if (missing > 0) Text(l10n.instanceCheckMissing(missing)),
            if (mismatched > 0) Text(l10n.instanceCheckMismatched(mismatched)),
          ],
        ],
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
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
    required this.onDelete,
  });

  final GameInstance instance;
  final bool selected;
  final VoidCallback onSelect;
  final VoidCallback onLaunch;
  final ValueChanged<String> onCheck;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final color = _colorFor(instance.loader, tokens);

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
                  Icon(_iconFor(instance.loader), size: 24, color: color),
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
                        Text(
                          _instanceMeta(instance, l10n),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: textTheme.bodySmall?.copyWith(
                            color: instance.missing
                                ? tokens.danger
                                : tokens.textTertiary,
                          ),
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
                    icon: const Icon(Icons.play_arrow_rounded, size: 18),
                    label: Text(l10n.launch),
                  ),
                  PopupMenuButton<String>(
                    tooltip: l10n.settingsGeneral,
                    icon: Icon(
                      Icons.more_horiz_rounded,
                      color: tokens.textSecondary,
                    ),
                    onSelected: (value) {
                      if (value == 'delete') onDelete();
                      if (value == 'checkQuick') onCheck('quick');
                      if (value == 'checkFull') onCheck('full');
                    },
                    itemBuilder: (context) => [
                      if (instance.loader == GameLoader.forge ||
                          instance.loader == GameLoader.neoforge) ...[
                        PopupMenuItem(
                          value: 'checkQuick',
                          child: Row(
                            children: [
                              const Icon(Icons.bolt_rounded, size: 20),
                              const SizedBox(width: AppSpacing.xs),
                              Text(l10n.instanceCheckQuick),
                            ],
                          ),
                        ),
                        PopupMenuItem(
                          value: 'checkFull',
                          child: Row(
                            children: [
                              const Icon(Icons.fact_check_outlined, size: 20),
                              const SizedBox(width: AppSpacing.xs),
                              Text(l10n.instanceCheckFull),
                            ],
                          ),
                        ),
                      ],
                      PopupMenuItem(
                        value: 'delete',
                        child: Row(
                          children: [
                            Icon(
                              Icons.delete_outline_rounded,
                              color: tokens.danger,
                            ),
                            const SizedBox(width: AppSpacing.xs),
                            Text(l10n.instanceDelete),
                          ],
                        ),
                      ),
                    ],
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

String _instanceMeta(GameInstance instance, AppLocalizations l10n) {
  final parts = <String>[
    instance.gameVersion,
    instance.loader.label,
    l10n.instanceMods(instance.modCount),
  ];
  if (instance.missing) parts.insert(0, l10n.instanceMissing);
  if (instance.custom) parts.insert(0, l10n.instanceCustom);
  return parts.join(' · ');
}

IconData _iconFor(GameLoader loader) => switch (loader) {
  GameLoader.vanilla => Icons.landscape_rounded,
  GameLoader.fabric => Icons.auto_awesome_rounded,
  GameLoader.quilt => Icons.layers_rounded,
  GameLoader.forge => Icons.handyman_rounded,
  GameLoader.neoforge => Icons.extension_rounded,
};

Color _colorFor(GameLoader loader, AppTokens tokens) => switch (loader) {
  GameLoader.vanilla => tokens.accent,
  GameLoader.fabric => tokens.success,
  GameLoader.quilt => tokens.accent,
  GameLoader.forge => tokens.warning,
  GameLoader.neoforge => tokens.warning,
};

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
