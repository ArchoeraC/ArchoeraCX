import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../models/game_instance.dart';
import '../../theme/tokens.dart';

/// Original, theme-colored marks for loader families.
///
/// These are deliberately drawn as simplified silhouettes rather than copied
/// brand assets. They stay legible at 16–24 px and can be replaced with
/// licensed official artwork later without changing call sites.
class LoaderMark extends StatelessWidget {
  const LoaderMark({super.key, required this.loader, this.size = 24});

  final GameLoader loader;
  final double size;

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      size: Size.square(size),
      painter: _LoaderMarkPainter(loader: loader, tokens: context.tokens),
    );
  }
}

class _LoaderMarkPainter extends CustomPainter {
  const _LoaderMarkPainter({required this.loader, required this.tokens});

  final GameLoader loader;
  final AppTokens tokens;

  @override
  void paint(Canvas canvas, Size size) {
    final scale = size.shortestSide / 24;
    canvas.save();
    canvas.scale(scale);
    switch (loader) {
      case GameLoader.vanilla:
        _paintVanilla(canvas);
      case GameLoader.fabric:
        _paintFabric(canvas);
      case GameLoader.quilt:
        _paintQuilt(canvas);
      case GameLoader.forge:
        _paintForge(canvas, neo: false);
      case GameLoader.neoforge:
        _paintForge(canvas, neo: true);
    }
    canvas.restore();
  }

  Paint _fill(Color color) => Paint()
    ..color = color
    ..style = PaintingStyle.fill;

  Paint _stroke(Color color, [double width = 1.2]) => Paint()
    ..color = color
    ..style = PaintingStyle.stroke
    ..strokeWidth = width
    ..strokeJoin = StrokeJoin.round
    ..strokeCap = StrokeCap.round;

  void _outline(Canvas canvas, Path path, Color color) {
    canvas.drawPath(path, _stroke(color, 1.1));
  }

  /// Isometric grass block; the most recognizable vanilla silhouette.
  void _paintVanilla(Canvas canvas) {
    final top = Path()
      ..moveTo(12, 2.5)
      ..lineTo(21.5, 7.5)
      ..lineTo(12, 12.5)
      ..lineTo(2.5, 7.5)
      ..close();
    final left = Path()
      ..moveTo(2.5, 7.5)
      ..lineTo(12, 12.5)
      ..lineTo(12, 21.5)
      ..lineTo(2.5, 16.5)
      ..close();
    final right = Path()
      ..moveTo(21.5, 7.5)
      ..lineTo(12, 12.5)
      ..lineTo(12, 21.5)
      ..lineTo(21.5, 16.5)
      ..close();

    final side = Color.lerp(tokens.warning, tokens.textPrimary, 0.16)!;
    canvas.drawPath(top, _fill(tokens.success));
    canvas.drawPath(left, _fill(side));
    canvas.drawPath(right, _fill(Color.lerp(side, tokens.surface1, 0.22)!));
    for (final path in [top, left, right]) {
      _outline(canvas, path, tokens.textPrimary.withValues(alpha: 0.58));
    }

    // Grass fringe hanging over the two visible sides.
    final fringe = _fill(tokens.success.withValues(alpha: 0.95));
    for (final x in [5.0, 8.0, 15.0, 18.0]) {
      final y = x < 12 ? 9.2 + (x - 5) * 0.35 : 9.8 + (18 - x) * 0.3;
      canvas.drawRect(Rect.fromLTWH(x, y, 1.6, 2.2), fringe);
    }
  }

  /// Loom/thread motif: a woven diamond crossed by a needle.
  void _paintFabric(Canvas canvas) {
    final diamond = Path()
      ..moveTo(12, 2.2)
      ..lineTo(21.2, 12)
      ..lineTo(12, 21.8)
      ..lineTo(2.8, 12)
      ..close();
    canvas.drawPath(diamond, _fill(tokens.accentSoft));
    _outline(canvas, diamond, tokens.accent);

    final thread = _stroke(tokens.accent, 1.15);
    for (final y in [8.5, 12.0, 15.5]) {
      final width = y == 12.0 ? 7.0 : 4.8;
      canvas.drawLine(Offset(12 - width, y), Offset(12 + width, y), thread);
    }
    canvas.drawLine(const Offset(7.8, 6.8), const Offset(16.2, 17.2), thread);
    canvas.drawLine(const Offset(7.8, 17.2), const Offset(16.2, 6.8), thread);

    final needle = _stroke(tokens.textPrimary, 1.35);
    canvas.drawLine(const Offset(14.8, 17.8), const Offset(21.0, 21.2), needle);
    canvas.drawCircle(
      const Offset(20.2, 20.7),
      1.15,
      _fill(tokens.textPrimary),
    );
  }

