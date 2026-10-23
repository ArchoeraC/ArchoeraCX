import 'package:flutter/material.dart';

import '../../render/app_shaders.dart';
import '../../theme/tokens.dart';
import 'hover_glow.dart';

/// Angular surface used for the launcher's large blocks.
///
/// The shape is a straight rectangle with one or two cut corners. It keeps the
/// UI away from generic rounded cards while retaining a clear hit target.
///
/// Interactive panels ([onTap] != null) additionally get an [AppHoverGlow]
/// sheen that follows the pointer. The glow is clipped by the same
/// [ShapeBorderClipper] as the surface, paints above the content, and ignores
/// pointers. It adds no [Material] of its own, so the ink response still
/// attaches to the [Material] created here; a backend without a fragment
/// program keeps the plain panel with no extra pointer plumbing.
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

    // Only interactive panels get the sheen; static panels stay a plain
    // DecoratedBox with no pointer listeners. The glow sits inside the clip
    // below so the bevel cuts it exactly like the surface. When no fragment
    // program is available (no shader scope, backend without runtime effects),
    // the widget collapses to the child and the panel keeps its plain look.
    if (onTap != null) {
      content = AppHoverGlow(
        color: tokens.accent,
        program: AppShaderScope.of(context).glow,
        child: content,
      );
    }

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
