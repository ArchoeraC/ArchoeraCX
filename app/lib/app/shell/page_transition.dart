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
