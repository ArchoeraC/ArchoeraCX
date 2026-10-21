import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

/// A flat page section.
///
/// Sections replace decorative cards for page-level grouping: no fill, no
/// shadow, no rounded box. Separation comes from a single hairline rule.
class AppSection extends StatelessWidget {
  const AppSection({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.symmetric(vertical: AppSpacing.lg),
    this.showTop = false,
    this.showBottom = true,
    this.surface = false,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final bool showTop;
  final bool showBottom;
  final bool surface;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Container(
      width: double.infinity,
      padding: padding,
      decoration: BoxDecoration(
        color: surface ? tokens.surface1.withValues(alpha: 0.42) : null,
        border: Border(
          top: showTop ? BorderSide(color: tokens.border) : BorderSide.none,
          bottom: showBottom
              ? BorderSide(color: tokens.border)
              : BorderSide.none,
        ),
      ),
      child: child,
    );
  }
}
