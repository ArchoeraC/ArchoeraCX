part of '../shell.dart';

/// Full-width footer command bar.
///
/// Navigation lives on the left, account and launch actions on the right. This
/// keeps the middle of the window free for content and avoids a floating dock
/// competing with page cards.
class _CommandBar extends StatelessWidget {
  const _CommandBar({
    required this.contentMaxWidth,
    required this.destinations,
    required this.currentIndex,
    required this.account,
    required this.selectedInstance,
    required this.onSelected,
    required this.onAccount,
    required this.onChooseInstance,
    required this.onLaunch,
  });

  final double contentMaxWidth;
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
    // The home page keeps the slimmer footer: it has no launch action and the
    // shorter bar lets the page canvas run further down. Other pages keep the
    // full-height bar with the account + launch cluster.
    final onHome = currentIndex == 0;

    return SafeArea(
      top: false,
      child: AnimatedContainer(
        duration: context.tokens.normal,
        curve: Curves.easeOutCubic,
        height: onHome ? AppLayout.homeBarHeight : AppLayout.bottomBarHeight,
        decoration: BoxDecoration(
          color: onHome ? tokens.bg : tokens.surface1.withValues(alpha: 0.86),
          border: Border(
            top: BorderSide(color: onHome ? Colors.transparent : tokens.border),
          ),
        ),
        child: Center(
          child: ConstrainedBox(
            constraints: BoxConstraints(maxWidth: contentMaxWidth),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xl),
              child: LayoutBuilder(
                builder: (context, constraints) {
                  final compactNav = constraints.maxWidth < 820;
                  final compactAccount = constraints.maxWidth < 900;
                  final compactLaunch = constraints.maxWidth < 680;

                  return Material(
                    color: Colors.transparent,
                    child: Row(
                      children: [
                        Expanded(
                          child: SingleChildScrollView(
                            scrollDirection: Axis.horizontal,
                            child: _NavTabs(
                              destinations: destinations,
                              currentIndex: currentIndex,
                              compact: compactNav,
                              onSelected: onSelected,
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
                        // The launch action only belongs to the non-home bar;
                        // slide + fade it so switching pages does not pop it in.
                        AnimatedSwitcher(
                          duration: tokens.normal,
                          switchInCurve: Curves.easeOutCubic,
                          switchOutCurve: Curves.easeInCubic,
                          transitionBuilder: (child, animation) =>
                              SizeTransition(
                                sizeFactor: animation,
                                axis: Axis.horizontal,
                                alignment: Alignment.centerLeft,
                                child: FadeTransition(
                                  opacity: animation,
                                  child: child,
                                ),
                              ),
                          layoutBuilder: (currentChild, previousChildren) =>
                              Stack(
                                alignment: Alignment.centerRight,
                                children: [...previousChildren, ?currentChild],
                              ),
                          child: currentIndex == 0
                              ? const SizedBox.shrink(
                                  key: ValueKey('launch-action-hidden'),
                                )
                              : Padding(
                                  key: const ValueKey('launch-action'),
                                  padding: const EdgeInsets.only(
                                    left: AppSpacing.sm,
                                  ),
                                  child: _LaunchButton(
                                    key: const ValueKey('command-bar-launch'),
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
                                        : l10n.launchWithInstance(
                                            selectedInstance!.name,
                                          ),
                                  ),
                                ),
                        ),
                      ],
                    ),
                  );
                },
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Navigation row with one accent plate that slides to the selected item.
///
/// The plate is a single widget shared by all destinations instead of a border
/// repeated on every item: the eye follows the motion from the old tab to the
/// new one, and only one extra paint is added to the bar. Items are measured
/// after layout (they change width with the language, the compact breakpoint
/// and the theme font), so the plate always matches the real hit target.
class _NavTabs extends StatefulWidget {
  const _NavTabs({
    required this.destinations,
    required this.currentIndex,
    required this.compact,
    required this.onSelected,
  });

  final List<AppDestination> destinations;
  final int currentIndex;
  final bool compact;
  final ValueChanged<int> onSelected;

  @override
  State<_NavTabs> createState() => _NavTabsState();
}

class _NavTabsState extends State<_NavTabs> {
  final GlobalKey _stackKey = GlobalKey();
  final List<GlobalKey> _itemKeys = <GlobalKey>[];

  Rect? _plate;
  bool _animate = false;

  /// Whether the next applied rect change should animate.
  ///
  /// Latched in [didUpdateWidget] and consumed by [_reposition]: the shell can
  /// rebuild again between the navigation and the first build with the new
  /// plate, and re-deriving the flag on every update would silently drop the
  /// slide.
  bool _pendingAnimate = false;

  @override
  void initState() {
    super.initState();
    _resizeKeys(widget.destinations.length);
    _schedule();
  }

  @override
  void didUpdateWidget(covariant _NavTabs oldWidget) {
    super.didUpdateWidget(oldWidget);
    _resizeKeys(widget.destinations.length);
    // Selection changes animate; pure relayouts (window resize, language
    // change) snap so the plate never lags behind a resize.
    if (oldWidget.compact != widget.compact) _pendingAnimate = false;
    if (oldWidget.currentIndex != widget.currentIndex) _pendingAnimate = true;
    _schedule();
  }

  void _resizeKeys(int length) {
    while (_itemKeys.length < length) {
      _itemKeys.add(GlobalKey());
    }
    if (_itemKeys.length > length) {
      _itemKeys.removeRange(length, _itemKeys.length);
    }
  }

  void _schedule() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _reposition();
    });
  }

  void _reposition() {
    final stack = _stackKey.currentContext?.findRenderObject();
    if (stack is! RenderBox || !stack.hasSize) return;
    final index = widget.destinations.indexWhere(
      (destination) => destination.branchIndex == widget.currentIndex,
    );
    if (index < 0) {
      if (_plate != null) setState(() => _plate = null);
      return;
    }

    // The home destination never carries the selected plate: the dashboard is
    // the canvas itself, and a highlighted "home" tab would read as a second
    // header. Other destinations still show the plate normally.
    if (widget.destinations[index].path == '/') {
      _pendingAnimate = false;
      if (_plate != null) setState(() => _plate = null);
      return;
    }

    final item = _itemKeys[index].currentContext?.findRenderObject();
    if (item is! RenderBox || !item.hasSize) return;

    final rect =
        stack.globalToLocal(item.localToGlobal(Offset.zero)) & item.size;
    if (rect == _plate) return;
    final animate = _pendingAnimate;
    _pendingAnimate = false;
    setState(() {
      _plate = rect;
      _animate = animate;
    });
  }

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final reduceMotion = MediaQuery.maybeDisableAnimationsOf(context) ?? false;
    final plate = _plate;

    return Stack(
      key: _stackKey,
      children: [
        if (plate != null)
          AnimatedPositioned(
            duration: _animate && !reduceMotion ? tokens.normal : Duration.zero,
            curve: tokens.curve,
            left: plate.left,
            top: plate.top,
            width: plate.width,
            height: plate.height,
            child: const IgnorePointer(child: _NavPlate()),
          ),
        Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (
              var index = 0;
              index < widget.destinations.length;
              index++
            ) ...[
              if (index > 0) const SizedBox(width: AppSpacing.xxs),
              KeyedSubtree(
                key: _itemKeys[index],
                child: _NavItem(
                  key: ValueKey('nav-${widget.destinations[index].path}'),
                  destination: widget.destinations[index],
                  selected:
                      widget.destinations[index].branchIndex ==
                      widget.currentIndex,
                  // The dashboard is the canvas, not a highlighted tab.
                  highlighted:
                      widget.destinations[index].branchIndex ==
                          widget.currentIndex &&
                      widget.destinations[index].path != '/',
                  compact: widget.compact,
                  onTap: () =>
                      widget.onSelected(widget.destinations[index].branchIndex),
                ),
              ),
            ],
          ],
        ),
      ],
    );
  }
}

/// The sliding selected-state plate.
///
/// Angular by design: two cut corners from the panel language instead of a
/// generic rounded pill. The accent fill is the soft token so the label keeps
/// its contrast in both themes, and the hairline is the only strong edge.
class _NavPlate extends StatelessWidget {
  const _NavPlate();

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return DecoratedBox(
      key: const ValueKey('nav-highlight'),
      decoration: ShapeDecoration(
        color: tokens.accentSoft,
        shape: BeveledRectangleBorder(
          borderRadius: BorderRadius.only(
            topRight: Radius.circular(tokens.bevelS),
            bottomLeft: Radius.circular(tokens.bevelS),
          ),
          side: BorderSide(color: tokens.accent.withValues(alpha: 0.32)),
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  const _NavItem({
    super.key,
    required this.destination,
    required this.selected,
    required this.highlighted,
    required this.compact,
    required this.onTap,
  });

  final AppDestination destination;

  /// Whether this destination is the current branch (drives semantics).
  final bool selected;

  /// Whether it also carries the accent colour and the sliding plate. The home
  /// destination stays [selected] without being [highlighted].
  final bool highlighted;

  final bool compact;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final foreground = highlighted ? tokens.accent : tokens.textSecondary;
    // The home destination is the page canvas itself: it never draws the plate
    // and must not grow any press/hover block either, otherwise the bar implies
    // a selected/hovered tab that is intentionally absent. Other items keep the
    // theme's normal ink response.
    final isHome = destination.path == '/';
    final hoverTint = isHome ? Colors.transparent : tokens.surface2;
    final pressTint = isHome ? Colors.transparent : null;
    final shape = BeveledRectangleBorder(
      borderRadius: BorderRadius.only(
        topRight: Radius.circular(tokens.bevelS),
        bottomLeft: Radius.circular(tokens.bevelS),
      ),
    );

    return Tooltip(
      message: destination.label,
      child: Semantics(
        button: true,
        selected: selected,
        child: InkWell(
          onTap: onTap,
          customBorder: shape,
          hoverColor: hoverTint,
          highlightColor: pressTint,
          splashColor: pressTint,
          child: Container(
            height: 40,
            padding: EdgeInsets.symmetric(
              horizontal: compact ? AppSpacing.sm : AppSpacing.md,
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                TweenAnimationBuilder<Color?>(
                  tween: ColorTween(end: foreground),
                  duration: tokens.fast,
                  curve: tokens.curve,
                  builder: (context, color, _) => Icon(
                    highlighted ? destination.selectedIcon : destination.icon,
                    size: 18,
                    color: color,
                  ),
                ),
                if (!compact) ...[
                  const SizedBox(width: AppSpacing.xs),
                  AnimatedDefaultTextStyle(
                    duration: tokens.fast,
                    curve: tokens.curve,
                    style:
                        Theme.of(context).textTheme.labelLarge?.copyWith(
                          color: foreground,
                          fontWeight: FontWeight.w500,
                          letterSpacing: 0,
                        ) ??
                        TextStyle(color: foreground),
                    child: Text(destination.label, maxLines: 1),
                  ),
                ],
              ],
            ),
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
              if (current != null)
                AccountAvatar(account: current, size: 30)
              else
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
    super.key,
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
                hasInstance ? AppIcons.play : AppIcons.gridView,
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
                hasInstance ? AppIcons.play : AppIcons.gridView,
                size: 19,
              ),
              label: Text(label),
            ),
    );
  }
}
