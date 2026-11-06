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
          // The home footer is part of the page canvas: it paints the canvas
          // colour with no hairline. In solid mode that colour is the canvas
          // itself; with a wallpaper the canvas is transparent, so the nav
          // floats directly over the photo. Other branches keep the chrome
          // plate that separates the page from the controls.
          color: onHome
              ? tokens.bg
              : tokens.imageBackground
              ? tokens.surface1.withValues(alpha: 0.78)
              : tokens.surface1.withValues(alpha: 0.86),
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
                  final compactAccount = constraints.maxWidth < 900;
                  final compactLaunch = constraints.maxWidth < 680;

                  return Material(
                    color: Colors.transparent,
                    child: Row(
                      children: [
                        Expanded(
                          child: LayoutBuilder(
                            builder: (context, navConstraints) {
                              // Keep the natural, left-aligned row from the
                              // original command bar. Measure the labels only
                              // to decide when a tighter density is actually
                              // required; free space stays free instead of
                              // stretching the tabs across the window.
                              final metrics = _NavMetrics.measure(
                                context,
                                destinations,
                              );
                              final density = metrics.pick(
                                navConstraints.maxWidth,
                              );
                              return SingleChildScrollView(
                                scrollDirection: Axis.horizontal,
                                child: _NavTabs(
                                  destinations: destinations,
                                  currentIndex: currentIndex,
                                  compact: density == _NavDensity.compact,
                                  dense: density == _NavDensity.dense,
                                  onSelected: onSelected,
                                ),
                              );
                            },
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

/// Presentation density of the navigation row.
enum _NavDensity { normal, dense, compact }

/// Intrinsic widths of the three navigation densities.
///
/// Measuring the labels (including the user's text scale) rather than using a
/// fixed window breakpoint keeps short locales at full size: a wide Chinese
/// window must not shrink tabs that already fit, and an English window must
/// not clip the last one.
class _NavMetrics {
  const _NavMetrics({
    required this.normal,
    required this.dense,
    required this.compact,
  });

  final double normal;
  final double dense;
  final double compact;

  factory _NavMetrics.measure(
    BuildContext context,
    List<AppDestination> destinations,
  ) => _NavMetrics(
    normal: _rowWidth(context, destinations, _NavDensity.normal),
    dense: _rowWidth(context, destinations, _NavDensity.dense),
    compact: _rowWidth(context, destinations, _NavDensity.compact),
  );

  _NavDensity pick(double available) {
    if (normal <= available) return _NavDensity.normal;
    if (dense <= available) return _NavDensity.dense;
    return _NavDensity.compact;
  }

  static double _rowWidth(
    BuildContext context,
    List<AppDestination> destinations,
    _NavDensity density,
  ) {
    final compact = density == _NavDensity.compact;
    final dense = density == _NavDensity.dense;
    final padding = compact
        ? AppSpacing.sm
        : dense
        ? AppSpacing.tight
        : AppSpacing.md;
    final gap = compact
        ? 0.0
        : dense
        ? 2.0
        : AppSpacing.xs;
    final spacing = dense ? 2.0 : AppSpacing.xxs;
    final style =
        Theme.of(context).textTheme.labelLarge?.copyWith(
          fontSize: dense ? 13 : null,
          letterSpacing: 0,
          fontWeight: FontWeight.w500,
        ) ??
        TextStyle(fontSize: dense ? 13 : null);

    var total = 0.0;
    for (final destination in destinations) {
      total += padding * 2 + 18 + gap;
      if (compact) continue;
      final painter = TextPainter(
        text: TextSpan(text: destination.label, style: style),
        maxLines: 1,
        textDirection: Directionality.of(context),
        locale: Localizations.maybeLocaleOf(context),
        textScaler: MediaQuery.textScalerOf(context),
      )..layout();
      // TextPainter matches the Text layout (same style, direction, locale
      // and text scaler), so the measured width is the real hit-target width.
      total += painter.width;
      painter.dispose();
    }
    if (destinations.isNotEmpty) {
      total += spacing * (destinations.length - 1);
    }
    return total;
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
    required this.dense,
    required this.onSelected,
  });

  final List<AppDestination> destinations;
  final int currentIndex;
  final bool compact;
  final bool dense;
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
    if (oldWidget.dense != widget.dense) _pendingAnimate = false;
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
              if (index > 0) SizedBox(width: widget.dense ? 2 : AppSpacing.xxs),
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
                  dense: widget.dense,
                  // The home bar is transparent in image mode: an ink plate
                  // under the pointer would read as a leftover block on the
                  // wallpaper. Solid mode keeps the normal hover feedback.
                  hoverable:
                      widget.currentIndex != AppBranch.dashboard ||
                      !context.tokens.imageBackground,
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
    required this.dense,
    required this.hoverable,
    required this.onTap,
  });

  final AppDestination destination;

  /// Whether this destination is the current branch (drives semantics).
  final bool selected;

  /// Whether it also carries the accent colour and the sliding plate. The home
  /// destination stays [selected] without being [highlighted].
  final bool highlighted;

  final bool compact;
  final bool dense;

  /// Whether the item paints hover/press ink. Off on the transparent home bar.
  final bool hoverable;

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
    final flat = isHome || !hoverable;
    final hoverTint = flat ? Colors.transparent : tokens.surface2;
    final pressTint = flat ? Colors.transparent : null;
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
              horizontal: compact
                  ? AppSpacing.sm
                  : dense
                  ? AppSpacing.tight
                  : AppSpacing.md,
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
                  SizedBox(width: dense ? 2 : AppSpacing.xs),
                  AnimatedDefaultTextStyle(
                    duration: tokens.fast,
                    curve: tokens.curve,
                    style:
                        Theme.of(context).textTheme.labelLarge?.copyWith(
                          color: foreground,
                          fontWeight: FontWeight.w500,
                          letterSpacing: 0,
                          fontSize: dense ? 13 : null,
                        ) ??
                        TextStyle(
                          color: foreground,
                          fontSize: dense ? 13 : null,
                        ),
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

enum _AccountMenuAction { manage }

class _AccountButton extends ConsumerStatefulWidget {
  const _AccountButton({
    required this.account,
    required this.compact,
    required this.onTap,
  });

  final Account? account;
  final bool compact;
  final VoidCallback onTap;

  @override
  ConsumerState<_AccountButton> createState() => _AccountButtonState();
}

class _AccountButtonState extends ConsumerState<_AccountButton> {
  final GlobalKey _buttonKey = GlobalKey();
  double _scrollAccumulator = 0;
  DateTime _lastWheelSwitch = DateTime.fromMillisecondsSinceEpoch(0);

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final l10n = AppLocalizations.of(context);
    final accounts = ref.watch(accountsProvider).value ?? const <Account>[];
    final current = widget.account;
    final name = current?.displayName ?? l10n.addAccount;
    final detail = current?.kind.labelFor(l10n) ?? l10n.accountNoAccounts;
    final initial = current == null ? '+' : name.characters.first.toUpperCase();

    void handlePointerSignal(PointerSignalEvent event) {
      if (event is! PointerScrollEvent) return;
      if (accounts.length < 2) return;
      // Horizontal touchpad gestures are not account switching.
      if (event.scrollDelta.dy.abs() <= event.scrollDelta.dx.abs()) return;
      _scrollAccumulator += event.scrollDelta.dy;
      if (_scrollAccumulator.abs() < 32) return;
      final direction = _scrollAccumulator > 0 ? 1 : -1;
      _scrollAccumulator = 0;
      final now = DateTime.now();
      if (now.difference(_lastWheelSwitch) <
          const Duration(milliseconds: 200)) {
        return;
      }
      _lastWheelSwitch = now;
      _cycleAccount(accounts, direction, l10n);
    }

    return KeyedSubtree(
      key: const ValueKey('command-bar-account'),
      child: Listener(
        onPointerSignal: handlePointerSignal,
        child: Semantics(
          button: true,
          label: name,
          hint: l10n.accountSwitchHint,
          child: InkWell(
            key: _buttonKey,
            onTap: widget.onTap,
            onLongPress: () => _showAccountSwitcher(context, accounts, l10n),
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
                  if (!widget.compact) ...[
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
                            style: Theme.of(context).textTheme.labelSmall
                                ?.copyWith(
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
        ),
      ),
    );
  }

  Future<void> _showAccountSwitcher(
    BuildContext context,
    List<Account> accounts,
    AppLocalizations l10n,
  ) async {
    final overlay = Overlay.of(context).context.findRenderObject();
    final button = _buttonKey.currentContext?.findRenderObject();
    if (overlay is! RenderBox || button is! RenderBox) return;

    final topLeft = button.localToGlobal(Offset.zero, ancestor: overlay);
    final bottomRight = button.localToGlobal(
      button.size.bottomRight(Offset.zero),
      ancestor: overlay,
    );
    final selected = await showMenu<Object>(
      context: context,
      position: RelativeRect.fromRect(
        Rect.fromPoints(topLeft, bottomRight),
        Offset.zero & overlay.size,
      ),
      clipBehavior: Clip.antiAlias,
      items: [
        if (accounts.isEmpty)
          PopupMenuItem<Object>(
            enabled: false,
            child: Text(l10n.accountSwitchEmpty),
          )
        else
          for (final account in accounts)
            PopupMenuItem<Object>(
              value: account,
              height: 52,
              child: _AccountMenuRow(account: account),
            ),
        const PopupMenuDivider(),
        PopupMenuItem<Object>(
          value: _AccountMenuAction.manage,
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                AppIcons.accounts,
                size: 18,
                color: context.tokens.textSecondary,
              ),
              const SizedBox(width: AppSpacing.xs),
              Text(l10n.accountManage),
            ],
          ),
        ),
      ],
    );

    if (!mounted || selected == null) return;
    if (selected == _AccountMenuAction.manage) {
      widget.onTap();
      return;
    }
    if (selected is Account) {
      await _switchTo(selected, l10n);
    }
  }

  void _cycleAccount(
    List<Account> accounts,
    int direction,
    AppLocalizations l10n,
  ) {
    if (accounts.length < 2) return;
    final currentId = widget.account?.id;
    var index = accounts.indexWhere((account) => account.id == currentId);
    if (index < 0) index = direction > 0 ? -1 : 0;
    final next =
        accounts[(index + direction + accounts.length) % accounts.length];
    _switchTo(next, l10n);
  }

  Future<void> _switchTo(Account account, AppLocalizations l10n) async {
    if (account.id == widget.account?.id) return;
    try {
      await ref.read(accountsProvider.notifier).select(account.id);
      if (!mounted) return;
      toast(l10n.accountSwitched(account.displayName), type: ToastType.success);
    } catch (_) {
      if (!mounted) return;
      toast(l10n.accountSwitchFailed, type: ToastType.error);
    }
  }
}

class _AccountMenuRow extends StatelessWidget {
  const _AccountMenuRow({required this.account});

  final Account account;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        AccountAvatar(account: account, size: 26),
        const SizedBox(width: AppSpacing.xs),
        Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 180),
              child: Text(
                account.displayName,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            Text(
              account.kind.labelFor(l10n),
              style: Theme.of(context).textTheme.labelSmall
                  ?.copyWith(color: tokens.textTertiary),
            ),
          ],
        ),
        if (account.active) ...[
          const SizedBox(width: AppSpacing.xs),
          Icon(AppIcons.check, size: 16, color: tokens.accent),
        ],
      ],
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
