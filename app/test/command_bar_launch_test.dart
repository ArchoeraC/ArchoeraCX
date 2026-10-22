import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

Future<void> pumpApp(WidgetTester tester) async {
  SharedPreferences.setMockInitialValues(<String, Object>{});
  tester.view.physicalSize = const Size(1380, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [coreClientProvider.overrideWithValue(null)],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('launch action animates in and out with the command bar', (
    tester,
  ) async {
    await pumpApp(tester);

    final launchAction = find.byKey(const ValueKey('command-bar-launch'));
    // The home bar has no launch action.
    expect(launchAction, findsNothing);

    await tester.tap(find.byIcon(Icons.grid_view_outlined));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 80));

    // Mid-transition the action is still animating instead of popping in.
    final transitions = tester.widgetList<SizeTransition>(
      find.byType(SizeTransition),
    );
    expect(
      transitions.any((widget) => widget.sizeFactor.value > 0),
      isTrue,
      reason: 'the launch action should grow in, not appear instantly',
    );

    await tester.pumpAndSettle();
    expect(launchAction, findsOneWidget);

    await tester.tap(find.byIcon(Icons.space_dashboard_outlined));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 80));
    expect(tester.takeException(), isNull);

    await tester.pumpAndSettle();
    expect(launchAction, findsNothing);
  });

  testWidgets('hovering the launch action names the selected instance', (
    tester,
  ) async {
    await pumpApp(tester);
    await tester.tap(find.byIcon(Icons.grid_view_outlined));
    await tester.pumpAndSettle();

    final launchAction = find.byKey(const ValueKey('command-bar-launch'));
    expect(launchAction, findsOneWidget);

    // The tooltip carries the instance that will launch.
    final tooltip = tester.widget<Tooltip>(
      find.descendant(of: launchAction, matching: find.byType(Tooltip)),
    );
    expect(tooltip.message, 'Launch “1.21.4 · Fabric”');

    // Hovering reveals it without clicking.
    final mouse = await tester.createGesture(kind: PointerDeviceKind.mouse);
    await mouse.addPointer(location: Offset.zero);
    addTearDown(mouse.removePointer);
    await tester.pump();
    await mouse.moveTo(tester.getCenter(launchAction));
    await tester.pump(const Duration(seconds: 2));

    expect(find.text('Launch “1.21.4 · Fabric”'), findsOneWidget);
  });
}
