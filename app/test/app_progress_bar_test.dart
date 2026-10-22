import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/app_progress_bar.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Future<void> pumpBar(WidgetTester tester, Widget bar) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: AppTheme.darkWith(
        accent: const Color(0xFF4F7CFF),
        globalTint: false,
        reduceMotion: false,
        density: VisualDensity.standard,
      ),
      home: Scaffold(
        body: Center(child: SizedBox(width: 240, child: bar)),
      ),
    ),
  );
  await tester.pump();
}

void main() {
  testWidgets('determinate bar exposes its percentage to semantics', (
    tester,
  ) async {
    final handle = tester.ensureSemantics();
    await pumpBar(tester, const AppProgressBar(value: 0.42, height: 6));

    expect(find.byType(AppProgressBar), findsOneWidget);
    expect(tester.getSemantics(find.byType(AppProgressBar)).value, '42%');
    handle.dispose();
  });

  testWidgets('indeterminate bar keeps animating without a value', (
    tester,
  ) async {
    await pumpBar(tester, const AppProgressBar(height: 6));
    expect(tester.takeException(), isNull);
    await tester.pump(const Duration(milliseconds: 700));
    await tester.pump(const Duration(milliseconds: 700));
    expect(tester.takeException(), isNull);
  });

  testWidgets('reduced motion does not animate', (tester) async {
    tester.platformDispatcher.accessibilityFeaturesTestValue =
        const FakeAccessibilityFeatures(disableAnimations: true);
    addTearDown(tester.platformDispatcher.clearAccessibilityFeaturesTestValue);

    await pumpBar(tester, const AppProgressBar(height: 6));
    await tester.pump(const Duration(seconds: 2));
    expect(tester.takeException(), isNull);
  });
}
