import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

/// Opaque surface owned by one route.
///
/// `MaterialPageRoute` does not paint a background by itself, so pages used to
/// rely on the shell material behind the navigator. Ink effects (tile colours,
/// hover and pressed highlights) attach to the nearest material ancestor, so
/// they were painted on that shared shell material instead of the page: after
/// pushing another route they stayed visible, fixed to the window, above the
/// new page. Wrapping every route in this surface keeps both the background
/// and the ink inside the route that owns them, and clips ink to the page
/// bounds.
class AppPageSurface extends StatelessWidget {
  const AppPageSurface({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: context.tokens.bg,
      clipBehavior: Clip.hardEdge,
      child: child,
    );
  }
}
