part of '../settings_page.dart';

/// Fades between settings sections in the Material fade-through style: the
/// outgoing section fades out during the first third, the incoming section
/// fades in during the rest, so two dense panels are never mixed at full
/// opacity. A vertical slide would fight the shell page transition (the whole
/// page already enters from below), hence the few-pixel horizontal drift.
///
/// The outgoing section is snapshotted with [RenderRepaintBoundary.toImageSync]
/// (a GPU texture, no read-back) and dissolved with the shared
/// `transition.frag` program. Keeping a texture instead of the live subtree
/// means only one settings section is laid out and painted per frame; the
/// live-widget cross-fade remains the fallback when no shader program is
/// available.
class _SectionSwitcher extends StatefulWidget {
  const _SectionSwitcher({required this.duration, required this.child});

  final Duration duration;
  final Widget child;

  @override
  State<_SectionSwitcher> createState() => _SectionSwitcherState();
}

class _SectionSwitcherState extends State<_SectionSwitcher>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: widget.duration,
    value: 1,
  )..addStatusListener(_onStatusChanged);

  late final Animation<double> _incoming = CurvedAnimation(
    parent: _controller,
    curve: const Interval(0.35, 1, curve: Curves.easeOutCubic),
  );
  late final Animation<double> _outgoing = CurvedAnimation(
    parent: _controller,
    curve: const Interval(0, 0.35, curve: Curves.easeInCubic),
  );

  late final Animation<Offset> _drift = Tween<Offset>(
    begin: const Offset(0.012, 0),
    end: Offset.zero,
  ).animate(_incoming);

  /// Snapshot source; only the section that is currently live is captured.
  final GlobalKey _currentKey = GlobalKey();

  /// Outgoing section as a texture (shader path).
  ui.Image? _snapshot;

  /// Outgoing section as a live widget (fallback path).
  Widget? _previous;

  ui.FragmentProgram? _program;

  /// Drops both outgoing representations and disposes the texture.
  void _dropPrevious() {
    _previous = null;
    final snapshot = _snapshot;
    _snapshot = null;
    snapshot?.dispose();
  }

  void _onStatusChanged(AnimationStatus status) {
    if (status == AnimationStatus.completed && _hasPrevious) {
      setState(_dropPrevious);
    }
  }

  bool get _hasPrevious => _snapshot != null || _previous != null;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // Programs arrive asynchronously; reading them here registers the
    // dependency so a late load switches subsequent sections to the shader.
    _program = AppShaderScope.of(context).transition;
  }

  @override
  void didUpdateWidget(covariant _SectionSwitcher oldWidget) {
    super.didUpdateWidget(oldWidget);
    _controller.duration = widget.duration;
    if (oldWidget.child.key == widget.child.key) return;

    _dropPrevious();
    final captured =
        widget.duration > Duration.zero && _snapshotCurrentSection();
    if (!captured) _previous = oldWidget.child;
    _controller.forward(from: 0);
  }

  /// Captures the live section before the new child replaces it.
  ///
  /// Returns false when no shader is available or the boundary has not been
  /// painted yet; the caller then keeps the old widget alive instead.
  bool _snapshotCurrentSection() {
    if (_program == null) return false;
    final boundary = _currentKey.currentContext?.findRenderObject();
    if (boundary is! RenderRepaintBoundary || boundary.debugNeedsPaint) {
      return false;
    }
    try {
      _snapshot = boundary.toImageSync(
        pixelRatio: MediaQuery.devicePixelRatioOf(context),
      );
      return true;
    } catch (_) {
      _snapshot = null;
      return false;
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    _dropPrevious();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final snapshot = _snapshot;
    final previous = _previous;
    return Stack(
      // Pass the incoming constraints through so the panel keeps the width it
      // had before the switch instead of shrinking to its widest child.
      fit: StackFit.passthrough,
      alignment: Alignment.topLeft,
      children: [
        RepaintBoundary(
          key: _currentKey,
          child: FadeTransition(
            opacity: _incoming,
            child: SlideTransition(position: _drift, child: widget.child),
          ),
        ),
        if (snapshot != null)
          Positioned.fill(
            child: IgnorePointer(
              child: AppDissolveMask(
                progress: _controller,
                program: _program,
                softness: 0.45,
                child: RawImage(
                  image: snapshot,
                  fit: BoxFit.fitWidth,
                  alignment: Alignment.topCenter,
                ),
              ),
            ),
          )
        else if (previous != null)
          IgnorePointer(
            child: FadeTransition(
              opacity: ReverseAnimation(_outgoing),
              child: previous,
            ),
          ),
      ],
    );
  }
}
