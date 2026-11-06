import 'package:flutter/material.dart';

import '../../theme/tokens.dart';
import 'image_background.dart';

/// Window-level background: one opaque canvas colour, or a user wallpaper.
///
/// The shell keeps this as the single L0 layer. In solid mode it paints the
/// theme canvas; in image mode it paints a dark fallback, the baked wallpaper
/// (with blur/scale/dim) and then the transparent page tree above it. Pages
/// must never paint their own window background.
class AppBackground extends StatelessWidget {
  const AppBackground({
    super.key,
    required this.child,
    this.imagePath,
    this.imageBlur = 0,
    this.imageDim = 0.4,
    this.imageScale = 1.2,
  });

  final Widget child;

  /// Absolute image path, or null for the solid canvas.
  final String? imagePath;

  final double imageBlur;
  final double imageDim;
  final double imageScale;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final path = imagePath;
    if (path == null || path.isEmpty) {
      return ColoredBox(color: tokens.bg, child: child);
    }

    // In image mode `tokens.bg` is transparent so page surfaces reveal the
    // wallpaper; keep an opaque dark layer below it for the decode frame and
    // for missing/unsupported files.
    final base = tokens.imageBackground ? AppTokens.dark.bg : tokens.bg;
    return Stack(
      fit: StackFit.expand,
      clipBehavior: Clip.hardEdge,
      children: [
        ColoredBox(color: base),
        ImageBackground(
          path: path,
          blurSigma: imageBlur,
          dim: imageDim,
          scale: imageScale,
        ),
        child,
      ],
    );
  }
}
