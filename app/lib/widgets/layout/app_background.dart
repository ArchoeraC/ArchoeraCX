import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

/// Window-level background: one opaque canvas colour.
///
/// The shell used to paint a shader/radial-gradient stack here. The launcher is
/// deliberately flat now: the page surface, the panels and both chrome bars all
/// sit on the exact same `tokens.bg`, so there is nothing to blend, band or
/// repaint between them. Keep this the single L0 layer; pages must never paint
/// their own window background.
class AppBackground extends StatelessWidget {
  const AppBackground({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return ColoredBox(color: context.tokens.bg, child: child);
  }
}
