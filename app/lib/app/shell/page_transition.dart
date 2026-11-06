part of '../shell.dart';

enum _PageTransitionMode { pushUp, pushDown, lift }

/// Key on the outgoing page snapshot, used by tests to check that it keeps
/// sliding with the incoming page instead of staying put.
const pageTransitionSnapshotKey = ValueKey('page-transition-snapshot');

class _PageTransition extends ConsumerStatefulWidget {
  const _PageTransition({
    required this.index,
    required this.child,
    required this.coverImage,
    required this.mode,
    required this.onCoverFinished,
  });

  final int index;
  final Widget child;
  final ui.Image? coverImage;
  final _PageTransitionMode mode;
  final ValueChanged<ui.Image> onCoverFinished;

  @override
  ConsumerState<_PageTransition> createState() => _PageTransitionState();
}

class _PageTransitionState extends ConsumerState<_PageTransition>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    final reduceMotion = ref.read(appearanceProvider).reduceMotion;
    _controller = AnimationController(
      vsync: this,
      duration: reduceMotion
          ? Duration.zero
          : const Duration(milliseconds: 320),
      value: 1,
    )..addStatusListener(_onStatusChanged);
  }

  /// Shared timing for the sliding pages. `CurveTween` keeps this allocation
  /// free of a disposable `CurvedAnimation`.
  late final Animation<double> _curve = _controller.drive(
    CurveTween(curve: Curves.easeOutCubic),
  );

  late final Animation<Offset> _enterFromBottom = Tween<Offset>(
    begin: const Offset(0, 1),
    end: Offset.zero,
  ).animate(_curve);

  /// Entering home when the page above is pushed back down.
  late final Animation<Offset> _enterFromTop = Tween<Offset>(
    begin: const Offset(0, -1),
    end: Offset.zero,
  ).animate(_curve);

  /// Outgoing page shares [_curve] with the entering page so the two tiles
  /// stay exactly adjacent: no gap to the wallpaper and no double-painted
  /// strip in the middle of the push.
  late final Animation<Offset> _leaveUp = Tween<Offset>(
    begin: Offset.zero,
    end: const Offset(0, -1),
  ).animate(_curve);

  late final Animation<Offset> _leaveDown = Tween<Offset>(
    begin: Offset.zero,
    end: const Offset(0, 1),
  ).animate(_curve);

  late final Animation<Offset> _liftOffset = Tween<Offset>(
    begin: const Offset(0, 0.02),
    end: Offset.zero,
  ).animate(_curve);

  ui.Image? _previous;
  bool _finishing = false;
  _PageTransitionMode _mode = _PageTransitionMode.pushUp;

  @override
  void didUpdateWidget(covariant _PageTransition oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.index == widget.index) return;

    // A second branch switch can interrupt an unfinished transition. The old
    // snapshot is no longer referenced by the shell (it already swapped in the
    // new cover image), so hand it back on the next frame instead of leaking
    // the GPU texture.
    final stale = _previous;
    if (stale != null) {
      _previous = null;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        widget.onCoverFinished(stale);
      });
    }

    if (widget.coverImage == null) {
      _clearPrevious();
      _controller.value = 1;
      return;
    }

    _previous = widget.coverImage;
    _mode = widget.mode;
    _finishing = false;
    _controller.forward(from: 0);
  }

  void _onStatusChanged(AnimationStatus status) {
    if (status == AnimationStatus.completed) _finishPrevious();
  }

  void _finishPrevious() {
    final image = _previous;
    if (image == null || _finishing) return;
    _finishing = true;
    _previous = null;
    if (mounted) setState(() {});
    widget.onCoverFinished(image);
  }

  void _clearPrevious() {
    final image = _previous;
    _previous = null;
    if (image != null) {
      _finishing = true;
      widget.onCoverFinished(image);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    _clearPrevious();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final reduceMotion = ref.watch(appearanceProvider).reduceMotion;
    _controller.duration = reduceMotion
        ? Duration.zero
        : const Duration(milliseconds: 320);
    final current = Material(color: context.tokens.bg, child: widget.child);
    final previous = _previous;
    if (previous == null) return current;

    if (_mode == _PageTransitionMode.lift) {
      // Page-to-page switches lift the new page under a dissolving snapshot.
      // Painting the snapshot on top lets the dissolve reveal the finished
      // page directly, so no opacity animation is needed and the two layers
      // never turn into a grey wash.
      return ClipRect(
        child: Stack(
          fit: StackFit.expand,
          children: [
            Positioned.fill(
              child: SlideTransition(position: _liftOffset, child: current),
            ),
            Positioned.fill(
              child: AppDissolveMask(
                progress: _controller,
                program: reduceMotion
                    ? null
                    : AppShaderScope.of(context).transition,
                child: _Snapshot(
                  key: pageTransitionSnapshotKey,
                  image: previous,
                ),
              ),
            ),
          ],
        ),
      );
    }

    // Home and branch pages swap like consecutive slides: the outgoing page
    // and the incoming page travel the same vertical distance in lockstep, so
    // the old page is still visible while it leaves instead of being covered.
    // With a wallpaper the page canvas is transparent, but the two tiles never
    // overlap or separate, so the wallpaper simply stays behind both.
    final leavingHome = _mode == _PageTransitionMode.pushUp;
    final enter = leavingHome ? _enterFromBottom : _enterFromTop;
    final exit = leavingHome ? _leaveUp : _leaveDown;
    return ClipRect(
      child: Stack(
        fit: StackFit.expand,
        children: [
          Positioned.fill(
            child: SlideTransition(
              position: exit,
              child: _Snapshot(key: pageTransitionSnapshotKey, image: previous),
            ),
          ),
          Positioned.fill(
            child: SlideTransition(position: enter, child: current),
          ),
        ],
      ),
    );
  }
}

/// Snapshot of the page that is leaving, scaled to the page width and pinned to
/// the top so it lines up with the live page under the finger.
///
/// The snapshot is drawn as captured, including its transparent canvas in image
/// mode: it and the incoming page tile the viewport while they slide together,
/// so the shared wallpaper stays visible behind both and no opaque backing
/// sheet is needed.
class _Snapshot extends StatelessWidget {
  const _Snapshot({super.key, required this.image});

  final ui.Image image;

  @override
  Widget build(BuildContext context) {
    return RawImage(
      image: image,
      fit: BoxFit.fitWidth,
      alignment: Alignment.topCenter,
    );
  }
}
