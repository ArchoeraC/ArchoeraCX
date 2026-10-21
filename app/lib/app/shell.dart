import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/account.dart';
import '../models/game_instance.dart';
import '../stores/accounts.dart';
import '../services/launch/launch_action.dart';
import '../stores/instances.dart';
import '../theme/tokens.dart';
import '../widgets/layout/app_background.dart';

class AppShell extends ConsumerWidget {
  const AppShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
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
    final selectedInstance = ref.watch(selectedInstanceProvider);
    final account = ref.watch(currentAccountProvider);

    return AppBackground(
      child: Scaffold(
        backgroundColor: Colors.transparent,
        body: Column(
          children: [
            _TopBar(l10n: l10n),
            Expanded(
              child: Align(
                alignment: Alignment.topCenter,
                child: ConstrainedBox(
                  constraints: const BoxConstraints(
                    maxWidth: AppLayout.contentMaxWidth,
                  ),
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(
                      AppSpacing.xl,
                      AppSpacing.lg,
                      AppSpacing.xl,
                      AppSpacing.xs,
                    ),
                    child: _PageTransition(
                      index: index,
                      child: navigationShell,
                    ),
                  ),
                ),
              ),
            ),
            _CommandBar(
              destinations: destinations,
              currentIndex: index,
              account: account,
              selectedInstance: selectedInstance,
              onSelected: (i) => navigationShell.goBranch(
                i,
                initialLocation: i == navigationShell.currentIndex,
              ),
              onAccount: () => context.go('/accounts'),
              onChooseInstance: () => context.go('/instances'),
              onLaunch: () {
                if (selectedInstance == null) {
                  context.go('/instances');
                  return;
                }
                ref
                    .read(selectedInstanceIdProvider.notifier)
                    .select(selectedInstance.id);
                launchInstance(context, ref, selectedInstance);
              },
            ),
          ],
        ),
      ),
    );
  }
}

class AppDestination {
  const AppDestination({
    required this.path,
    required this.label,
    required this.icon,
    required this.selectedIcon,
  });

  final String path;
  final String label;
  final IconData icon;
  final IconData selectedIcon;
}

class _TopBar extends StatelessWidget {
  const _TopBar({required this.l10n});

  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Container(
      height: AppLayout.titleBarHeight,
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xl),
      decoration: BoxDecoration(
        color: tokens.surface1.withValues(alpha: 0.62),
        border: Border(bottom: BorderSide(color: tokens.border)),
      ),
      child: Row(
        children: [
          Container(
            width: 30,
            height: 30,
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topLeft,
                end: Alignment.bottomRight,
                colors: [tokens.accent, tokens.accentPressed],
              ),
              borderRadius: BorderRadius.circular(tokens.radiusS),
            ),
            child: Icon(
              Icons.grid_view_rounded,
              size: 17,
              color: tokens.onAccent,
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          Text(
            l10n.appName,
            style: textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w500),
          ),
          const Spacer(),
        ],
      ),
    );
  }
}

class _PageTransition extends StatefulWidget {
  const _PageTransition({required this.index, required this.child});

  final int index;
  final Widget child;

  @override
  State<_PageTransition> createState() => _PageTransitionState();
}

class _PageTransitionState extends State<_PageTransition>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 220),
    value: 1,
  );
  late final Animation<double> _reveal = CurvedAnimation(
    parent: _controller,
    curve: Curves.easeOutCubic,
  );

  @override
  void didUpdateWidget(covariant _PageTransition oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.index != widget.index) {
      _controller.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _reveal,
      builder: (context, child) =>
          ClipRect(clipper: _BottomRevealClipper(_reveal.value), child: child),
      child: widget.child,
    );
  }
}

class _BottomRevealClipper extends CustomClipper<Rect> {
  const _BottomRevealClipper(this.progress);

  final double progress;

  @override
  Rect getClip(Size size) {
    final visible = size.height * progress.clamp(0.0, 1.0);
    return Rect.fromLTWH(0, size.height - visible, size.width, visible);
  }

  @override
  bool shouldReclip(covariant _BottomRevealClipper oldClipper) =>
      oldClipper.progress != progress;
}

/// Full-width footer command bar.
///
/// Navigation lives on the left, account and launch actions on the right. This
/// keeps the middle of the window free for content and avoids a floating dock
/// competing with page cards.
class _CommandBar extends StatelessWidget {
  const _CommandBar({
    required this.destinations,
    required this.currentIndex,
    required this.account,
    required this.selectedInstance,
    required this.onSelected,
    required this.onAccount,
    required this.onChooseInstance,
    required this.onLaunch,
  });

  final List<AppDestination> destinations;
  final int currentIndex;
  final Account? account;
  final GameInstance? selectedInstance;
  final ValueChanged<int> onSelected;
  final VoidCallback onAccount;
  final VoidCallback onChooseInstance;
  final VoidCallback onLaunch;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final l10n = AppLocalizations.of(context);

