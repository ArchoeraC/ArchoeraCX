import 'package:flutter/material.dart';

import '../../models/game_instance.dart';
import '../../theme/tokens.dart';

/// Loader identity mark.
///
/// Fabric/Quilt/Forge/NeoForge use their official project icons, checked into
/// `assets/icons/loaders/` with source and license notes. Vanilla is an original
/// isometric grass-block mark because Mojang textures are not redistributed.
///
/// Forge's mark is a white anvil; it is tinted with `textPrimary` so it remains
/// visible in both light and dark themes.
class LoaderMark extends StatelessWidget {
  const LoaderMark({super.key, required this.loader, this.size = 24});

  final GameLoader loader;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final image = switch (loader) {
      GameLoader.vanilla => CustomPaint(
        size: Size.square(size),
        painter: _VanillaMarkPainter(tokens),
      ),
      GameLoader.fabric => _asset('assets/icons/loaders/fabric.png'),
      GameLoader.quilt => _asset('assets/icons/loaders/quilt.png'),
      GameLoader.forge => _asset(
        'assets/icons/loaders/forge.png',
        color: tokens.textPrimary,
      ),
      GameLoader.neoforge => _asset('assets/icons/loaders/neoforge.png'),
    };

    return Semantics(
      label: loader.name,
      image: true,
      child: SizedBox.square(dimension: size, child: image),
    );
  }

  Widget _asset(String path, {Color? color}) {
    return Image.asset(
      path,
      fit: BoxFit.contain,
      filterQuality: FilterQuality.none,
      color: color,
      colorBlendMode: color == null ? null : BlendMode.srcIn,
      errorBuilder: (context, error, stackTrace) => Icon(
        Icons.extension_rounded,
        size: size,
        color: context.tokens.accent,
      ),
    );
  }
}

class _VanillaMarkPainter extends CustomPainter {
  const _VanillaMarkPainter(this.tokens);

  final AppTokens tokens;

  @override
  void paint(Canvas canvas, Size size) {
    final scale = size.shortestSide / 24;
    canvas.save();
    canvas.scale(scale);

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
    final fill = Paint()..style = PaintingStyle.fill;
    final stroke = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.1
      ..strokeJoin = StrokeJoin.round
      ..color = tokens.textPrimary.withValues(alpha: 0.58);

    canvas.drawPath(top, fill..color = tokens.success);
    canvas.drawPath(left, fill..color = side);
    canvas.drawPath(
      right,
      fill..color = Color.lerp(side, tokens.surface1, 0.22)!,
    );
    for (final path in [top, left, right]) {
      canvas.drawPath(path, stroke);
    }

    fill.color = tokens.success.withValues(alpha: 0.95);
    for (final x in [5.0, 8.0, 15.0, 18.0]) {
      final y = x < 12 ? 9.2 + (x - 5) * 0.35 : 9.8 + (18 - x) * 0.3;
      canvas.drawRect(Rect.fromLTWH(x, y, 1.6, 2.2), fill);
    }

    canvas.restore();
  }

  @override
  bool shouldRepaint(covariant _VanillaMarkPainter oldDelegate) =>
      oldDelegate.tokens != tokens;
}
