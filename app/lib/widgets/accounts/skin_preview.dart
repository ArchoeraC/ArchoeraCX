import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import '../../models/account.dart';
import '../../theme/tokens.dart';

/// Cropped head of a skin texture. Used by compact account rows.
class SkinHead extends StatelessWidget {
  const SkinHead({
    super.key,
    required this.account,
    required this.size,
    this.fallback,
  });

  final Account account;
  final double size;
  final Widget? fallback;

  @override
  Widget build(BuildContext context) {
    final url = account.skinUrl;
    if (url == null || url.isEmpty) return fallback ?? const SizedBox.shrink();
    return _SkinImage(
      url: url,
      builder: (context, image) {
        if (image == null) return fallback ?? const SizedBox.shrink();
        return CustomPaint(
          size: Size.square(size),
          painter: _SkinHeadPainter(image, context.tokens),
        );
      },
    );
  }
}

/// Flat 2D paper-doll preview assembled from the skin texture atlas.
///
/// This deliberately avoids a 3D renderer: it crops the head/body/arms/legs
/// from the 64×64 (or legacy 64×32) skin atlas and lays them out as a blocky
/// character. That is enough to make the account feel personal while keeping
/// the UI dependency-free.
class SkinPreview extends StatelessWidget {
  const SkinPreview({
    super.key,
    required this.account,
    this.width = 150,
    this.height = 230,
    this.imageProvider,
  });

  final Account account;
  final double width;
  final double height;

  /// Test-only injection; production resolves [Account.skinUrl].
  final ImageProvider? imageProvider;

  @override
  Widget build(BuildContext context) {
    final url = account.skinUrl;
    if (url == null || url.isEmpty) {
      return _PlaceholderSkin(
        width: width,
        height: height,
        tokens: context.tokens,
      );
    }
    return _SkinImage(
      url: url,
      provider: imageProvider,
      builder: (context, image) {
        if (image == null) {
          return _PlaceholderSkin(
            width: width,
            height: height,
            tokens: context.tokens,
          );
        }
        return CustomPaint(
          size: Size(width, height),
          painter: _SkinBodyPainter(image, context.tokens),
        );
      },
    );
  }
}

class _SkinImage extends StatefulWidget {
  const _SkinImage({required this.url, required this.builder, this.provider});

  final String url;
  final ImageProvider? provider;
  final Widget Function(BuildContext context, ui.Image? image) builder;

  @override
  State<_SkinImage> createState() => _SkinImageState();
}

class _SkinImageState extends State<_SkinImage> {
  ImageStream? _stream;
  ImageStreamListener? _listener;
  ui.Image? _image;

  @override
  void initState() {
    super.initState();
    _resolve();
  }

  @override
  void didUpdateWidget(covariant _SkinImage oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.url != widget.url) {
      _detach();
      _image = null;
      _resolve();
    }
  }

  void _resolve() {
    try {
      final stream = (widget.provider ?? NetworkImage(widget.url)).resolve(
        const ImageConfiguration(),
      );
      final listener = ImageStreamListener((info, _) {
        if (!mounted) return;
        setState(() => _image = info.image);
      }, onError: (_, _) {});
      _stream = stream;
      _listener = listener;
      stream.addListener(listener);
    } catch (_) {
      // Image loading is the only UI-owned network fetch; failure falls back
      // to the local placeholder.
    }
  }

  void _detach() {
    final stream = _stream;
    final listener = _listener;
    if (stream != null && listener != null) {
      stream.removeListener(listener);
    }
    _stream = null;
    _listener = null;
  }

  @override
  void dispose() {
    _detach();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => widget.builder(context, _image);
}

class _SkinHeadPainter extends CustomPainter {
  const _SkinHeadPainter(this.image, this.tokens);

  final ui.Image image;
  final AppTokens tokens;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.drawRect(Offset.zero & size, Paint()..color = tokens.surface2);
    final side = size.shortestSide;
    final paint = Paint()..filterQuality = FilterQuality.none;
    final face = _srcRect(image, 8, 8, 8, 8);
    if (face != null) {
      canvas.drawImageRect(image, face, Rect.fromLTWH(0, 0, side, side), paint);
    }
    // Legacy 64x32 skins do not have a usable hat layer; their trailing
    // pixels are often opaque black padding and would cover the face.
    if (image.height >= 64) {
      final hat = _srcRect(image, 40, 8, 8, 8);
      if (hat != null) {
        canvas.drawImageRect(
          image,
          hat,
          Rect.fromLTWH(0, 0, side, side),
          paint,
        );
      }
    }
  }

  @override
  bool shouldRepaint(covariant _SkinHeadPainter oldDelegate) =>
      oldDelegate.image != image || oldDelegate.tokens != tokens;
}

class _SkinBodyPainter extends CustomPainter {
  const _SkinBodyPainter(this.image, this.tokens);

  final ui.Image image;
  final AppTokens tokens;

