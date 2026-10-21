import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../models/account.dart';
import '../models/game_instance.dart';
import '../stores/accounts.dart';
import '../stores/instances.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_section.dart';
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
    final accountsAsync = ref.watch(accountsProvider);
    final accounts = accountsAsync.value ?? const <Account>[];
    final currentAccount = ref.watch(currentAccountProvider);

    return SingleChildScrollView(
      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppPageHeader(
            title: l10n.dashboardTitle,
            subtitle: l10n.dashboardSubtitle,
          ),
          const SizedBox(height: AppSpacing.xs),
          _LaunchHero(
            l10n: l10n,
            instance: selected,
            onLaunch: () {
              if (selected != null) {
                ref
                    .read(selectedInstanceIdProvider.notifier)
                    .select(selected.id);
              }
              _notify(context, l10n.instanceLaunchNotReady);
            },
            onBrowse: () => context.go('/instances'),
            onCreate: () => showCreateInstanceDialog(context),
          ),
          _OverviewSection(
            l10n: l10n,
            instanceCount: instances.length,
            accountCount: accounts.length,
            currentAccount: currentAccount,
            selected: selected,
            onAccount: () => context.go('/accounts'),
            onCreate: () => showCreateInstanceDialog(context),
            onImport: () => _notify(context, l10n.commonCoreNotConnected),
            onDownload: () => context.go('/download'),
          ),
          _RecentInstancesSection(
            l10n: l10n,
            instances: instances,
            selectedId: selected?.id,
            loading: instancesAsync.isLoading && instances.isEmpty,
            onBrowse: () => context.go('/instances'),
            onSelect: (instance) {
              ref.read(selectedInstanceIdProvider.notifier).select(instance.id);
              context.go('/instances');
            },
          ),
          const SizedBox(height: AppSpacing.lg),
          Text(
            l10n.dashboardEnvironment,
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: AppSpacing.xs),
          LayoutBuilder(
            builder: (context, constraints) {
              const coreCard = CoreStatusCard();
              const javaCard = JavaStatusCard();

              if (constraints.maxWidth >= 860) {
                return const IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Expanded(child: coreCard),
                      SizedBox(width: AppSpacing.md),
                      Expanded(child: javaCard),
                    ],
                  ),
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
          Text(
            l10n.homeFootnote,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.textTertiary),
          ),
        ],
      ),
    );
  }

  void _notify(BuildContext context, String message) {
    final tokens = context.tokens;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          behavior: SnackBarBehavior.floating,
          backgroundColor: tokens.textPrimary,
          content: Text(message, style: TextStyle(color: tokens.bg)),
          duration: const Duration(seconds: 2),
        ),
      );
  }
}

