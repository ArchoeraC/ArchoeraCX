part of '../shell.dart';

enum _PageTransitionMode { cover, retract, lift }

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

  late final Animation<Offset> _offset = Tween<Offset>(
    begin: const Offset(0, 1),
    end: Offset.zero,
  ).animate(CurvedAnimation(parent: _controller, curve: Curves.easeOutCubic));

  late final Animation<Offset> _outOffset = Tween<Offset>(
    begin: Offset.zero,
    end: const Offset(0, 1),
  ).animate(CurvedAnimation(parent: _controller, curve: Curves.easeInCubic));

  /// Exit motion for leaving the home page: the old page slides up and out.
  late final Animation<Offset> _upOutOffset = Tween<Offset>(
    begin: Offset.zero,
    end: const Offset(0, -1),
  ).animate(CurvedAnimation(parent: _controller, curve: Curves.easeInCubic));

  late final Animation<Offset> _liftOffset = Tween<Offset>(
    begin: const Offset(0, 0.02),
    end: Offset.zero,
  ).animate(CurvedAnimation(parent: _controller, curve: Curves.easeOutCubic));

  ui.Image? _previous;
  bool _finishing = false;
  _PageTransitionMode _mode = _PageTransitionMode.cover;

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

    // With a wallpaper the page canvas is transparent, so a snapshot and the
    // live page would show through each other. Between the home page and any
    // branch the outgoing content slides out (up when leaving home, down when
    // returning) and the incoming page is revealed by a clip that follows the
    // vacated edge. The wallpaper never moves, and no opaque backing sheet is
    // needed. Switches between two non-home branches keep the normal lift
    // transition below.
    if (context.tokens.imageBackground && _mode != _PageTransitionMode.lift) {
      final fromTop = _mode == _PageTransitionMode.retract;
      final exit = fromTop ? _outOffset : _upOutOffset;
      return ClipRect(
        child: Stack(
          fit: StackFit.expand,
          children: [
            Positioned.fill(
              child: SlideTransition(
                position: exit,
                child: _Snapshot(image: previous),
              ),
            ),
            Positioned.fill(
              child: AnimatedBuilder(
                animation: exit,
                builder: (context, child) => ClipRect(
                  clipper: _RevealClipper(
                    progress: fromTop ? exit.value.dy : -exit.value.dy,
                    fromTop: fromTop,
                  ),
                  child: child,
                ),
                child: current,
              ),
            ),
          ],
        ),
      );
    }

    return ClipRect(
      child: Stack(
        fit: StackFit.expand,
        children: [
          if (_mode == _PageTransitionMode.retract) ...[
            Positioned.fill(child: current),
            Positioned.fill(
              child: SlideTransition(
                position: _outOffset,
                child: _Snapshot(image: previous),
              ),
            ),
          ] else if (_mode == _PageTransitionMode.lift) ...[
            // Page-to-page switches lift the new page under a dissolving
            // snapshot. Painting the snapshot on top lets the dissolve reveal
            // the finished page directly, so no opacity animation is needed and
            // the two layers never turn into a grey wash.
            Positioned.fill(
              child: SlideTransition(position: _liftOffset, child: current),
            ),
            Positioned.fill(
              child: AppDissolveMask(
                progress: _controller,
                program: reduceMotion
                    ? null
                    : AppShaderScope.of(context).transition,
                child: _Snapshot(image: previous),
              ),
            ),
          ] else ...[
            Positioned.fill(child: _Snapshot(image: previous)),
            Positioned.fill(
              child: SlideTransition(position: _offset, child: current),
            ),
          ],
        ],
      ),
    );
  }
}

/// Snapshot of the page that is leaving, scaled to the page width and pinned to
/// the top so it lines up with the live page under the finger.
///
/// The snapshot keeps its transparent canvas: in image mode the incoming page
/// is clipped to the area the snapshot has already vacated, so the two layouts
/// never paint on top of each other and the shared wallpaper stays behind both.
class _Snapshot extends StatelessWidget {
  const _Snapshot({required this.image});

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

/// Clips the incoming page to the region the outgoing snapshot has vacated.
class _RevealClipper extends CustomClipper<Rect> {
  const _RevealClipper({required this.progress, required this.fromTop});

  /// 0 = outgoing page still covers the viewport, 1 = fully gone.
  final double progress;

  /// Returning home reveals the page from the top edge downwards.
  final bool fromTop;

  @override
  Rect getClip(Size size) {
    final distance = size.height * progress.clamp(0.0, 1.0);
    return fromTop
        ? Rect.fromLTRB(0, 0, size.width, distance)
        : Rect.fromLTRB(0, size.height - distance, size.width, size.height);
  }

  @override
  bool shouldReclip(_RevealClipper oldClipper) =>
      oldClipper.progress != progress || oldClipper.fromTop != fromTop;
}
