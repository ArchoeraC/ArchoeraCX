import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/shell.dart';
import 'package:archoera_cx/render/dissolve_mask.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues(<String, Object>{});
  });

  Future<void> pumpSettings(WidgetTester tester, Size size) async {
    tester.view.physicalSize = size;
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

    final router = GoRouter.of(tester.element(find.byType(AppShell)));
    router.go('/settings');
    await tester.pumpAndSettle();
  }

  testWidgets('section switch fades without layout errors', (tester) async {
    await pumpSettings(tester, const Size(1440, 900));

    await tester.tap(find.widgetWithText(ListTile, 'Java'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 60));
    expect(tester.takeException(), isNull);

    await tester.pumpAndSettle();
    expect(find.text('Java runtimes'), findsOneWidget);

    // The switched section must keep the full content width instead of
    // shrinking to the widest child inside the switcher's stack.
    final row = find.widgetWithText(ListTile, 'Java runtimes');
    expect(tester.getSize(row).width, greaterThan(600));
  });

  testWidgets('section switch fades on the tab layout', (tester) async {
    await pumpSettings(tester, const Size(700, 800));

    // The tab strip scrolls horizontally; bring Java into view first.
    await tester.ensureVisible(find.text('Java'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Java'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 60));
    expect(tester.takeException(), isNull);

    await tester.pumpAndSettle();
    expect(find.text('Java runtimes'), findsOneWidget);
  });

  testWidgets('section switch dissolves a snapshot and drops the old subtree', (
    tester,
  ) async {
    await pumpSettings(tester, const Size(1440, 900));
    // The appearance section is the default and owns this control.
    expect(find.byKey(const ValueKey('start-page-dropdown')), findsOneWidget);

    await tester.tap(find.widgetWithText(ListTile, 'Java'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 40));

    // The outgoing section must be a GPU texture, not a second live subtree:
    // otherwise both dense panels are laid out and painted during the switch.
    expect(find.byType(AppDissolveMask), findsOneWidget);
    expect(find.byKey(const ValueKey('start-page-dropdown')), findsNothing);
    expect(tester.takeException(), isNull);

    await tester.pumpAndSettle();
    expect(find.byType(AppDissolveMask), findsNothing);
    expect(find.text('Java runtimes'), findsOneWidget);
  });
}