    return SafeArea(
      top: false,
      child: Container(
        height: AppLayout.bottomBarHeight,
        padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xl),
        decoration: BoxDecoration(
          color: tokens.surface1.withValues(alpha: 0.86),
          border: Border(top: BorderSide(color: tokens.border)),
        ),
        child: LayoutBuilder(
          builder: (context, constraints) {
            final compactNav = constraints.maxWidth < 620;
            final compactAccount = constraints.maxWidth < 900;
            final compactLaunch = constraints.maxWidth < 680;

            return Material(
              color: Colors.transparent,
              child: Row(
                children: [
                  Expanded(
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: Row(
                        children: [
                          for (var i = 0; i < destinations.length; i++)
                            Padding(
                              padding: const EdgeInsets.only(
                                right: AppSpacing.xxs,
                              ),
                              child: _NavItem(
                                destination: destinations[i],
                                selected: i == currentIndex,
                                compact: compactNav,
                                onTap: () => onSelected(i),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(width: AppSpacing.md),
                  Container(width: 1, height: 30, color: tokens.border),
                  const SizedBox(width: AppSpacing.md),
                  _AccountButton(
                    account: account,
                    compact: compactAccount,
                    onTap: onAccount,
                  ),
                  if (currentIndex != 0) ...[
                    const SizedBox(width: AppSpacing.sm),
                    _LaunchButton(
                      compact: compactLaunch,
                      hasInstance: selectedInstance != null,
                      onPressed: selectedInstance == null
                          ? onChooseInstance
                          : onLaunch,
                      label: selectedInstance == null
                          ? l10n.selectInstance
                          : l10n.launch,
                      tooltip: selectedInstance == null
                          ? l10n.selectInstance
                          : l10n.launch,
                    ),
                  ],
                ],
              ),
            );
          },
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  const _NavItem({
    required this.destination,
    required this.selected,
    required this.compact,
    required this.onTap,
  });

  final AppDestination destination;
  final bool selected;
  final bool compact;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final foreground = selected ? tokens.accent : tokens.textSecondary;

    return Tooltip(
      message: destination.label,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Container(
          height: 40,
          padding: EdgeInsets.symmetric(
            horizontal: compact ? AppSpacing.sm : AppSpacing.md,
          ),
          decoration: BoxDecoration(
            border: Border(
              bottom: BorderSide(
                color: selected ? tokens.accent : Colors.transparent,
                width: 2,
              ),
            ),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                selected ? destination.selectedIcon : destination.icon,
                size: 18,
                color: foreground,
              ),
              if (!compact) ...[
                const SizedBox(width: AppSpacing.xs),
                Text(
                  destination.label,
                  maxLines: 1,
                  style: Theme.of(context).textTheme.labelLarge?.copyWith(
                    color: foreground,
                    fontWeight: selected ? FontWeight.w500 : FontWeight.w500,
                    letterSpacing: 0,
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _AccountButton extends StatelessWidget {
  const _AccountButton({
    required this.account,
    required this.compact,
    required this.onTap,
  });

  final Account? account;
  final bool compact;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final l10n = AppLocalizations.of(context);
    final current = account;
    final name = current?.displayName ?? l10n.addAccount;
    final detail = current?.kind.labelFor(l10n) ?? l10n.accountNoAccounts;
    final initial = current == null ? '+' : name.characters.first.toUpperCase();

    return Tooltip(
      message: name,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.xs,
            vertical: AppSpacing.xxs,
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              CircleAvatar(
                radius: 15,
                backgroundColor: tokens.accentSoft,
                child: Text(
                  initial,
                  style: TextStyle(
                    color: tokens.accent,
                    fontSize: 12,
                    fontWeight: FontWeight.w500,
                  ),
                ),
              ),
              if (!compact) ...[
                const SizedBox(width: AppSpacing.xs),
                ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 150),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.labelMedium,
                      ),
                      Text(
                        detail,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.labelSmall?.copyWith(
                          color: tokens.textTertiary,
                          letterSpacing: 0,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _LaunchButton extends StatelessWidget {
  const _LaunchButton({
    required this.compact,
    required this.hasInstance,
    required this.onPressed,
    required this.label,
    required this.tooltip,
  });

  final bool compact;
  final bool hasInstance;
  final VoidCallback onPressed;
  final String label;
  final String tooltip;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Tooltip(
      message: tooltip,
      child: compact
          ? IconButton.filled(
              onPressed: onPressed,
              style: IconButton.styleFrom(
                backgroundColor: tokens.accent,
                foregroundColor: tokens.onAccent,
                minimumSize: const Size(38, 38),
              ),
              icon: Icon(
                hasInstance
                    ? Icons.play_arrow_rounded
                    : Icons.grid_view_rounded,
                size: 20,
              ),
            )
          : FilledButton.icon(
              onPressed: onPressed,
              style: FilledButton.styleFrom(
                minimumSize: const Size(0, 38),
                padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
              ),
              icon: Icon(
                hasInstance
                    ? Icons.play_arrow_rounded
                    : Icons.grid_view_rounded,
                size: 19,
              ),
              label: Text(label),
            ),
    );
  }
}
