import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../stores/appearance.dart';
import '../theme/tokens.dart';

/// One-shot launch animation drawn entirely with the launcher's line language.
///
/// The real shell is built underneath from the first frame, so the animation
/// never delays core startup. It is disabled for reduced-motion sessions and
/// for widget tests; the latter construct [ArchoeraApp] without the flag.
class AppLaunchSplash extends ConsumerStatefulWidget {
  const AppLaunchSplash({
    super.key,
    required this.enabled,
    required this.child,
  });

  final bool enabled;
  final Widget child;

  @override
  ConsumerState<AppLaunchSplash> createState() => _AppLaunchSplashState();
}

class _AppLaunchSplashState extends ConsumerState<AppLaunchSplash>
    with SingleTickerProviderStateMixin {
  static const _duration = Duration(milliseconds: 1200);

  AnimationController? _controller;
  bool _finished = false;

  @override
  void initState() {
    super.initState();
    final reduceMotion = ref.read(appearanceProvider).reduceMotion;
    if (!widget.enabled || reduceMotion) {
      _finished = true;
      return;
    }
    _controller = AnimationController(vsync: this, duration: _duration)
      ..addStatusListener((status) {
        if (status == AnimationStatus.completed && mounted) {
          setState(() => _finished = true);
        }
      })
      ..forward();
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    if (_finished || controller == null) return widget.child;

    final fade = CurvedAnimation(
      parent: controller,
      curve: const Interval(0.78, 1, curve: Curves.easeInCubic),
    );
    return Stack(
      fit: StackFit.expand,
      children: [
        widget.child,
        Positioned.fill(
          child: AbsorbPointer(
            child: FadeTransition(
              opacity: ReverseAnimation(fade),
              child: RepaintBoundary(
                key: const ValueKey('app-launch-splash'),
                child: AnimatedBuilder(
                  animation: controller,
                  builder: (context, _) {
                    final tokens = context.tokens;
                    return CustomPaint(
                      painter: _LaunchSplashPainter(
                        progress: controller.value,
                        tokens: tokens,
                      ),
                      child: const SizedBox.expand(),
                    );
                  },
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _LaunchSplashPainter extends CustomPainter {
  const _LaunchSplashPainter({required this.progress, required this.tokens});

  final double progress;
  final AppTokens tokens;

  @override
  void paint(Canvas canvas, Size size) {
    final background = Paint()..color = tokens.bg;
    canvas.drawRect(Offset.zero & size, background);
    if (size.isEmpty) return;

    final shortSide = math.min(size.width, size.height);
    final frameWidth = math
        .min(size.width * 0.44, 300.0)
        .clamp(180.0, 300.0)
        .toDouble();
    final frameHeight = math.min(frameWidth * 0.62, shortSide * 0.45);
    final rect = Rect.fromCenter(
      center: Offset(size.width / 2, size.height / 2),
      width: frameWidth,
      height: frameHeight,
    );

    final frameProgress = _segment(progress, 0, 0.52);
    final innerProgress = _segment(progress, 0.12, 0.64);
    final markProgress = _segment(progress, 0.28, 0.72);
    final sweepProgress = _segment(progress, 0.46, 0.82);

    final bevel = math.min(tokens.bevelL, frameWidth * 0.11);
    final outer = _beveledFrame(rect, bevel);
    final inner = _beveledFrame(rect.deflate(9), bevel * 0.72);

    final basePaint = Paint()
      ..color = tokens.border.withValues(alpha: 0.72)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;
    final faintPaint = Paint()
      ..color = tokens.border.withValues(alpha: 0.42)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;
    final accentPaint = Paint()
      ..color = tokens.accent
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.6
      ..strokeCap = StrokeCap.round;

    _drawPartial(canvas, outer, basePaint, frameProgress);
    _drawPartial(canvas, inner, faintPaint, innerProgress);

    // Small corner ticks make the frame read as a technical instrument rather
    // than a card.
    _drawTicks(canvas, rect, tokens, frameProgress);

    final center = rect.center;
    final glyph = Path()
      ..moveTo(center.dx - frameWidth * 0.12, center.dy + frameHeight * 0.10)
      ..lineTo(center.dx, center.dy - frameHeight * 0.15)
      ..lineTo(center.dx + frameWidth * 0.12, center.dy + frameHeight * 0.10)
      ..moveTo(center.dx - frameWidth * 0.065, center.dy + frameHeight * 0.10)
      ..lineTo(center.dx + frameWidth * 0.065, center.dy + frameHeight * 0.10);
    _drawPartial(canvas, glyph, accentPaint, markProgress);

    // A short accent segment travels around the inner frame as a loading
    // pulse; it never becomes a spinner or a filled progress bar.
    _drawSweep(canvas, inner, sweepProgress);
  }

  static double _segment(double value, double start, double end) {
    if (value <= start) return 0;
    if (value >= end) return 1;
    final t = (value - start) / (end - start);
    return Curves.easeOutCubic.transform(t);
  }

  void _drawPartial(Canvas canvas, Path path, Paint paint, double progress) {
    if (progress <= 0) return;
    for (final metric in path.computeMetrics()) {
      final end = metric.length * progress.clamp(0, 1);
      canvas.drawPath(metric.extractPath(0, end), paint);
    }
  }

  void _drawSweep(Canvas canvas, Path path, double progress) {
    if (progress <= 0 || progress >= 1) return;
    final highlight = Paint()
      ..color = tokens.accent.withValues(alpha: 0.9)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2
      ..strokeCap = StrokeCap.round;
    for (final metric in path.computeMetrics()) {
      final span = metric.length * 0.16;
      final start = (metric.length - span) * progress;
      canvas.drawPath(metric.extractPath(start, start + span), highlight);
    }
  }

  void _drawTicks(Canvas canvas, Rect rect, AppTokens tokens, double t) {
    if (t <= 0) return;
    final paint = Paint()
      ..color = tokens.textTertiary.withValues(alpha: 0.55)
      ..strokeWidth = 1
      ..strokeCap = StrokeCap.square;
    final tick = 6.0 * t;
    final corners = [
      (rect.topLeft, const Offset(1, 0), const Offset(0, 1)),
      (rect.topRight, const Offset(-1, 0), const Offset(0, 1)),
      (rect.bottomLeft, const Offset(1, 0), const Offset(0, -1)),
      (rect.bottomRight, const Offset(-1, 0), const Offset(0, -1)),
    ];
    for (final (point, horizontal, vertical) in corners) {
      canvas.drawLine(point, point + horizontal * tick, paint);
      canvas.drawLine(point, point + vertical * tick, paint);
    }
  }

  Path _beveledFrame(Rect rect, double bevel) {
    return Path()
      ..moveTo(rect.left, rect.top)
      ..lineTo(rect.right - bevel, rect.top)
      ..lineTo(rect.right, rect.top + bevel)
      ..lineTo(rect.right, rect.bottom)
      ..lineTo(rect.left + bevel, rect.bottom)
      ..lineTo(rect.left, rect.bottom - bevel)
      ..close();
  }

  @override
  bool shouldRepaint(covariant _LaunchSplashPainter oldDelegate) {
    return oldDelegate.progress != progress || oldDelegate.tokens != tokens;
  }
}
