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

    final side = (math.min(size.width, size.height) * 0.42)
        .clamp(160.0, 280.0)
        .toDouble();
    final frame = Rect.fromCenter(
      center: Offset(size.width / 2, size.height / 2),
      width: side,
      height: side,
    );
    final arm = side * 0.18;

    final bracketPaint = Paint()
      ..color = tokens.textSecondary.withValues(alpha: 0.62)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.1
      ..strokeCap = StrokeCap.square;
    final diamondPaint = Paint()
      ..color = tokens.warning
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.7
      ..strokeCap = StrokeCap.round;
    final innerDiamondPaint = Paint()
      ..color = tokens.warning.withValues(alpha: 0.38)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;

    final brackets = _brackets(frame, arm);
    for (var index = 0; index < brackets.length; index++) {
      final start = 0.02 + index * 0.055;
      final progress = _segment(this.progress, start, start + 0.38);
      _drawPartial(canvas, brackets[index], bracketPaint, progress);
    }

    final diamond = _diamond(frame.center, side * 0.46);
    final diamondProgress = _segment(progress, 0.28, 0.68);
    _drawPartial(canvas, diamond, diamondPaint, diamondProgress);

    final innerDiamond = _diamond(frame.center, side * 0.25);
    final innerProgress = _segment(progress, 0.38, 0.74);
    _drawPartial(canvas, innerDiamond, innerDiamondPaint, innerProgress);

    // A short accent segment travels around the diamond as a loading pulse; it
    // never becomes a spinner or a filled progress bar.
    final sweepProgress = _segment(progress, 0.55, 0.9);
    _drawSweep(canvas, diamond, sweepProgress);
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

  /// Four viewfinder corner brackets, drawn as separate paths so they can
  /// appear one after another without a full frame or a logo shape.
  List<Path> _brackets(Rect rect, double arm) {
    return [
      Path()
        ..moveTo(rect.left, rect.top + arm)
        ..lineTo(rect.left, rect.top)
        ..lineTo(rect.left + arm, rect.top),
      Path()
        ..moveTo(rect.right - arm, rect.top)
        ..lineTo(rect.right, rect.top)
        ..lineTo(rect.right, rect.top + arm),
      Path()
        ..moveTo(rect.left, rect.bottom - arm)
        ..lineTo(rect.left, rect.bottom)
        ..lineTo(rect.left + arm, rect.bottom),
      Path()
        ..moveTo(rect.right - arm, rect.bottom)
        ..lineTo(rect.right, rect.bottom)
        ..lineTo(rect.right, rect.bottom - arm),
    ];
  }

  Path _diamond(Offset center, double size) {
    final half = size / 2;
    return Path()
      ..moveTo(center.dx, center.dy - half)
      ..lineTo(center.dx + half, center.dy)
      ..lineTo(center.dx, center.dy + half)
      ..lineTo(center.dx - half, center.dy)
      ..close();
  }

  @override
  bool shouldRepaint(covariant _LaunchSplashPainter oldDelegate) {
    return oldDelegate.progress != progress || oldDelegate.tokens != tokens;
  }
}
