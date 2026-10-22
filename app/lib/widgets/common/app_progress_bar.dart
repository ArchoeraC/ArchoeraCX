import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

/// Angular progress bar that matches the launcher's chamfered panel language.
///
/// The track is a thin line with a 1px border and two chamfered corners
/// (top-right and bottom-left, mirroring [AppShapes.panel]); the fill has a
/// slanted leading edge. Pass a null [value] for an indeterminate sweep.
class AppProgressBar extends StatefulWidget {
  const AppProgressBar({
    super.key,
    this.value,
    this.height = 6,
    this.color,
    this.trackColor,
    this.semanticLabel,
  });

  /// 0..1 when known, null for an indeterminate sweep.
  final double? value;

  final double height;
  final Color? color;
  final Color? trackColor;
  final String? semanticLabel;

  @override
  State<AppProgressBar> createState() => _AppProgressBarState();
}

class _AppProgressBarState extends State<AppProgressBar>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1400),
  );

  @override
  void didUpdateWidget(AppProgressBar oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.value != widget.value) _syncAnimation();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _syncAnimation();
  }

  void _syncAnimation() {
    final animated = widget.value == null && !_reduceMotion;
    if (animated) {
      if (!_controller.isAnimating) _controller.repeat();
    } else if (_controller.isAnimating) {
      _controller.stop();
    }
  }

  bool get _reduceMotion =>
      MediaQuery.maybeDisableAnimationsOf(context) ?? false;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final value = widget.value;
    final percent = value == null
        ? null
        : (value.clamp(0.0, 1.0) * 100).round();

    return Semantics(
      label: widget.semanticLabel,
      value: percent == null ? null : '$percent%',
      child: SizedBox(
        height: widget.height,
        width: double.infinity,
        child: AnimatedBuilder(
          animation: _controller,
          builder: (context, _) => CustomPaint(
            painter: _AngularProgressPainter(
              value: value,
              sweep: _controller.value,
              height: widget.height,
              color: widget.color ?? tokens.accent,
              trackColor: widget.trackColor ?? tokens.surface2,
              borderColor: tokens.border,
              reduceMotion: _reduceMotion,
            ),
          ),
        ),
      ),
    );
  }
}

class _AngularProgressPainter extends CustomPainter {
  _AngularProgressPainter({
    required this.value,
    required this.sweep,
    required this.height,
    required this.color,
    required this.trackColor,
    required this.borderColor,
    required this.reduceMotion,
  });

  final double? value;
  final double sweep;
  final double height;
  final Color color;
  final Color trackColor;
  final Color borderColor;
  final bool reduceMotion;

  @override
  void paint(Canvas canvas, Size size) {
    if (size.width <= 0 || size.height <= 0) return;
    final h = size.height;
    final cut = math.min(h * 0.7, 10.0);
    final track = _chamfered(size.width, h, cut);

    canvas.drawPath(track, Paint()..color = trackColor);

    canvas.save();
    canvas.clipPath(track);
    final fillPaint = Paint()..color = color;
    final lead = math.max(2.0, h * 0.6);

    if (value == null) {
      // Indeterminate: a slanted segment sweeping left to right.
      final segment = math.max(size.width * 0.32, h * 4);
      final travel = size.width + segment;
      final x = reduceMotion
          ? (size.width - segment) / 2
          : travel * sweep - segment;
      canvas.drawPath(_segment(x, segment, h, lead), fillPaint);
    } else {
      final width = size.width * value!.clamp(0.0, 1.0);
      if (width > 0) {
        canvas.drawPath(_segment(0, width, h, lead), fillPaint);
      }
    }
    canvas.restore();

    // Hairline border keeps the bar readable on busy backgrounds.
    canvas.drawPath(
      track,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1
        ..color = borderColor,
    );
  }

  /// Rectangle with the top-right and bottom-left corners chamfered.
  Path _chamfered(double width, double height, double cut) {
    return Path()
      ..moveTo(0, 0)
      ..lineTo(width - cut, 0)
      ..lineTo(width, cut)
      ..lineTo(width, height)
      ..lineTo(cut, height)
      ..lineTo(0, height - cut)
      ..close();
  }

  /// Fill with a slanted leading (right) edge.
  Path _segment(double x, double width, double height, double lead) {
    final slant = math.min(lead, width);
    return Path()
      ..moveTo(x, 0)
      ..lineTo(x + width - slant, 0)
      ..lineTo(x + width, height)
      ..lineTo(x, height)
      ..close();
  }

  @override
  bool shouldRepaint(_AngularProgressPainter oldDelegate) =>
      oldDelegate.value != value ||
      oldDelegate.sweep != sweep ||
      oldDelegate.height != height ||
      oldDelegate.color != color ||
      oldDelegate.trackColor != trackColor ||
      oldDelegate.borderColor != borderColor ||
      oldDelegate.reduceMotion != reduceMotion;
}
