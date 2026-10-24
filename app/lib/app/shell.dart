import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/account.dart';
import '../models/game_instance.dart';
import '../render/app_shaders.dart';
import '../render/dissolve_mask.dart';
import '../stores/accounts.dart';
import '../services/core/downloads/build_policy.dart';
import '../services/core/launch/launch_action.dart';
import '../stores/appearance.dart';
import '../stores/instances.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../widgets/accounts/account_avatar.dart';
import '../widgets/common/task_center_button.dart';
import '../widgets/layout/app_background.dart';

part 'shell/page_transition.dart';
part 'shell/command_bar.dart';

class AppShell extends ConsumerStatefulWidget {
  const AppShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  ConsumerState<AppShell> createState() => _AppShellState();
}

/// Signature of the shell's animated branch switch.
typedef OpenAppBranch = void Function(int index);

/// Branch order as declared in the router's `StatefulShellRoute`.
///
/// Pages pass these values to [openAppBranch] (and the shell keeps its
/// destinations in sync) so adding a branch only moves its siblings here.
abstract final class AppBranch {
  static const int dashboard = 0;
  static const int instances = 1;
  static const int download = 2;
  static const int accounts = 3;
  static const int wiki = 4;
  static const int settings = 5;
}

/// Exposes the shell's animated branch switch to descendant pages.
///
/// Pages live under the branch navigator, so they can reach this scope with
/// [maybeOf]. Calling [open] runs the same snapshot transition as the command
/// bar; a plain `context.go` would swap the indexed stack instantly.
class AppBranchScope extends InheritedWidget {
  const AppBranchScope({
    super.key,
    required this.index,
    required this.open,
    required super.child,
  });

  /// Currently active [StatefulShellBranch] index.
  final int index;

  /// Requests a branch switch; the shell captures the outgoing page first.
  final OpenAppBranch open;

  static AppBranchScope? maybeOf(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<AppBranchScope>();

  @override
  bool updateShouldNotify(AppBranchScope oldWidget) =>
      index != oldWidget.index || open != oldWidget.open;
}

/// Switches to [index] through the shell transition when one is available.
///
/// [fallbackPath] keeps standalone pages (tests, previews) working when the
/// widget tree has no [AppShell] ancestor.
void openAppBranch(BuildContext context, int index, {String? fallbackPath}) {
  final scope = AppBranchScope.maybeOf(context);
  if (scope != null) {
    scope.open(index);
    return;
  }
  if (fallbackPath != null) context.go(fallbackPath);
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

  /// Captures the current page for the outgoing transition layer.
  ///
  /// Uses [RenderRepaintBoundary.toImageSync] so the snapshot is rasterized on
  /// the raster thread and never synchronously read back to the CPU. The old
  /// `toImage` call stalled the UI thread for a full frame on high-DPI or
  /// software-rendered machines, which was the most visible GPU cost of every
  /// branch switch.
  ui.Image? _capturePage() {
    final boundary = _pageKey.currentContext?.findRenderObject();
    if (boundary is! RenderRepaintBoundary) return null;
    // A boundary that still needs paint has no layer to snapshot yet; skipping
    // the transition is cheaper than forcing a synchronous repaint.
    if (boundary.debugNeedsPaint) return null;
    try {
      return boundary.toImageSync(
        pixelRatio: MediaQuery.devicePixelRatioOf(context),
      );
    } catch (_) {
      return null;
    }
  }

  void _selectBranch(int index) {
    final navigationShell = widget.navigationShell;
    final current = navigationShell.currentIndex;
    if (index == current) {
      navigationShell.goBranch(index, initialLocation: true);
      return;
    }

    // Every branch switch gets a snapshot underlay so the new page can cover
    // the old one. Returning to the home page reverses the motion: the old
    // page retracts downward instead of a new page covering it.
    final image = _capturePage();
    if (!mounted) {
      image?.dispose();
      return;
    }
    final mode = current == AppBranch.dashboard
        ? _PageTransitionMode.cover
        : index == AppBranch.dashboard
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
      if (mounted) setState(() => _coverImage = null);
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
        branchIndex: AppBranch.dashboard,
        label: l10n.navDashboard,
        icon: AppIcons.dashboard,
        selectedIcon: AppIcons.dashboard,
      ),
      AppDestination(
        path: '/instances',
        branchIndex: AppBranch.instances,
        label: l10n.navInstances,
        icon: AppIcons.instances,
        selectedIcon: AppIcons.instances,
      ),
      if (ref.watch(downloadEnabledProvider))
        AppDestination(
          path: '/download',
          branchIndex: AppBranch.download,
          label: l10n.navDownload,
          icon: AppIcons.download,
          selectedIcon: AppIcons.download,
        ),
      AppDestination(
        path: '/accounts',
        branchIndex: AppBranch.accounts,
        label: l10n.navAccounts,
        icon: AppIcons.accounts,
        selectedIcon: AppIcons.accounts,
      ),
      AppDestination(
        path: '/wiki',
        branchIndex: AppBranch.wiki,
        label: l10n.navWiki,
        icon: AppIcons.wiki,
        selectedIcon: AppIcons.wiki,
      ),
      AppDestination(
        path: '/settings',
        branchIndex: AppBranch.settings,
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
                        child: AppBranchScope(
                          index: index,
                          open: _selectBranch,
                          child: navigationShell,
                        ),
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
              onAccount: () => _selectBranch(AppBranch.accounts),
              onChooseInstance: () => _selectBranch(AppBranch.instances),
              onLaunch: () {
                if (selectedInstance == null) {
                  _selectBranch(AppBranch.instances);
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

/// One entry of the bottom command bar.
///
/// [branchIndex] is the `StatefulShellBranch` index, not the position in the
/// rendered row: the download branch can be hidden by policy while the indexes
/// of the remaining branches stay stable.
class AppDestination {
  const AppDestination({
    required this.path,
    required this.branchIndex,
    required this.label,
    required this.icon,
    required this.selectedIcon,
  });

  final String path;
  final int branchIndex;
  final String label;
  final IconData icon;
  final IconData selectedIcon;
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
      // The title bar is part of the page canvas, not a separate chrome
      // strip: same background as the page and no hairline under it, so the
      // brand and the task button appear to float on the content.
      decoration: BoxDecoration(color: tokens.bg),
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
