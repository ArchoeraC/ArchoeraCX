import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/theme/tokens.dart';
import 'package:archoera_cx/widgets/layout/app_background.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget harness({required Widget child, bool dark = true}) {
  return MaterialApp(
    theme: dark ? AppTheme.dark : AppTheme.light,
    home: AppBackground(child: child),
  );
}

Finder canvas(Color color) => find.byWidgetPredicate(
  (widget) => widget is ColoredBox && widget.color == color,
);

void main() {
  testWidgets('paints one opaque canvas colour behind the child', (
    tester,
  ) async {
    await tester.pumpWidget(
      harness(child: const Center(child: Text('page content'))),
    );

    expect(find.text('page content'), findsOneWidget);
    expect(canvas(AppTokens.dark.bg), findsOneWidget);
    expect(tester.getSize(find.byType(AppBackground)), const Size(800, 600));

    // No gradients or shader layers: the background is exactly one opaque
    // colour.
    expect(find.byType(ShaderMask), findsNothing);
    expect(
      find.byWidgetPredicate(
        (widget) =>
            widget is DecoratedBox &&
            widget.decoration is BoxDecoration &&
            (widget.decoration as BoxDecoration).gradient != null,
      ),
      findsNothing,
    );
    expect(tester.binding.transientCallbackCount, 0);
  });

  testWidgets('follows the light theme token', (tester) async {
    await tester.pumpWidget(
      harness(dark: false, child: const SizedBox.expand()),
    );

    expect(canvas(AppTokens.light.bg), findsOneWidget);
    expect(canvas(AppTokens.dark.bg), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
