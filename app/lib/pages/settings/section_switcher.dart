part of '../settings_page.dart';

/// Fades between settings sections in the Material fade-through style: the
/// outgoing section fades out during the first third, the incoming section
/// fades in during the rest, so two dense panels are never mixed at full
/// opacity. A vertical slide would fight the shell page transition (the whole
/// page already enters from below), hence the few-pixel horizontal drift.
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

  Widget? _previous;

  void _onStatusChanged(AnimationStatus status) {
    if (status == AnimationStatus.completed && _previous != null) {
      setState(() => _previous = null);
    }
  }

  @override
  void didUpdateWidget(covariant _SectionSwitcher oldWidget) {
    super.didUpdateWidget(oldWidget);
    _controller.duration = widget.duration;
    if (oldWidget.child.key != widget.child.key) {
      _previous = oldWidget.child;
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
    final previous = _previous;
    return Stack(
      // Pass the incoming constraints through so the panel keeps the width it
      // had before the switch instead of shrinking to its widest child.
      fit: StackFit.passthrough,
      alignment: Alignment.topLeft,
      children: [
        if (previous != null)
          IgnorePointer(
            child: FadeTransition(
              opacity: ReverseAnimation(_outgoing),
              child: previous,
            ),
          ),
        FadeTransition(
          opacity: _incoming,
          child: SlideTransition(
            position: Tween<Offset>(
              begin: const Offset(0.012, 0),
              end: Offset.zero,
            ).animate(_incoming),
            child: widget.child,
          ),
        ),
      ],
    );
  }
}