  /// Patchwork square with stitched seams.
  void _paintQuilt(Canvas canvas) {
    final body = Path()
      ..moveTo(3.2, 3.2)
      ..lineTo(20.8, 3.2)
      ..lineTo(20.8, 20.8)
      ..lineTo(3.2, 20.8)
      ..close();
    canvas.drawPath(body, _fill(tokens.surface2));
    canvas.save();
    canvas.clipPath(body);
    final panels = [
      (const Rect.fromLTWH(3.2, 3.2, 8.8, 8.8), tokens.accent),
      (const Rect.fromLTWH(12, 3.2, 8.8, 8.8), tokens.success),
      (const Rect.fromLTWH(3.2, 12, 8.8, 8.8), tokens.warning),
      (const Rect.fromLTWH(12, 12, 8.8, 8.8), tokens.textTertiary),
    ];
    for (final (rect, color) in panels) {
      canvas.drawRect(rect, _fill(color.withValues(alpha: 0.55)));
    }
    canvas.restore();
    _outline(canvas, body, tokens.textPrimary.withValues(alpha: 0.55));

    final stitch = _stroke(tokens.textPrimary.withValues(alpha: 0.8), 1.05);
    _dashedLine(canvas, const Offset(12, 3.6), const Offset(12, 20.4), stitch);
    _dashedLine(canvas, const Offset(3.6, 12), const Offset(20.4, 12), stitch);
  }

  /// Anvil for Forge; NeoForge adds a flame and a warmer palette.
  void _paintForge(Canvas canvas, {required bool neo}) {
    if (neo) {
      final flame = Path()
        ..moveTo(12, 1.2)
        ..cubicTo(16.8, 5.2, 18.8, 8.6, 17.4, 11.4)
        ..cubicTo(19.2, 11.0, 20.2, 9.8, 20.8, 8.3)
        ..cubicTo(22.0, 12.4, 20.3, 16.2, 16.4, 18.1)
        ..cubicTo(14.0, 19.3, 10.0, 19.3, 7.6, 18.1)
        ..cubicTo(3.7, 16.2, 2.0, 12.4, 3.2, 8.3)
        ..cubicTo(3.8, 9.8, 4.8, 11.0, 6.6, 11.4)
        ..cubicTo(5.2, 8.6, 7.2, 5.2, 12, 1.2)
        ..close();
      canvas.drawPath(flame, _fill(tokens.danger.withValues(alpha: 0.9)));
      _outline(canvas, flame, tokens.danger);
    }

    final anvil = Path()
      ..moveTo(4.2, 8.0)
      ..lineTo(18.4, 8.0)
      ..lineTo(21.8, 10.0)
      ..lineTo(18.2, 11.4)
      ..lineTo(14.8, 11.4)
      ..lineTo(14.8, 14.2)
      ..lineTo(18.4, 14.2)
      ..lineTo(19.6, 18.4)
      ..lineTo(4.4, 18.4)
      ..lineTo(5.6, 14.2)
      ..lineTo(9.2, 14.2)
      ..lineTo(9.2, 11.4)
      ..lineTo(4.2, 11.4)
      ..close();
    final body = neo ? tokens.warning : tokens.textSecondary;
    canvas.drawPath(anvil, _fill(body.withValues(alpha: 0.9)));
    _outline(canvas, anvil, tokens.textPrimary.withValues(alpha: 0.62));

    // Hammer handle and head.
    final handle = _stroke(tokens.textPrimary, 1.6);
    canvas.drawLine(const Offset(5.2, 20.4), const Offset(17.0, 9.4), handle);
    final head = Path()
      ..moveTo(15.2, 5.4)
      ..lineTo(20.6, 8.2)
      ..lineTo(18.0, 11.0)
      ..lineTo(13.4, 8.3)
      ..close();
    canvas.drawPath(head, _fill(neo ? tokens.danger : tokens.warning));
    _outline(canvas, head, tokens.textPrimary.withValues(alpha: 0.62));
  }

  void _dashedLine(Canvas canvas, Offset start, Offset end, Paint paint) {
    final delta = end - start;
    final length = delta.distance;
    final direction = delta / length;
    const dash = 1.4;
    const gap = 1.2;
    for (var t = 0.0; t < length; t += dash + gap) {
      final a = start + direction * t;
      final b = start + direction * math.min(t + dash, length);
      canvas.drawLine(a, b, paint);
    }
  }

  @override
  bool shouldRepaint(covariant _LoaderMarkPainter oldDelegate) =>
      oldDelegate.loader != loader || oldDelegate.tokens != tokens;
}
