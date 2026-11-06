part of '../shell.dart';

enum _PageTransitionMode { cover, retract, lift }

/// Key on the outgoing page snapshot, used by tests to check that it keeps
/// sliding with the incoming page instead of staying put.
const pageTransitionSnapshotKey = ValueKey('page-transition-snapshot');

class _PageTransition extends ConsumerStatefulWidget {
  const _PageTransition({
    required this.index,
    required this.child,
    required this.coverImage,
    required this.mode,
    required this.onCoverAdopted,
  });

  final int index;
  final Widget child;
  final ui.Image? coverImage;
  final _PageTransitionMode mode;

  /// Called during the build that adopts [coverImage] so the shell can drop its
  /// own reference: the transition becomes the only owner from here on and is
  /// responsible for disposing the texture exactly once.
  final ValueChanged<ui.Image> onCoverAdopted;

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

  /// Shared timing for the entering layers. `CurveTween` keeps this allocation
  /// free of a disposable `CurvedAnimation`.
  late final Animation<double> _curve = _controller.drive(
    CurveTween(curve: Curves.easeOutCubic),
  );

  /// Leaving layers accelerate away instead of easing out, so the outgoing page
  /// is clearly on its way out while the incoming one settles.
  late final Animation<double> _exitCurve = _controller.drive(
    CurveTween(curve: Curves.easeInCubic),
  );

  /// Covering the dashboard (solid canvas): the branch page rises from the
  /// bottom edge over the frozen dashboard snapshot.
  late final Animation<Offset> _offset = Tween<Offset>(
    begin: const Offset(0, 1),
    end: Offset.zero,
  ).animate(_curve);

  /// Returning to the dashboard (solid canvas): the frozen branch snapshot
  /// slides back down over the live dashboard.
  late final Animation<Offset> _outOffset = Tween<Offset>(
    begin: Offset.zero,
    end: const Offset(0, 1),
  ).animate(_exitCurve);

  /// Wallpaper mode: the two pages tile the viewport and travel one viewport in
  /// lockstep, sharing [_curve] so the seam never opens or double-paints.
  late final Animation<Offset> _enterFromBottom = Tween<Offset>(
    begin: const Offset(0, 1),
    end: Offset.zero,
  ).animate(_curve);

  late final Animation<Offset> _enterFromTop = Tween<Offset>(
    begin: const Offset(0, -1),
    end: Offset.zero,
  ).animate(_curve);

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
  _PageTransitionMode _mode = _PageTransitionMode.cover;

  @override
  void didUpdateWidget(covariant _PageTransition oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.index == widget.index) return;

    // A second branch switch can interrupt an unfinished transition; the stale
    // snapshot is not painted after this build, so release it once the frame
    // that stops referencing it has been rasterized.
    final stale = _previous;
    _previous = null;
    _disposeAfterFrame(stale);

    final cover = widget.coverImage;
    if (cover == null) {
      _controller.value = 1;
      return;
    }

    _previous = cover;
    widget.onCoverAdopted(cover);
    _mode = widget.mode;
    _controller.forward(from: 0);
  }

  void _onStatusChanged(AnimationStatus status) {
    if (status != AnimationStatus.completed) return;
    final image = _previous;
    if (image == null) return;
    _previous = null;
    if (mounted) setState(() {});
    _disposeAfterFrame(image);
  }

  /// Releases a texture after the current frame, when the snapshot layer that
  /// still samples it has been composited.
  void _disposeAfterFrame(ui.Image? image) {
    if (image == null) return;
    WidgetsBinding.instance.addPostFrameCallback((_) => image.dispose());
  }

  @override
  void dispose() {
    _controller.dispose();
    // No frame is painted after this point, so the texture can go immediately;
    // the transition is the only owner of every adopted cover.
    _previous?.dispose();
    _previous = null;
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

    // Wallpaper mode: the page canvas is transparent, so the outgoing page and
    // the incoming page travel together like consecutive slides. They share one
    // curve and one viewport of travel, which keeps the seam closed and lets the
    // wallpaper stay visible behind both. This is the recent behaviour; only the
    // solid canvas keeps the older cover/retract motion.
    final snapshot = _Snapshot(key: pageTransitionSnapshotKey, image: previous);
    if (context.tokens.imageBackground) {
      final leavingHome = _mode == _PageTransitionMode.cover;
      return ClipRect(
        child: Stack(
          fit: StackFit.expand,
          children: [
            Positioned.fill(
              child: SlideTransition(
                position: leavingHome ? _leaveUp : _leaveDown,
                child: snapshot,
              ),
            ),
            Positioned.fill(
              child: SlideTransition(
                position: leavingHome ? _enterFromBottom : _enterFromTop,
                child: current,
              ),
            ),
          ],
        ),
      );
    }

    if (_mode == _PageTransitionMode.retract) {
      // Returning to the dashboard on a solid canvas: the branch page stays and
      // the dashboard snapshot slides down over it.
      return ClipRect(
        child: Stack(
          fit: StackFit.expand,
          children: [
            Positioned.fill(child: current),
            Positioned.fill(
              child: SlideTransition(position: _outOffset, child: snapshot),
            ),
          ],
        ),
      );
    }

    // Entering a branch on a solid canvas: the dashboard snapshot stays put and
    // the branch page covers it from below.
    return ClipRect(
      child: Stack(
        fit: StackFit.expand,
        children: [
          Positioned.fill(child: snapshot),
          Positioned.fill(
            child: SlideTransition(position: _offset, child: current),
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

