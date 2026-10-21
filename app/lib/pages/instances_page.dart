import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../l10n/generated/app_localizations.dart';
import '../models/game_instance.dart';
import '../stores/instances.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_card.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
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

    return SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AppPageHeader(
            title: l10n.instancesTitle,
            subtitle: l10n.instancesSubtitle,
            actions: [
              OutlinedButton.icon(
                onPressed: () => _notify(context, l10n.commonCoreNotConnected),
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
          const SizedBox(height: AppSpacing.lg),
          AppCard(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: TextField(
                        onChanged: (value) => setState(() => _query = value),
                        decoration: InputDecoration(
                          hintText: l10n.searchInstances,
                          prefixIcon: const Icon(
                            Icons.search_rounded,
                            size: 20,
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(width: AppSpacing.sm),
                    _FilterChip(label: l10n.filterAll, selected: true),
                  ],
                ),
                const SizedBox(height: AppSpacing.md),
                if (instancesAsync.isLoading && instances.isEmpty)
                  const Padding(
                    padding: EdgeInsets.all(AppSpacing.xl),
                    child: Center(child: CircularProgressIndicator()),
                  )
                else if (instancesAsync.hasError && instances.isEmpty)
                  Padding(
                    padding: const EdgeInsets.all(AppSpacing.lg),
                    child: Text(
                      '${instancesAsync.error}',
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.danger),
                    ),
                  )
                else if (instances.isEmpty)
                  AppEmptyState(
                    icon: Icons.grid_view_rounded,
                    title: l10n.instanceEmptyTitle,
                    message: l10n.instanceEmptyMessage,
                    primaryAction: FilledButton.icon(
                      onPressed: () => showCreateInstanceDialog(context),
                      icon: const Icon(Icons.add_rounded, size: 18),
                      label: Text(l10n.createInstance),
                    ),
                  )
                else if (filtered.isEmpty)
                  Padding(
                    padding: const EdgeInsets.all(AppSpacing.lg),
                    child: Center(
                      child: Text(
                        l10n.instanceNoMatches,
                        style: Theme.of(context).textTheme.bodyMedium
                            ?.copyWith(color: tokens.textSecondary),
                      ),
                    ),
                  )
                else
                  Column(
                    children: [
                      for (final instance in filtered)
                        Padding(
                          padding: const EdgeInsets.only(bottom: AppSpacing.xs),
                          child: _InstanceTile(
                            instance: instance,
                            selected: instance.id == selectedId,
                            onSelect: () => ref
                                .read(selectedInstanceIdProvider.notifier)
                                .select(instance.id),
                            onLaunch: () => _launch(instance),
                            onDelete: () => _delete(instance, l10n),
                          ),
                        ),
                    ],
                  ),
              ],
            ),
          ),
          const SizedBox(height: AppSpacing.lg),
          Text(
            l10n.commonMockData,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.textTertiary),
          ),
        ],
      ),
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

class _FilterChip extends StatelessWidget {
  const _FilterChip({required this.label, this.selected = false});

  final String label;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Container(
      height: 40,
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm),
      decoration: BoxDecoration(
        color: selected ? tokens.accentSoft : tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusM),
        border: Border.all(color: selected ? tokens.accent : tokens.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            Icons.filter_list_rounded,
            size: 16,
            color: selected ? tokens.accent : tokens.textSecondary,
          ),
          const SizedBox(width: AppSpacing.xxs),
          Text(
            label,
            style: Theme.of(context).textTheme.labelMedium?.copyWith(
              color: selected ? tokens.accent : tokens.textSecondary,
            ),
          ),
        ],
      ),
    );
  }
}

class _InstanceTile extends StatelessWidget {
  const _InstanceTile({
    required this.instance,
    required this.selected,
    required this.onSelect,
    required this.onLaunch,
    required this.onDelete,
  });

  final GameInstance instance;
  final bool selected;
  final VoidCallback onSelect;
  final VoidCallback onLaunch;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final color = _colorFor(tokens);

    return AppCard(
      compact: true,
      selected: selected,
      onTap: onSelect,
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.md,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        children: [
          Container(
            width: 44,
            height: 44,
            decoration: BoxDecoration(
              color: color.withValues(alpha: 0.14),
              borderRadius: BorderRadius.circular(tokens.radiusM),
            ),
            child: Icon(_iconFor(instance.loader), color: color, size: 22),
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Flexible(
                      child: Text(
                        instance.name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.titleSmall,
                      ),
                    ),
                    const SizedBox(width: AppSpacing.xs),
                    _Tag(label: instance.loader.label),
                  ],
                ),
                const SizedBox(height: 2),
                Text(
                  '${instance.loaderLabel} · '
                  '${l10n.instanceMods(instance.modCount)}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodySmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          Text(
            _lastPlayedLabel(context, l10n, instance.lastPlayed),
            style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
          ),
          const SizedBox(width: AppSpacing.xs),
          IconButton(
            onPressed: onLaunch,
            tooltip: l10n.launch,
            icon: const Icon(Icons.play_arrow_rounded),
            color: tokens.accent,
          ),
          PopupMenuButton<String>(
            tooltip: l10n.settingsGeneral,
            icon: Icon(Icons.more_horiz_rounded, color: tokens.textSecondary),
            onSelected: (value) {
              if (value == 'delete') onDelete();
            },
            itemBuilder: (context) => [
              PopupMenuItem(
                value: 'delete',
                child: Row(
                  children: [
                    Icon(Icons.delete_outline_rounded, color: tokens.danger),
                    const SizedBox(width: AppSpacing.xs),
                    Text(l10n.instanceDelete),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  IconData _iconFor(GameLoader loader) => switch (loader) {
    GameLoader.vanilla => Icons.landscape_rounded,
    GameLoader.fabric => Icons.auto_awesome_rounded,
    GameLoader.quilt => Icons.layers_rounded,
    GameLoader.forge => Icons.handyman_rounded,
    GameLoader.neoforge => Icons.extension_rounded,
  };

  Color _colorFor(AppTokens tokens) => switch (instance.loader) {
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
}

class _Tag extends StatelessWidget {
  const _Tag({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 3),
      decoration: BoxDecoration(
        color: tokens.surface3,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall
            ?.copyWith(color: tokens.textSecondary, letterSpacing: 0),
      ),
    );
  }
}
