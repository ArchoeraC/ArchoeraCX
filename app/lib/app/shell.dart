import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../utils/app_info.dart';
import '../widgets/layout/app_background.dart';
import '../theme/tokens.dart';
import '../l10n/generated/app_localizations.dart';
import '../widgets/layout/side_bar.dart';

class AppShell extends StatelessWidget {
  const AppShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final destinations = <AppDestination>[
      AppDestination(
        path: '/',
        label: l10n.navDashboard,
        icon: Icons.space_dashboard_outlined,
        selectedIcon: Icons.space_dashboard_rounded,
      ),
      AppDestination(
        path: '/instances',
        label: l10n.navInstances,
        icon: Icons.grid_view_outlined,
        selectedIcon: Icons.grid_view_rounded,
      ),
      AppDestination(
        path: '/download',
        label: l10n.navDownload,
        icon: Icons.download_outlined,
        selectedIcon: Icons.download_rounded,
      ),
      AppDestination(
        path: '/accounts',
        label: l10n.navAccounts,
        icon: Icons.person_outline_rounded,
        selectedIcon: Icons.person_rounded,
      ),
      AppDestination(
        path: '/settings',
        label: l10n.navSettings,
        icon: Icons.tune_outlined,
        selectedIcon: Icons.tune_rounded,
      ),
    ];

    final index = navigationShell.currentIndex;

    return AppBackground(
      child: Scaffold(
        backgroundColor: Colors.transparent,
        body: Column(
          children: [
            _TitleBar(
              title: destinations[index].label,
              l10n: l10n,
              onTasks: () => _notify(context, l10n.commonNoTasks),
              onNotifications: () =>
                  _notify(context, l10n.commonNoNotifications),
            ),
            Expanded(
              child: LayoutBuilder(
                builder: (context, constraints) {
                  final collapsed =
                      constraints.maxWidth < AppLayout.collapseBreakpoint;

                  return Row(
                    children: [
                      AppSidebar(
                        destinations: destinations,
                        currentIndex: index,
                        collapsed: collapsed,
                        onSelected: (i) => navigationShell.goBranch(
                          i,
                          initialLocation: i == navigationShell.currentIndex,
                        ),
                        coreStatusLabel: l10n.coreOffline,
                        coreStatusHint: l10n.coreOfflineHint,
                        version: AppInfo.version,
                      ),
                      Expanded(
                        child: Padding(
                          padding: EdgeInsets.fromLTRB(
                            collapsed ? AppSpacing.md : AppSpacing.lg,
                            AppSpacing.lg,
                            collapsed ? AppSpacing.md : AppSpacing.lg,
                            AppSpacing.md,
                          ),
                          child: Align(
                            alignment: Alignment.topCenter,
                            child: ConstrainedBox(
                              constraints: const BoxConstraints(
                                maxWidth: AppLayout.contentMaxWidth,
                              ),
                              child: navigationShell,
                            ),
                          ),
                        ),
                      ),
                    ],
                  );
                },
              ),
            ),
            _BottomBar(
              l10n: l10n,
              onLaunch: () => _notify(context, l10n.commonCoreNotConnected),
              onTasks: () => _notify(context, l10n.commonNoTasks),
            ),
          ],
        ),
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

class _TitleBar extends StatelessWidget {
  const _TitleBar({
    required this.title,
    required this.l10n,
    required this.onTasks,
    required this.onNotifications,
  });

  final String title;
  final AppLocalizations l10n;
  final VoidCallback onTasks;
  final VoidCallback onNotifications;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Container(
      height: AppLayout.titleBarHeight,
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
      decoration: BoxDecoration(
        color: tokens.surface1.withValues(alpha: 0.55),
        border: Border(bottom: BorderSide(color: tokens.border)),
      ),
      child: Row(
        children: [
          Text(
            'ArchoeraCX',
            style: textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700),
          ),
          const SizedBox(width: AppSpacing.sm),
          Container(width: 1, height: 14, color: tokens.border),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Text(
              title,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: textTheme.bodyMedium?.copyWith(
                color: tokens.textSecondary,
              ),
            ),
          ),
          _PreviewChip(label: l10n.appPreview),
          const SizedBox(width: AppSpacing.xs),
          _TitleAction(
            icon: Icons.checklist_rounded,
            tooltip: l10n.navTasks,
            onTap: onTasks,
          ),
          _TitleAction(
            icon: Icons.notifications_none_rounded,
            tooltip: l10n.navNotifications,
            onTap: onNotifications,
          ),
        ],
      ),
    );
  }
}

class _PreviewChip extends StatelessWidget {
  const _PreviewChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: 4,
      ),
      decoration: BoxDecoration(
        color: tokens.accentSoft,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall
            ?.copyWith(color: tokens.accent, letterSpacing: 0.6),
      ),
    );
  }
}

class _TitleAction extends StatelessWidget {
  const _TitleAction({
    required this.icon,
    required this.tooltip,
    required this.onTap,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Tooltip(
      message: tooltip,
      child: IconButton(
        onPressed: onTap,
        icon: Icon(icon, size: 20),
        color: tokens.textSecondary,
        visualDensity: VisualDensity.compact,
        style: IconButton.styleFrom(
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(tokens.radiusS),
          ),
        ),
      ),
    );
  }
}

class _BottomBar extends StatelessWidget {
  const _BottomBar({
    required this.l10n,
    required this.onLaunch,
    required this.onTasks,
  });

  final AppLocalizations l10n;
  final VoidCallback onLaunch;
  final VoidCallback onTasks;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Container(
      height: AppLayout.bottomBarHeight,
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
      decoration: BoxDecoration(
        color: tokens.surface1.withValues(alpha: 0.72),
        border: Border(top: BorderSide(color: tokens.border)),
      ),
      child: Row(
        children: [
          _AccountChip(l10n: l10n),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Text(
              l10n.instanceName1,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: textTheme.bodySmall?.copyWith(color: tokens.textSecondary),
            ),
          ),
          IconButton(
            onPressed: onTasks,
            tooltip: l10n.navTasks,
            icon: const Icon(Icons.checklist_rounded, size: 20),
            color: tokens.textSecondary,
          ),
          const SizedBox(width: AppSpacing.xs),
          Tooltip(
            message: l10n.commonCoreNotConnected,
            child: FilledButton.icon(
              onPressed: onLaunch,
              icon: const Icon(Icons.play_arrow_rounded, size: 20),
              label: Text(l10n.launch),
            ),
          ),
        ],
      ),
    );
  }
}

class _AccountChip extends StatelessWidget {
  const _AccountChip({required this.l10n});

  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: 6,
      ),
      decoration: BoxDecoration(
        color: tokens.surface2,
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: tokens.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          CircleAvatar(
            radius: 12,
            backgroundColor: tokens.accentSoft,
            child: Text(
              'A',
              style: TextStyle(
                color: tokens.accent,
                fontSize: 11,
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                l10n.accountOffline,
                style: Theme.of(context).textTheme.labelMedium,
              ),
              Text(
                l10n.accountLocal,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: tokens.textTertiary,
                  fontSize: 10,
                  height: 1.1,
                ),
              ),
            ],
          ),
          const SizedBox(width: AppSpacing.xxs),
          Icon(Icons.expand_more_rounded, size: 16, color: tokens.textTertiary),
        ],
      ),
    );
  }
}