  @override
  void paint(Canvas canvas, Size size) {
    final unit = (size.width / 16).clamp(0.0, size.height / 32);
    final offsetX = (size.width - unit * 16) / 2;
    final offsetY = (size.height - unit * 32) / 2;
    final paint = Paint()..filterQuality = FilterQuality.none;
    final shadow = Paint()
      ..color = tokens.textPrimary.withValues(alpha: 0.12)
      ..style = PaintingStyle.fill;

    canvas.drawOval(
      Rect.fromCenter(
        center: Offset(size.width / 2, offsetY + unit * 32.6),
        width: unit * 11,
        height: unit * 2,
      ),
      shadow,
    );

    void part(
      double sx,
      double sy,
      double sw,
      double sh,
      double dx,
      double dy,
      double dw,
      double dh, {
      bool flip = false,
    }) {
      final source = _srcRect(image, sx, sy, sw, sh);
      if (source == null) return;
      final destination = Rect.fromLTWH(
        offsetX + dx * unit,
        offsetY + dy * unit,
        dw * unit,
        dh * unit,
      );
      if (flip) {
        canvas.save();
        canvas.translate(destination.right, destination.top);
        canvas.scale(-1, 1);
        canvas.drawImageRect(
          image,
          source,
          Rect.fromLTWH(0, 0, destination.width, destination.height),
          paint,
        );
        canvas.restore();
      } else {
        canvas.drawImageRect(image, source, destination, paint);
      }
    }

    final legacy = image.height < 64;
    // Body first, then head and limbs.
    part(20, 20, 8, 12, 4, 8, 8, 12);
    part(20, 36, 8, 12, 4, 8, 8, 12);
    if (legacy) {
      part(4, 20, 4, 12, 4, 20, 4, 12);
      part(20, 20, 4, 12, 8, 20, 4, 12);
      part(44, 20, 4, 12, 0, 8, 4, 12);
      part(44, 20, 4, 12, 12, 8, 4, 12, flip: true);
    } else {
      part(4, 20, 4, 12, 4, 20, 4, 12);
      part(20, 52, 4, 12, 8, 20, 4, 12);
      part(44, 20, 4, 12, 0, 8, 4, 12);
      part(36, 52, 4, 12, 12, 8, 4, 12);
      part(44, 36, 4, 12, 0, 8, 4, 12);
      part(52, 52, 4, 12, 12, 8, 4, 12);
      part(4, 36, 4, 12, 4, 20, 4, 12);
      part(4, 52, 4, 12, 8, 20, 4, 12);
    }
    part(8, 8, 8, 8, 4, 0, 8, 8);
    if (!legacy) part(40, 8, 8, 8, 4, 0, 8, 8);
  }

  @override
  bool shouldRepaint(covariant _SkinBodyPainter oldDelegate) =>
      oldDelegate.image != image || oldDelegate.tokens != tokens;
}

class _PlaceholderSkin extends StatelessWidget {
  const _PlaceholderSkin({
    required this.width,
    required this.height,
    required this.tokens,
  });

  final double width;
  final double height;
  final AppTokens tokens;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: width,
      height: height,
      child: CustomPaint(painter: _PlaceholderPainter(tokens)),
    );
  }
}

class _PlaceholderPainter extends CustomPainter {
  const _PlaceholderPainter(this.tokens);

  final AppTokens tokens;

  @override
  void paint(Canvas canvas, Size size) {
    final grid = Paint()
      ..color = tokens.border.withValues(alpha: 0.55)
      ..strokeWidth = 1;
    const step = 24.0;
    for (var x = step; x < size.width; x += step) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), grid);
    }
    for (var y = step; y < size.height; y += step) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), grid);
    }

    final unit = size.height / 34;
    final left = size.width / 2 - unit * 5;
    final head = Paint()
      ..color = Color.lerp(tokens.warning, tokens.surface1, 0.45)!;
    final shirt = Paint()..color = tokens.accent;
    final trousers = Paint()..color = tokens.surface3;
    final outline = Paint()
      ..color = tokens.textPrimary.withValues(alpha: 0.35)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;

    final headRect = Rect.fromLTWH(
      left + unit * 2,
      unit * 5,
      unit * 6,
      unit * 6,
    );
    final bodyRect = Rect.fromLTWH(left + unit, unit * 12, unit * 8, unit * 10);
    final leftArm = Rect.fromLTWH(left - unit, unit * 12, unit * 2, unit * 9);
    final rightArm = Rect.fromLTWH(
      left + unit * 9,
      unit * 12,
      unit * 2,
      unit * 9,
    );
    final leftLeg = Rect.fromLTWH(
      left + unit * 1.5,
      unit * 22,
      unit * 3,
      unit * 7,
    );
    final rightLeg = Rect.fromLTWH(
      left + unit * 5.5,
      unit * 22,
      unit * 3,
      unit * 7,
    );

    canvas.drawRect(leftLeg, trousers);
    canvas.drawRect(rightLeg, trousers);
    canvas.drawRect(leftArm, shirt);
    canvas.drawRect(rightArm, shirt);
    canvas.drawRect(bodyRect, shirt);
    canvas.drawRect(headRect, head);
    for (final rect in [
      headRect,
      bodyRect,
      leftArm,
      rightArm,
      leftLeg,
      rightLeg,
    ]) {
      canvas.drawRect(rect, outline);
    }

    // Tiny face so the empty state still reads as a character, not a block.
    final eyes = Paint()..color = tokens.textPrimary.withValues(alpha: 0.7);
    canvas.drawRect(
      Rect.fromLTWH(left + unit * 3.2, unit * 7.2, unit * 0.7, unit * 0.7),
      eyes,
    );
    canvas.drawRect(
      Rect.fromLTWH(left + unit * 6.1, unit * 7.2, unit * 0.7, unit * 0.7),
      eyes,
    );
  }

  @override
  bool shouldRepaint(covariant _PlaceholderPainter oldDelegate) =>
      oldDelegate.tokens != tokens;
}

Rect? _srcRect(
  ui.Image image,
  double left,
  double top,
  double width,
  double height,
) {
  if (left + width > image.width || top + height > image.height) return null;
  return Rect.fromLTWH(left, top, width, height);
}