class _LaunchHero extends StatelessWidget {
  const _LaunchHero({
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
    final current = instance;
    final hasInstance = current != null;

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
      child: LayoutBuilder(
        builder: (context, constraints) {
          final showArt = constraints.maxWidth >= 760;
          return Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Row(
                      children: [
                        Icon(
                          Icons.bolt_rounded,
                          size: 17,
                          color: tokens.accent,
                        ),
                        const SizedBox(width: AppSpacing.xxs),
                        Text(
                          l10n.quickLaunch,
                          style: textTheme.labelLarge?.copyWith(
                            color: tokens.accent,
                            fontWeight: FontWeight.w500,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: AppSpacing.sm),
                    Text(
                      current?.name ?? l10n.instanceEmptyTitle,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.displaySmall?.copyWith(
                        fontSize: showArt ? 42 : 32,
                        height: 1.12,
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                    const SizedBox(height: AppSpacing.xs),
                    Text(
                      hasInstance
                          ? l10n.quickLaunchSubtitle
                          : l10n.instanceEmptyMessage,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.bodyMedium?.copyWith(
                        color: tokens.textSecondary,
                      ),
                    ),
                    if (current != null) ...[
                      const SizedBox(height: AppSpacing.sm),
                      _HeroMeta(
                        items: [
                          current.loaderLabel,
                          current.gameVersion,
                          l10n.instanceMods(current.modCount),
                          _lastPlayedLabel(context, l10n, current.lastPlayed),
                        ],
                      ),
                    ],
                    const SizedBox(height: AppSpacing.lg),
                    Wrap(
                      spacing: AppSpacing.xs,
                      runSpacing: AppSpacing.xs,
                      children: [
                        FilledButton.icon(
                          onPressed: hasInstance ? onLaunch : onCreate,
                          style: FilledButton.styleFrom(
                            minimumSize: const Size(0, 44),
                            padding: const EdgeInsets.symmetric(
                              horizontal: AppSpacing.lg,
                            ),
                          ),
                          icon: Icon(
                            hasInstance
                                ? Icons.play_arrow_rounded
                                : Icons.add_rounded,
                            size: 20,
                          ),
                          label: Text(
                            hasInstance ? l10n.launch : l10n.createInstance,
                          ),
                        ),
                        OutlinedButton.icon(
                          onPressed: onBrowse,
                          style: OutlinedButton.styleFrom(
                            minimumSize: const Size(0, 44),
                          ),
                          icon: const Icon(Icons.grid_view_rounded, size: 18),
                          label: Text(l10n.viewAll),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              if (showArt) ...[
                const SizedBox(width: AppSpacing.xl),
                const SizedBox(
                  height: 180,
                  child: VerticalDivider(width: 1, thickness: 1),
                ),
                const SizedBox(width: AppSpacing.xl),
                SizedBox(width: 190, child: _HeroArt(loader: current?.loader)),
              ],
            ],
          );
        },
      ),
    );
  }
}

class _HeroArt extends StatelessWidget {
  const _HeroArt({required this.loader});

  final GameLoader? loader;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Icon(
          loader == null ? Icons.add_rounded : _loaderIcon(loader!),
          size: 88,
          color: tokens.accent,
        ),
        const SizedBox(height: AppSpacing.sm),
        Text(
          loader?.label ?? 'ArchoeraCX',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: textTheme.labelMedium?.copyWith(color: tokens.textSecondary),
        ),
      ],
    );
  }
}

class _HeroMeta extends StatelessWidget {
  const _HeroMeta({required this.items});

  final List<String> items;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final style = Theme.of(context).textTheme.bodySmall
        ?.copyWith(color: tokens.textSecondary);

    return Wrap(
      spacing: AppSpacing.xs,
      runSpacing: AppSpacing.xxs,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        for (var i = 0; i < items.length; i++) ...[
          if (i > 0)
            Text('·', style: style?.copyWith(color: tokens.textTertiary)),
          Text(items[i], style: style),
        ],
      ],
    );
  }
}

class _OverviewSection extends StatelessWidget {
  const _OverviewSection({
    required this.l10n,
    required this.instanceCount,
    required this.accountCount,
    required this.currentAccount,
    required this.selected,
    required this.onAccount,
    required this.onCreate,
    required this.onImport,
    required this.onDownload,
  });

  final AppLocalizations l10n;
  final int instanceCount;
  final int accountCount;
  final Account? currentAccount;
  final GameInstance? selected;
  final VoidCallback onAccount;
  final VoidCallback onCreate;
  final VoidCallback onImport;
  final VoidCallback onDownload;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final workspace = _WorkspaceColumn(
      l10n: l10n,
      instanceCount: instanceCount,
      accountCount: accountCount,
      selected: selected,
    );
    final account = _AccountColumn(
      l10n: l10n,
      currentAccount: currentAccount,
      onAccount: onAccount,
    );
    final actions = _QuickActionsColumn(
      l10n: l10n,
      onCreate: onCreate,
      onImport: onImport,
      onDownload: onDownload,
    );

