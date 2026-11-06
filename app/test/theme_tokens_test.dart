import 'package:archoera_cx/theme/tokens.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('image mode swaps the hover plate for a light wash', () {
    final solid = AppTokens.dark.withAccent(
      AppTokens.brandAccent,
      brightness: Brightness.dark,
    );
    expect(solid.hoverFill, solid.surface2);
    expect(solid.pressedFill, isNull);

    final image = AppTokens.dark.withAccent(
      AppTokens.brandAccent,
      brightness: Brightness.dark,
      imageBackground: true,
    );
    // An 0.86-alpha surface2 reads as a solid block on the wallpaper, so rows
    // and tiles borrow the command bar's foreground wash instead.
    expect(image.hoverFill.a, closeTo(0.10, 0.001));
    expect(image.hoverFill, isNot(image.surface2));
    expect(image.pressedFill?.a, closeTo(0.16, 0.001));
  });
}
