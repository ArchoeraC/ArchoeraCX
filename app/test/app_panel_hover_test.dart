import 'package:archoera_cx/render/app_shaders.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/app_panel.dart';
import 'package:archoera_cx/widgets/common/hover_glow.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// Most tests deliberately run without an `AppShaderScope`, which is also the
/// production fallback on backends without runtime effects. The glow layer must
/// not exist in that case; the last test loads the real program to cover the
/// paint path.
Future<void> pumpPanel(
  WidgetTester tester, {
  VoidCallback? onTap,
  Widget Function(Widget panel)? wrap,
}) async {
  Widget panel = AppPanel(
    onTap: onTap,
    child: const SizedBox(height: 120, child: Text('panel content')),
  );
  panel = wrap == null ? panel : wrap(panel);
  await tester.pumpWidget(
    MaterialApp(
      debugShowCheckedModeBanner: false,
      theme: AppTheme.darkWith(reduceMotion: false),
      home: Scaffold(
        body: Center(child: SizedBox(width: 320, child: panel)),
      ),
    ),
  );
}

void main() {
  testWidgets('plain panel renders its child and no glow paint', (
    tester,
  ) async {
    await pumpPanel(tester, onTap: () {});

    expect(find.text('panel content'), findsOneWidget);
    expect(find.byType(InkWell), findsOneWidget);
    expect(find.byKey(appHoverGlowKey), findsNothing);
    expect(find.byType(CustomPaint), findsNothing);
  });

  testWidgets(
    'mouse hover without a program adds no layer and does not throw',
    (tester) async {
      await pumpPanel(tester, onTap: () {});

      final gesture = await tester.createGesture(kind: PointerDeviceKind.mouse);
      addTearDown(gesture.removePointer);
      await gesture.addPointer(location: Offset.zero);
      await tester.pump();

      await gesture.moveTo(tester.getCenter(find.byType(AppPanel)));
      await tester.pump(const Duration(milliseconds: 200));

      expect(tester.takeException(), isNull);
      expect(find.byKey(appHoverGlowKey), findsNothing);
      expect(find.byType(CustomPaint), findsNothing);
    },
  );

  testWidgets('tapping the panel still calls onTap', (tester) async {
    var taps = 0;
    await pumpPanel(tester, onTap: () => taps++);

    await tester.tap(find.byType(AppPanel));
    await tester.pump();

    expect(taps, 1);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a null program installs no pointer plumbing', (tester) async {
    await tester.pumpWidget(
      const Directionality(
        textDirection: TextDirection.ltr,
        child: AppHoverGlow(
          color: Color(0xFF4F7CFF),
          child: SizedBox(width: 80, height: 40),
        ),
      ),
    );

    expect(find.byType(MouseRegion), findsNothing);
    expect(find.byType(Listener), findsNothing);
    expect(find.byKey(appHoverGlowKey), findsNothing);
  });

  testWidgets('a loaded program paints one quad clipped to the panel', (
    tester,
  ) async {
    await pumpPanel(
      tester,
      onTap: () {},
      wrap: (panel) => AppShaderScope(child: panel),
    );
    // Loading is a microtask; one extra frame publishes the programs.
    await tester.pump();
    final program = AppShaderScope.of(tester.element(find.byType(AppPanel)))
        .glow;
    expect(program, isNotNull);

    final panelSize = tester.getSize(find.byType(AppPanel));
    expect(panelSize.width, 320);

    final gesture = await tester.createGesture(kind: PointerDeviceKind.mouse);
    addTearDown(gesture.removePointer);
    await gesture.addPointer(location: Offset.zero);
    await tester.pump();

    await gesture.moveTo(tester.getCenter(find.byType(AppPanel)));
    await tester.pump(const Duration(milliseconds: 200));

    expect(tester.takeException(), isNull);
    final glow = find.byKey(appHoverGlowKey);
    expect(glow, findsOneWidget);
    // Positioned.fill keeps the quad the size of the panel, and the panel's
    // ClipPath is still the ancestor that cuts the bevel.
    expect(tester.getSize(glow), panelSize);
    expect(
      find.ancestor(of: glow, matching: find.byType(ClipPath)),
      findsOneWidget,
    );

    await gesture.moveTo(Offset.zero);
    await tester.pump(const Duration(milliseconds: 300));
    await tester.pump();
    expect(find.byKey(appHoverGlowKey), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
