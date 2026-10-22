import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

/// Network image with the launcher's neutral loading/error surfaces.
///
/// Image loading is the one UI operation allowed to touch the network
/// directly; metadata and downloads still go through the native core.
class AppNetworkImage extends StatelessWidget {
  const AppNetworkImage({
    super.key,
    required this.url,
    this.fit = BoxFit.cover,
    this.fallbackIcon,
    this.borderRadius,
  });

  final String url;
  final BoxFit fit;
  final IconData? fallbackIcon;
  final BorderRadius? borderRadius;

  @override
  Widget build(BuildContext context) {
    // `image_outlined` is not emitted by the release icon tree shaker in this
    // SDK, so the placeholder uses a glyph that is already part of the subset
    // (and matches the download page's broken-image placeholder).
    final placeholderIcon = fallbackIcon ?? Icons.broken_image_outlined;
    final image = Image.network(
      url,
      fit: fit,
      filterQuality: FilterQuality.medium,
      frameBuilder: (context, child, frame, wasSynchronouslyLoaded) {
        if (wasSynchronouslyLoaded) return child;
        return AnimatedOpacity(
          opacity: frame == null ? 0 : 1,
          duration: const Duration(milliseconds: 180),
          curve: Curves.easeOut,
          child: child,
        );
      },
      loadingBuilder: (context, child, progress) =>
          progress == null ? child : _Placeholder(icon: placeholderIcon),
      errorBuilder: (_, _, _) => _Placeholder(icon: placeholderIcon),
    );
    if (borderRadius == null) return image;
    return ClipRRect(borderRadius: borderRadius!, child: image);
  }
}

class _Placeholder extends StatelessWidget {
  const _Placeholder({required this.icon});

  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return ColoredBox(
      color: tokens.surface2,
      child: Center(child: Icon(icon, color: tokens.textTertiary, size: 22)),
    );
  }
}
