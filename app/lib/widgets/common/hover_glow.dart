import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/foundation.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/widgets.dart';

/// Key of the glow layer that [AppHoverGlow] inserts while it is active.
///
/// The layer only exists when a fragment program is available and the pointer
/// (or a press) made the sheen visible, so tests can assert that the plain
/// fallback adds no paint work.
const Key appHoverGlowKey = Key('app-hover-glow');

/// Pointer-following accent sheen for interactive panels.
///
/// The sheen is drawn by `shaders/glow.frag`, not by a [RadialGradient]: a
/// gradient would allocate a new shader object on every pointer move and a
/// blurred layer would cost an extra offscreen pass. The fragment program
/// evaluates one analytic radial falloff per pixel, so moving the pointer only
/// repaints a single quad with no allocation, blur or layout change.
///
/// This is a pure enhancement: the panel keeps its existing surface, clip and
/// ink handling, and the sheen is the only added paint. [program] is optional
/// on purpose (see `AppShaderScope`); when it is null, when [enabled] is false,
/// or on a backend without runtime effects, the widget returns [child]
/// unchanged.
///
/// Intensity is deliberately capped in the painter: hover contributes at most
/// 0.10 alpha and the transient press pulse at most 0.16. The shader emits
/// premultiplied color from that value, so the sheen stays a hint under text
/// instead of becoming a light box.
class AppHoverGlow extends StatefulWidget {
  const AppHoverGlow({
    super.key,
    required this.child,
    required this.color,
    this.program,
    this.enabled = true,
    this.radius = 220,
  });

  final Widget child;

  /// Accent of the sheen. Alpha is forced to 1.0 because the painter owns the
  /// envelope; a translucent accent would double-apply the fade.
  final Color color;

  /// Compiled `glow.frag`, usually `AppShaderScope.of(context).glow`.
  final ui.FragmentProgram? program;

  /// When false the glow is not installed at all (no `MouseRegion`, `Listener`
  /// or `Stack`), which is the zero-overhead path for static panels.
  final bool enabled;

  /// Falloff radius in logical pixels.
  final double radius;

  @override
  State<AppHoverGlow> createState() => _AppHoverGlowState();
}

