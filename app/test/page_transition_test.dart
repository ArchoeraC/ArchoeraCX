import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/shell.dart';
import 'package:archoera_cx/pages/dashboard_page.dart';
import 'package:archoera_cx/pages/instances_page.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

Future<void> pumpApp(WidgetTester tester) async {
  SharedPreferences.setMockInitialValues(<String, Object>{});
  tester.view.physicalSize = const Size(1440, 900);
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

/// Halfway through a 320ms transition, after the branch change has been
/// applied but before the animation finishes.
Future<void> pumpMidTransition(WidgetTester tester) async {
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 160));
}

void main() {
  testWidgets('leaving home pushes the old and new pages together', (
    tester,
  ) async {
    await pumpApp(tester);
    final content = tester.getRect(find.byType(DashboardPage));

    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await pumpMidTransition(tester);

    final snapshot = tester.getRect(find.byKey(pageTransitionSnapshotKey));
    final incoming = tester.getRect(find.byType(InstancesPage));

    // The outgoing snapshot is still on screen, shifted above the content
    // area instead of staying put under a page that covers it.
    expect(snapshot.top, lessThan(content.top));
    expect(snapshot.bottom, greaterThan(content.top));
    // The incoming page has not arrived yet and starts exactly where the
    // snapshot ends: the two tiles slide in lockstep with no gap.
    expect(incoming.top, greaterThan(content.top));
    expect(incoming.top, closeTo(snapshot.bottom, 1.0));
    expect(incoming.bottom, greaterThan(content.bottom));

    await tester.pumpAndSettle();
    expect(find.byKey(pageTransitionSnapshotKey), findsNothing);
    expect(tester.getRect(find.byType(InstancesPage)).top, content.top);
    expect(tester.getRect(find.byType(InstancesPage)).left, content.left);
  });

  testWidgets('returning home pushes the old page down under it', (
    tester,
  ) async {
    await pumpApp(tester);
    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await tester.pumpAndSettle();
    final content = tester.getRect(find.byType(InstancesPage));

    await tester.tap(find.byKey(const ValueKey('nav-/')));
    await pumpMidTransition(tester);

    final snapshot = tester.getRect(find.byKey(pageTransitionSnapshotKey));
    final incoming = tester.getRect(find.byType(DashboardPage));

    expect(snapshot.top, greaterThan(content.top));
    expect(snapshot.bottom, greaterThan(content.bottom));
    // Home enters from the top edge, immediately above the leaving page.
    expect(incoming.top, lessThan(content.top));
    expect(incoming.bottom, closeTo(snapshot.top, 1.0));

    await tester.pumpAndSettle();
    expect(find.byKey(pageTransitionSnapshotKey), findsNothing);
    expect(tester.getRect(find.byType(DashboardPage)).top, content.top);
    expect(tester.getRect(find.byType(DashboardPage)).left, content.left);
  });

  testWidgets('switches between other pages keep the dissolve transition', (
    tester,
  ) async {
    await pumpApp(tester);
    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await tester.pumpAndSettle();
    final content = tester.getRect(find.byType(InstancesPage));

    await tester.tap(find.byKey(const ValueKey('nav-/stats')));
    await pumpMidTransition(tester);

    // The dissolve lifts the incoming page in place; nothing slides out of
    // the content area, so no layer is left above or below it.
    final snapshot = tester.getRect(find.byKey(pageTransitionSnapshotKey));
    expect(snapshot, content);

    await tester.pumpAndSettle();
  });
}