    Widget pad(Widget child) =>
        Padding(padding: const EdgeInsets.all(AppSpacing.md), child: child);
    return AppSection(
      showBottom: true,
      padding: EdgeInsets.zero,
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth >= 1080) {
            return IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Expanded(flex: 3, child: pad(workspace)),
                  VerticalDivider(width: 1, color: tokens.border),
                  Expanded(flex: 3, child: pad(account)),
                  VerticalDivider(width: 1, color: tokens.border),
                  Expanded(flex: 2, child: pad(actions)),
                ],
              ),
            );
          }

          if (constraints.maxWidth >= 680) {
            return Column(
              children: [
                IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Expanded(child: pad(workspace)),
                      VerticalDivider(width: 1, color: tokens.border),
                      Expanded(child: pad(account)),
                    ],
                  ),
                ),
                Divider(height: 1, color: tokens.border),
                pad(actions),
              ],
            );
          }

          return Column(
            children: [
              pad(workspace),
              Divider(height: 1, color: tokens.border),
              pad(account),
              Divider(height: 1, color: tokens.border),
              pad(actions),
            ],
          );
        },
      ),
    );
  }
}

class _WorkspaceColumn extends StatelessWidget {
  const _WorkspaceColumn({
    required this.l10n,
    required this.instanceCount,
    required this.accountCount,
    required this.selected,
  });

  final AppLocalizations l10n;
  final int instanceCount;
  final int accountCount;
  final GameInstance? selected;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(l10n.dashboardWorkspace, style: textTheme.titleMedium),
        const SizedBox(height: AppSpacing.md),
        Row(
          children: [
            Expanded(
              child: _Metric(
                value: '$instanceCount',
                label: l10n.statInstances,
              ),
            ),
            Container(width: 1, height: 36, color: tokens.border),
            const SizedBox(width: AppSpacing.md),
            Expanded(
              child: _Metric(value: '$accountCount', label: l10n.statAccounts),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.md),
        Divider(height: 1, color: tokens.border),
        const SizedBox(height: AppSpacing.sm),
        _KeyValueRow(
          label: l10n.instanceLoaderLabel,
          value: selected?.loaderLabel ?? '—',
        ),
        const SizedBox(height: AppSpacing.xs),
        _KeyValueRow(
          label: l10n.instanceGameVersionLabel,
          value: selected?.gameVersion ?? '—',
        ),
      ],
    );
  }
}

class _Metric extends StatelessWidget {
  const _Metric({required this.value, required this.label});

  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          value,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: textTheme.headlineSmall?.copyWith(
            fontWeight: FontWeight.w500,
            letterSpacing: 0,
          ),
        ),
        Text(
          label,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
        ),
      ],
    );
  }
}

class _KeyValueRow extends StatelessWidget {
  const _KeyValueRow({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Row(
      children: [
        Expanded(
          child: Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: textTheme.bodySmall?.copyWith(color: tokens.textSecondary),
          ),
        ),
        const SizedBox(width: AppSpacing.sm),
        Flexible(
          child: Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            textAlign: TextAlign.end,
            style: textTheme.labelLarge,
          ),
        ),
      ],
    );
  }
}

class _AccountColumn extends StatelessWidget {
  const _AccountColumn({
    required this.l10n,
    required this.currentAccount,
    required this.onAccount,
  });