class _AppHoverGlowState extends State<AppHoverGlow>
    with TickerProviderStateMixin {
  /// Hover fade-in, kept short so the sheen feels attached to the cursor.
  static const Duration _enterDuration = Duration(milliseconds: 160);

  /// Hover fade-out outlives the entry so leaving never strobes.
  static const Duration _exitDuration = Duration(milliseconds: 220);

  /// One half of the press pulse; the forward + reverse pair is ~240 ms.
  static const Duration _pulseDuration = Duration(milliseconds: 120);

  late final AnimationController _hover = AnimationController(
    vsync: this,
    duration: _enterDuration,
    reverseDuration: _exitDuration,
  );

  late final AnimationController _pulse = AnimationController(
    vsync: this,
    duration: _pulseDuration,
  );

  /// Pointer in panel-local logical pixels. Moving it only notifies the
  /// painter, so a hover move never rebuilds this widget's subtree.
  final ValueNotifier<Offset> _pointer = ValueNotifier<Offset>(Offset.zero);

  late final Listenable _repaint = Listenable.merge([_hover, _pulse, _pointer]);

  ui.FragmentProgram? _program;
  ui.FragmentShader? _shader;

  bool _hovering = false;
  bool _active = false;
  bool _reduceMotion = false;
  bool _tickerEnabled = true;

  /// Reduced motion (and a muted [TickerMode]) jumps between end states
  /// instead of starting a ticker.
  bool get _animate => !_reduceMotion && _tickerEnabled;

  @override
  void initState() {
    super.initState();
    _syncShader();
    _hover.addStatusListener(_onHoverStatus);
    _pulse.addStatusListener(_onPulseStatus);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _reduceMotion = MediaQuery.maybeDisableAnimationsOf(context) ?? false;
    _tickerEnabled = TickerMode.valuesOf(context).enabled;
    // A preference or a muted subtree can change mid-hover; settle to the
    // static state so an in-flight ticker does not keep the frame loop alive.
    if (!_animate) _settle();
  }

  @override
  void didUpdateWidget(AppHoverGlow oldWidget) {
    super.didUpdateWidget(oldWidget);
    _syncShader();
    if (!widget.enabled || _shader == null) {
      _hovering = false;
      _active = false;
      _hover.value = 0;
      _pulse.value = 0;
    }
  }

  /// Creates exactly one [ui.FragmentShader] per program and disposes the old
  /// one, so rebuilding the scope cannot leak native shader handles.
  void _syncShader() {
    final program = widget.enabled ? widget.program : null;
    if (identical(program, _program)) return;
    _shader?.dispose();
    _shader = program?.fragmentShader();
    _program = program;
  }

  void _onHoverStatus(AnimationStatus status) {
    if (status == AnimationStatus.dismissed) _hideIfIdle();
  }

  void _onPulseStatus(AnimationStatus status) {
    if (status == AnimationStatus.completed) {
      _pulse.reverse();
    } else if (status == AnimationStatus.dismissed) {
      _hideIfIdle();
    }
  }

  void _hideIfIdle() {
    if (!mounted || !_active) return;
    if (_hovering || !_hover.isDismissed || !_pulse.isDismissed) return;
    setState(() => _active = false);
  }

  void _reveal() {
    if (_active) return;
    setState(() => _active = true);
  }

  /// Snaps to the state the animations would reach, without ticking.
  void _settle() {
    _hover.value = _hovering ? 1 : 0;
    _pulse.value = 0;
    _active = _hovering;
  }

  void _onEnter(PointerEnterEvent event) {
    _hovering = true;
    _pointer.value = event.localPosition;
    _reveal();
    if (_animate) {
      _hover.forward();
    } else {
      _hover.value = 1;
    }
  }

  void _onHover(PointerHoverEvent event) {
    _pointer.value = event.localPosition;
  }

  void _onExit(PointerExitEvent event) {
    _hovering = false;
    if (_animate) {
      _hover.reverse();
    } else {
      _hover.value = 0;
      _hideIfIdle();
    }
  }

  void _onDown(PointerDownEvent event) {
    _pointer.value = event.localPosition;
    // Under reduced motion the hover state is already settled; a transient
    // press pulse would have no visible start, so keep the static state.
    if (!_animate) return;
    // Start the pulse before revealing the layer: switching a controller from
    // "reverse" back to "forward" can report a dismissed state, and a hide
    // between the two calls must not swallow the new pulse.
    _pulse.forward(from: 0);
    _reveal();
  }

  void _onUp(PointerUpEvent event) {
    _pointer.value = event.localPosition;
    // The pulse keeps its fixed envelope after release so a quick click stays
    // visible; it reverses itself when the rise completes.
  }

  void _onCancel(PointerCancelEvent event) {
    // Same as release: the pulse is self-terminating, keep the final position.
  }

  @override
  void dispose() {
    _hover.removeStatusListener(_onHoverStatus);
    _pulse.removeStatusListener(_onPulseStatus);
    _hover.dispose();
    _pulse.dispose();
    _pointer.dispose();
    _shader?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final shader = _shader;
    if (!widget.enabled || shader == null) return widget.child;

    return Listener(
      onPointerDown: _onDown,
      onPointerUp: _onUp,
      onPointerCancel: _onCancel,
      child: MouseRegion(
        onEnter: _onEnter,
        onHover: _onHover,
        onExit: _onExit,
        child: Stack(
          // Passthrough keeps the child's incoming constraints exactly as they
          // were before this wrapper: with a loose stack the panel would stop
          // filling tight slots (e.g. a full-width card in a Column).
          fit: StackFit.passthrough,
          // The caller clips to the panel shape; the child may already paint
          // outside its box, so the stack must not clip on its own.
          clipBehavior: Clip.none,
          children: [
            widget.child,
            if (_active)
              Positioned.fill(
                child: IgnorePointer(
                  child: CustomPaint(
                    key: appHoverGlowKey,
                    willChange: true,
                    painter: _HoverGlowPainter(
                      shader: shader,
                      color: widget.color.withValues(alpha: 1.0),
                      radius: widget.radius,
                      hover: _hover,
                      pulse: _pulse,
                      pointer: _pointer,
                      repaint: _repaint,
                    ),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// Peak alpha of the hover sheen.
const double _hoverAlpha = 0.10;

/// Peak alpha of the press sheen. Also the ceiling of the combined envelope.
const double _pulseAlpha = 0.16;

/// Paints the accent quad with the uniform layout of `shaders/glow.frag`.
///
/// The animation values are read at paint time and the [CustomPainter]'s
/// repaint [Listenable] is the merged controllers/pointer, so pointer moves
/// produce one paint of this quad without rebuilding the panel.
class _HoverGlowPainter extends CustomPainter {
  _HoverGlowPainter({
    required this.shader,
    required this.color,
    required this.radius,
    required this.hover,
    required this.pulse,
    required this.pointer,
    required Listenable repaint,
  }) : super(repaint: repaint);

  final ui.FragmentShader shader;
  final Color color;
  final double radius;
  final Animation<double> hover;
  final Animation<double> pulse;
  final ValueListenable<Offset> pointer;

  @override
  void paint(Canvas canvas, Size size) {
    if (size.width <= 0 || size.height <= 0) return;

    // Hover and press are separate bounded layers: the pressed panel can reach
    // 0.16, but never the sum, so text contrast does not depend on hover state.
    final intensity = math.min(
      hover.value * _hoverAlpha + pulse.value * _pulseAlpha,
      _pulseAlpha,
    );
    if (intensity <= 0) return;

    final at = pointer.value;
    shader
      ..setFloat(0, size.width)
      ..setFloat(1, size.height)
      ..setFloat(2, at.dx)
      ..setFloat(3, at.dy)
      ..setFloat(4, color.r)
      ..setFloat(5, color.g)
      ..setFloat(6, color.b)
      ..setFloat(7, color.a)
      ..setFloat(8, intensity)
      ..setFloat(9, radius);

    canvas.drawRect(Offset.zero & size, Paint()..shader = shader);
  }

  @override
  bool shouldRepaint(_HoverGlowPainter oldDelegate) =>
      oldDelegate.shader != shader ||
      oldDelegate.color != color ||
      oldDelegate.radius != radius ||
      oldDelegate.hover != hover ||
      oldDelegate.pulse != pulse ||
      oldDelegate.pointer != pointer;
}
