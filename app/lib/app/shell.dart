import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/account.dart';
import '../models/game_instance.dart';
import '../stores/accounts.dart';
import '../services/core/downloads/build_policy.dart';
import '../services/core/launch/launch_action.dart';
import '../stores/appearance.dart';
import '../stores/instances.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../utils/app_info.dart';
import '../widgets/accounts/account_avatar.dart';
import '../widgets/common/task_center_button.dart';
import '../widgets/layout/app_background.dart';

part 'shell/page_transition.dart';
part 'shell/command_bar.dart';
part 'shell/dev_banner.dart';

class AppShell extends ConsumerStatefulWidget {
  const AppShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  ConsumerState<AppShell> createState() => _AppShellState();
}

class _AppShellState extends ConsumerState<AppShell> {
  final GlobalKey _pageKey = GlobalKey();
  ui.Image? _coverImage;
  _PageTransitionMode _mode = _PageTransitionMode.cover;

  @override
  void dispose() {
    _coverImage?.dispose();
    super.dispose();
  }

  Future<ui.Image?> _capturePage() async {
    final boundary = _pageKey.currentContext?.findRenderObject();
    if (boundary is! RenderRepaintBoundary) return null;
    try {
      return await boundary.toImage(
        pixelRatio: MediaQuery.devicePixelRatioOf(context),
      );
    } catch (_) {
      return null;
    }
  }

  Future<void> _selectBranch(int index) async {
    final navigationShell = widget.navigationShell;
    final current = navigationShell.currentIndex;
    if (index == current) {
      navigationShell.goBranch(index, initialLocation: true);
      return;
    }

    // Every branch switch gets a snapshot underlay so the new page can cover
    // the old one. Returning to the home page reverses the motion: the old
    // page retracts downward instead of a new page covering it.
    final image = await _capturePage();
    if (!mounted) {
      image?.dispose();
      return;
    }
    final mode = current == 0
        ? _PageTransitionMode.cover
        : index == 0
        ? _PageTransitionMode.retract
        : _PageTransitionMode.lift;
    setState(() {
      _coverImage = image;
      _mode = mode;
    });

    navigationShell.goBranch(index);
  }

  void _finishCover(ui.Image image) {
    if (identical(_coverImage, image)) {
      setState(() => _coverImage = null);
      // RawImage may still be in the tree for this frame.
      WidgetsBinding.instance.addPostFrameCallback((_) => image.dispose());
    } else {
      image.dispose();
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final destinations = <AppDestination>[
      AppDestination(
        path: '/',
        branchIndex: 0,
        label: l10n.navDashboard,
        icon: AppIcons.dashboard,
        selectedIcon: AppIcons.dashboard,
      ),
      AppDestination(
        path: '/instances',
        branchIndex: 1,
        label: l10n.navInstances,
        icon: AppIcons.instances,
        selectedIcon: AppIcons.instances,
      ),
      if (ref.watch(downloadEnabledProvider))
        AppDestination(
          path: '/download',
          branchIndex: 2,
          label: l10n.navDownload,
          icon: AppIcons.download,
          selectedIcon: AppIcons.download,
        ),
      AppDestination(
        path: '/accounts',
        branchIndex: 3,
        label: l10n.navAccounts,
        icon: AppIcons.accounts,
        selectedIcon: AppIcons.accounts,
      ),
      AppDestination(
        path: '/wiki',
        branchIndex: 4,
        label: l10n.navWiki,
        icon: AppIcons.wiki,
        selectedIcon: AppIcons.wiki,
      ),
      AppDestination(
        path: '/settings',
        branchIndex: 5,
        label: l10n.navSettings,
        icon: AppIcons.settings,
        selectedIcon: AppIcons.settings,
      ),
    ];

    final navigationShell = widget.navigationShell;
    final index = navigationShell.currentIndex;
    final selectedInstance = ref.watch(selectedInstanceProvider);
    final account = ref.watch(currentAccountProvider);
    final contentMaxWidth = AppLayout.contentMaxWidthFor(
      MediaQuery.sizeOf(context).width,
    );

    return AppBackground(
      child: Scaffold(
        backgroundColor: Colors.transparent,
        body: Column(
          children: [
            _TopBar(l10n: l10n, contentMaxWidth: contentMaxWidth),
            if (AppInfo.isDevelopmentBuild)
              _DevelopmentBanner(l10n: l10n, contentMaxWidth: contentMaxWidth),
            Expanded(
              child: Align(
                alignment: Alignment.topCenter,
                child: ConstrainedBox(
                  constraints: BoxConstraints(maxWidth: contentMaxWidth),
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(
                      AppSpacing.xl,
                      AppSpacing.lg,
                      AppSpacing.xl,
                      AppSpacing.xs,
                    ),
                    child: RepaintBoundary(
                      key: _pageKey,
                      child: _PageTransition(
                        index: index,
                        coverImage: _coverImage,
                        mode: _mode,
                        onCoverFinished: _finishCover,
                        child: navigationShell,
                      ),
                    ),
                  ),
                ),
              ),
            ),
            _CommandBar(
              contentMaxWidth: contentMaxWidth,
              destinations: destinations,
              currentIndex: index,
              account: account,
              selectedInstance: selectedInstance,
              onSelected: _selectBranch,
              onAccount: () => _selectBranch(3),
              onChooseInstance: () => _selectBranch(1),
              onLaunch: () {
                if (selectedInstance == null) {
                  _selectBranch(1);
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

class _TopBar extends StatelessWidget {
  const _TopBar({required this.l10n, required this.contentMaxWidth});

  final AppLocalizations l10n;
  final double contentMaxWidth;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Container(
      height: AppLayout.titleBarHeight,
      decoration: BoxDecoration(
        color: tokens.surface1.withValues(alpha: 0.62),
        border: Border(bottom: BorderSide(color: tokens.border)),
      ),
      child: Center(
        child: ConstrainedBox(
          constraints: BoxConstraints(maxWidth: contentMaxWidth),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xl),
            child: Row(
              children: [
                ClipRRect(
                  borderRadius: BorderRadius.circular(tokens.radiusS),
                  child: Image.asset(
                    'assets/branding/archoera_icon_256.png',
                    width: 30,
                    height: 30,
                    fit: BoxFit.cover,
                    filterQuality: FilterQuality.medium,
                  ),
                ),
                const SizedBox(width: AppSpacing.xs),
                Text(
                  l10n.appName,
                  // Brand text follows the theme accent so the shell picks up
                  // custom/system accent colors instead of a fixed foreground.
                  style: textTheme.titleSmall?.copyWith(
                    color: tokens.accent,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const Spacer(),
                const TaskCenterButton(),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
