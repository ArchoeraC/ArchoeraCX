import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

/// Window-level background.
///
/// Phase 1 is intentionally cheap: solid color + two soft glows.
/// Image/Mica/Acrylic layers arrive in Phase 2 with a solid fallback.
class AppBackground extends StatelessWidget {
  const AppBackground({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return ClipRect(
      child: Stack(
        fit: StackFit.expand,
        children: [
          ColoredBox(color: tokens.bg),
          Positioned(
            top: -220,
            right: -160,
            child: _Glow(color: tokens.accent, size: 560, opacity: 0.10),
          ),
          Positioned(
            bottom: -260,
            left: -200,
            child: _Glow(color: tokens.accent, size: 520, opacity: 0.06),
          ),
          Positioned.fill(child: child),
        ],
      ),
    );
  }
}

class _Glow extends StatelessWidget {
  const _Glow({required this.color, required this.size, required this.opacity});

  final Color color;
  final double size;
  final double opacity;

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(
            colors: [
              color.withValues(alpha: opacity),
              color.withValues(alpha: 0),
            ],
          ),
        ),
      ),
    );
  }
}