  final AppLocalizations l10n;
  final Account? currentAccount;
  final VoidCallback onAccount;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final account = currentAccount;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(l10n.currentAccount, style: textTheme.titleMedium),
        const SizedBox(height: AppSpacing.md),
        if (account == null) ...[
          Row(
            children: [
              Icon(Icons.person_rounded, size: 26, color: tokens.textTertiary),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Text(
                  l10n.accountNoAccounts,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodySmall?.copyWith(
                    color: tokens.textSecondary,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          TextButton.icon(
            onPressed: onAccount,
            icon: const Icon(Icons.add_rounded, size: 17),
            label: Text(l10n.addAccount),
          ),
        ] else ...[
          Row(
            children: [
              CircleAvatar(
                radius: 22,
                backgroundColor: tokens.accentSoft,
                child: Text(
                  account.displayName.characters.first.toUpperCase(),
                  style: textTheme.titleMedium?.copyWith(
                    color: tokens.accent,
                    fontWeight: FontWeight.w500,
                  ),
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      account.displayName,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.titleSmall,
                    ),
                    Text(
                      account.kindLabel,
                      maxLines: 1,
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
          const SizedBox(height: AppSpacing.sm),
          TextButton.icon(
            onPressed: onAccount,
            icon: const Icon(Icons.chevron_right_rounded, size: 17),
            label: Text(l10n.viewAll),
          ),
        ],
      ],
    );
  }
}

class _QuickActionsColumn extends StatelessWidget {
  const _QuickActionsColumn({
    required this.l10n,
    required this.onCreate,
    required this.onImport,
    required this.onDownload,
  });

  final AppLocalizations l10n;
  final VoidCallback onCreate;
  final VoidCallback onImport;
  final VoidCallback onDownload;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(l10n.dashboardQuickActions, style: textTheme.titleMedium),
        const SizedBox(height: AppSpacing.xs),
        _ActionRow(
          icon: Icons.add_rounded,
          label: l10n.createInstance,
          onTap: onCreate,
        ),
        _ActionRow(
          icon: Icons.file_download_outlined,
          label: l10n.importPack,
          onTap: onImport,
        ),
        _ActionRow(
          icon: Icons.download_rounded,
          label: l10n.dashboardBrowseContent,
          onTap: onDownload,
          color: tokens.success,
        ),
      ],
    );
  }
}

class _ActionRow extends StatelessWidget {
  const _ActionRow({
    required this.icon,
    required this.label,
    required this.onTap,
    this.color,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.xxs,
            vertical: AppSpacing.xs,
          ),
          child: Row(
            children: [
              Icon(icon, size: 18, color: color ?? tokens.textSecondary),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(
                  label,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
              ),
              Icon(
                Icons.chevron_right_rounded,
                size: 18,
                color: tokens.textTertiary,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _RecentInstancesSection extends StatelessWidget {
  const _RecentInstancesSection({
    required this.l10n,
    required this.instances,
    required this.selectedId,
    required this.loading,
    required this.onBrowse,
    required this.onSelect,
  });

  final AppLocalizations l10n;
  final List<GameInstance> instances;
  final String? selectedId;
  final bool loading;
  final VoidCallback onBrowse;
  final ValueChanged<GameInstance> onSelect;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final recent = instances.take(4).toList(growable: false);

    return AppSection(
      showTop: false,
      showBottom: true,
      padding: EdgeInsets.zero,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.only(
              top: AppSpacing.sm,
              bottom: AppSpacing.xxs,
            ),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.recentInstances,
                    style: textTheme.titleMedium,
                  ),
                ),
                TextButton(onPressed: onBrowse, child: Text(l10n.viewAll)),
              ],
            ),
          ),
          if (loading)
            const Padding(
              padding: EdgeInsets.all(AppSpacing.lg),
              child: Center(child: CircularProgressIndicator()),
            )
          else if (recent.isEmpty)
            Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.sm),
              child: Text(
                l10n.instanceEmptyMessage,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
            )
          else
            for (var i = 0; i < recent.length; i++) ...[
              _InstanceRow(
                l10n: l10n,
                instance: recent[i],
                selected: recent[i].id == selectedId,
                onTap: () => onSelect(recent[i]),
              ),
              if (i < recent.length - 1)
                Divider(height: 1, color: tokens.border),
            ],
        ],
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
    final color = _loaderColor(tokens, instance.loader);

    return Material(
      color: selected ? tokens.accentSoft : Colors.transparent,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.xs,
            vertical: AppSpacing.sm,
          ),
          child: Row(
            children: [
              Icon(_loaderIcon(instance.loader), size: 22, color: color),
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
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
              const SizedBox(width: AppSpacing.xs),
              Icon(
                Icons.play_circle_outline_rounded,
                size: 20,
                color: selected ? tokens.accent : tokens.textSecondary,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

IconData _loaderIcon(GameLoader loader) => switch (loader) {
  GameLoader.vanilla => Icons.landscape_rounded,
  GameLoader.fabric => Icons.auto_awesome_rounded,
  GameLoader.quilt => Icons.layers_rounded,
  GameLoader.forge => Icons.handyman_rounded,
  GameLoader.neoforge => Icons.extension_rounded,
};

Color _loaderColor(AppTokens tokens, GameLoader loader) => switch (loader) {
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
