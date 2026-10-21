import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

/// Angular surface used for the launcher's large blocks.
///
/// The shape is a straight rectangle with one or two cut corners. It keeps the
/// UI away from generic rounded cards while retaining a clear hit target.
class AppPanel extends StatelessWidget {
  const AppPanel({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(AppSpacing.md),
    this.surface = true,
    this.accent = false,
    this.onTap,
    this.clip = true,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final bool surface;
  final bool accent;
  final VoidCallback? onTap;
  final bool clip;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final shape = AppShapes.panel(
      tokens,
      side: BorderSide(
        color: accent ? tokens.accent.withValues(alpha: 0.55) : tokens.border,
      ),
    );

    Widget content = DecoratedBox(
      decoration: ShapeDecoration(
        color: surface ? tokens.surface1 : null,
        shape: shape,
      ),
      child: Padding(padding: padding, child: child),
    );

    if (clip) {
      content = ClipPath(
        clipper: ShapeBorderClipper(shape: shape),
        child: content,
      );
    }

    if (onTap == null) return content;

    return Material(
      color: Colors.transparent,
      child: InkWell(onTap: onTap, customBorder: shape, child: content),
    );
  }
}

/// Shared angular shapes for panels, dialogs and sheets.
abstract final class AppShapes {
  static BorderRadius panelRadius(AppTokens tokens) => BorderRadius.only(
    topRight: Radius.circular(tokens.bevelL),
    bottomLeft: Radius.circular(tokens.bevelS),
  );

  static BorderRadius dialogRadius(AppTokens tokens) => BorderRadius.only(
    topLeft: Radius.circular(tokens.bevelL),
    bottomRight: Radius.circular(tokens.bevelS),
  );

  static BeveledRectangleBorder panel(
    AppTokens tokens, {
    BorderSide side = BorderSide.none,
  }) => BeveledRectangleBorder(side: side, borderRadius: panelRadius(tokens));

  static BeveledRectangleBorder dialog(
    AppTokens tokens, {
    BorderSide side = BorderSide.none,
  }) => BeveledRectangleBorder(side: side, borderRadius: dialogRadius(tokens));
}
