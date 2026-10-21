import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../models/game_instance.dart';
import '../stores/instances.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_card.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/core_status_card.dart';
import '../widgets/common/java_status_card.dart';
import '../widgets/instances/create_instance_dialog.dart';

class DashboardPage extends ConsumerWidget {
  const DashboardPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final instancesAsync = ref.watch(instancesProvider);
    final instances = instancesAsync.value ?? const <GameInstance>[];
    final selected = ref.watch(selectedInstanceProvider);

    return SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AppPageHeader(
            title: l10n.dashboardTitle,
            subtitle: l10n.dashboardSubtitle,
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
          LayoutBuilder(
            builder: (context, constraints) {
              final wide = constraints.maxWidth >= 860;
              final quickLaunch = _QuickLaunchCard(
                l10n: l10n,
                instance: selected,
                onLaunch: () {
                  ref
                      .read(selectedInstanceIdProvider.notifier)
                      .select(selected?.id);
                  _notify(context, l10n.instanceLaunchNotReady);
                },
                onBrowse: () => context.go('/instances'),
                onCreate: () => showCreateInstanceDialog(context),
              );
              final stats = _StatsCard(
                l10n: l10n,
                instanceCount: instances.length,
              );

              if (wide) {
                return Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(flex: 3, child: quickLaunch),
                    const SizedBox(width: AppSpacing.md),
                    Expanded(flex: 2, child: stats),
                  ],
                );
              }

              return Column(
                children: [
                  quickLaunch,
                  const SizedBox(height: AppSpacing.md),
                  stats,
                ],
              );
            },
          ),
          const SizedBox(height: AppSpacing.lg),
          LayoutBuilder(
            builder: (context, constraints) {
              const coreCard = CoreStatusCard();
              const javaCard = JavaStatusCard();

              if (constraints.maxWidth >= 860) {
                return const Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(child: coreCard),
                    SizedBox(width: AppSpacing.md),
                    Expanded(child: javaCard),
                  ],
                );
              }

              return const Column(
                children: [
                  coreCard,
                  SizedBox(height: AppSpacing.md),
                  javaCard,
                ],
              );
            },
          ),
          const SizedBox(height: AppSpacing.lg),
          AppCard(
            padding: const EdgeInsets.all(AppSpacing.md),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(
                    AppSpacing.xs,
                    AppSpacing.xs,
                    AppSpacing.xs,
                    AppSpacing.sm,
                  ),
                  child: Row(
                    children: [
                      Expanded(
                        child: Text(
                          l10n.recentInstances,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ),
                      TextButton(
                        onPressed: () => context.go('/instances'),
                        child: Text(l10n.viewAll),
                      ),
                    ],
                  ),
                ),
                if (instancesAsync.isLoading && instances.isEmpty)
                  const Padding(
                    padding: EdgeInsets.all(AppSpacing.md),
                    child: Center(child: CircularProgressIndicator()),
                  )
                else if (instances.isEmpty)
                  Padding(
                    padding: const EdgeInsets.all(AppSpacing.md),
                    child: Text(
                      l10n.instanceEmptyMessage,
                      style: Theme.of(context).textTheme.bodyMedium
                          ?.copyWith(color: tokens.textSecondary),
                    ),
                  )
                else
                  for (final instance in instances.take(3)) ...[
                    _InstanceRow(
                      l10n: l10n,
                      instance: instance,
                      selected: instance.id == selected?.id,
                      onTap: () {
                        ref
                            .read(selectedInstanceIdProvider.notifier)
                            .select(instance.id);
                        context.go('/instances');
                      },
                    ),
                    if (instance != instances.take(3).last) const Divider(),
                  ],
              ],
            ),
          ),
          const SizedBox(height: AppSpacing.lg),
          Text(
            l10n.homeFootnote,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.textTertiary),
          ),
          const SizedBox(height: AppSpacing.sm),
        ],
      ),
    );
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

class _QuickLaunchCard extends StatelessWidget {
  const _QuickLaunchCard({
    required this.l10n,
    required this.instance,
    required this.onLaunch,
    required this.onBrowse,
    required this.onCreate,
  });

  final AppLocalizations l10n;
  final GameInstance? instance;
  final VoidCallback onLaunch;
  final VoidCallback onBrowse;
  final VoidCallback onCreate;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final hasInstance = instance != null;

    return Container(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [tokens.accent, tokens.accentPressed],
        ),
        borderRadius: BorderRadius.circular(tokens.radiusL),
      ),
      padding: const EdgeInsets.all(AppSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.bolt_rounded, size: 18, color: tokens.onAccent),
              const SizedBox(width: AppSpacing.xs),
              Text(
                l10n.quickLaunch,
                style: textTheme.labelLarge?.copyWith(color: tokens.onAccent),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
          Text(
            hasInstance ? instance!.name : l10n.instanceEmptyTitle,
            style: textTheme.headlineSmall?.copyWith(color: tokens.onAccent),
          ),
          const SizedBox(height: AppSpacing.xxs),
          Text(
            hasInstance ? l10n.quickLaunchSubtitle : l10n.instanceEmptyMessage,
            style: textTheme.bodyMedium?.copyWith(
              color: tokens.onAccent.withValues(alpha: 0.78),
            ),
          ),
          if (hasInstance) ...[
            const SizedBox(height: AppSpacing.xs),
            Text(
              '${instance!.loaderLabel} · '
              '${l10n.instanceMods(instance!.modCount)}',
              style: textTheme.bodySmall?.copyWith(
                color: tokens.onAccent.withValues(alpha: 0.7),
              ),
            ),
          ],
          const SizedBox(height: AppSpacing.lg),
          Row(
            children: [
              FilledButton.icon(
                onPressed: hasInstance ? onLaunch : onCreate,
                style: FilledButton.styleFrom(
                  backgroundColor: tokens.onAccent,
                  foregroundColor: tokens.accentPressed,
                ),
                icon: Icon(
                  hasInstance ? Icons.play_arrow_rounded : Icons.add_rounded,
                  size: 20,
                ),
                label: Text(hasInstance ? l10n.launch : l10n.createInstance),
              ),
              const SizedBox(width: AppSpacing.xs),
              OutlinedButton(
                onPressed: hasInstance ? onBrowse : onCreate,
                style: OutlinedButton.styleFrom(
                  foregroundColor: tokens.onAccent,
                  side: BorderSide(
                    color: tokens.onAccent.withValues(alpha: 0.4),
                  ),
                ),
                child: Text(hasInstance ? l10n.viewAll : l10n.createInstance),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _StatsCard extends StatelessWidget {
  const _StatsCard({required this.l10n, required this.instanceCount});

  final AppLocalizations l10n;
  final int instanceCount;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            l10n.dashboardTitle,
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: AppSpacing.md),
          LayoutBuilder(
            builder: (context, constraints) {
              final tileWidth = (constraints.maxWidth - AppSpacing.sm) / 2;
              return Wrap(
                spacing: AppSpacing.sm,
                runSpacing: AppSpacing.sm,
                children: [
                  _StatTile(
                    width: tileWidth,
                    icon: Icons.grid_view_rounded,
                    value: '$instanceCount',
                    label: l10n.statInstances,
                    color: tokens.accent,
                  ),
                  _StatTile(
                    width: tileWidth,
                    icon: Icons.coffee_rounded,
                    value: '21',
                    label: l10n.statJava,
                    color: tokens.success,
                  ),
                  _StatTile(
                    width: tileWidth,
                    icon: Icons.download_rounded,
                    value: '0',
                    label: l10n.statDownloads,
                    color: tokens.warning,
                  ),
                  _StatTile(
                    width: tileWidth,
                    icon: Icons.person_rounded,
                    value: '1',
                    label: l10n.statAccounts,
                    color: tokens.accent,
                  ),
                ],
              );
            },
          ),
        ],
      ),
    );
  }
}

class _StatTile extends StatelessWidget {
  const _StatTile({
    required this.width,
    required this.icon,
    required this.value,
    required this.label,
    required this.color,
  });

  final double width;
  final IconData icon;
  final String value;
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return SizedBox(
      width: width,
      child: Container(
        padding: const EdgeInsets.all(AppSpacing.sm),
        decoration: BoxDecoration(
          color: tokens.surface2,
          borderRadius: BorderRadius.circular(tokens.radiusM),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, size: 18, color: color),
            const SizedBox(height: AppSpacing.sm),
            Text(value, style: Theme.of(context).textTheme.titleLarge),
            Text(
              label,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textTertiary),
            ),
          ],
        ),
      ),
    );
  }
}

class _InstanceRow extends StatelessWidget {
  const _InstanceRow({
    required this.l10n,
    required this.instance,
    required this.selected,
    required this.onTap,
  });

  final AppLocalizations l10n;
  final GameInstance instance;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final color = _colorFor(tokens);

    return ListTile(
      selected: selected,
      onTap: onTap,
      contentPadding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: AppSpacing.xxs,
      ),
      leading: Container(
        width: 40,
        height: 40,
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.14),
          borderRadius: BorderRadius.circular(tokens.radiusM),
        ),
        child: Icon(_iconFor(instance.loader), size: 20, color: color),
      ),
      title: Text(instance.name, style: textTheme.titleSmall),
      subtitle: Text(
        '${instance.loaderLabel} · ${l10n.instanceMods(instance.modCount)}',
      ),
      trailing: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Text(
            _lastPlayedLabel(context, l10n, instance.lastPlayed),
            style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
          ),
          const SizedBox(height: AppSpacing.xxs),
          Icon(Icons.play_circle_outline_rounded, color: tokens.textSecondary),
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
