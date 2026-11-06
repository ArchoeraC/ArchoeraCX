import 'dart:convert';
import 'dart:io';

import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/shell.dart';
import 'package:archoera_cx/pages/dashboard_page.dart';
import 'package:archoera_cx/pages/instances_page.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// 1×1 transparent PNG, enough for `FileImage` to decode in widget tests.
final _pixelPng = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8A'
  'AQUBAScY42YAAAAASUVORK5CYII=',
);

Future<void> _pump(
  WidgetTester tester,
  Map<String, Object> preferences,
) async {
  SharedPreferences.setMockInitialValues(preferences);
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

/// Pumps the app on a wallpaper: that is the mode whose transition reveals the
/// outgoing page instead of covering it.
Future<void> pumpApp(WidgetTester tester) async {
  final directory = Directory.systemTemp.createTempSync('archoera-transition');
  final image = File('${directory.path}/wall.png')
    ..writeAsBytesSync(_pixelPng);
  addTearDown(() => directory.deleteSync(recursive: true));
  await _pump(tester, <String, Object>{
    'ui.appearanceStyle': 'image',
    'ui.backgroundImage': image.path,
  });
}

/// Pumps the app on the solid canvas (the default appearance).
Future<void> pumpSolidApp(WidgetTester tester) =>
    _pump(tester, const <String, Object>{});

/// Halfway through a 320ms transition, after the branch change has been
/// applied but before the animation finishes.
Future<void> pumpMidTransition(WidgetTester tester) async {
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 160));
}

void main() {
  testWidgets('leaving home tiles the two pages over the wallpaper', (
    tester,
  ) async {
    await pumpApp(tester);
    final content = tester.getRect(find.byType(DashboardPage));

    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await pumpMidTransition(tester);

    final snapshot = tester.getRect(find.byKey(pageTransitionSnapshotKey));
    final incoming = tester.getRect(find.byType(InstancesPage));

    // The dashboard and the branch page travel one viewport in lockstep: the
    // snapshot leaves from above while the branch page arrives from below, and
    // their edges stay adjacent so the wallpaper never shows through a seam.
    expect(snapshot.top, lessThan(content.top));
    expect(incoming.top, greaterThan(content.top));
    expect(incoming.top, closeTo(snapshot.bottom, 1.0));

    await tester.pumpAndSettle();
    expect(find.byKey(pageTransitionSnapshotKey), findsNothing);
    expect(tester.getRect(find.byType(InstancesPage)).top, content.top);
    expect(tester.getRect(find.byType(InstancesPage)).left, content.left);
  });

  testWidgets('returning home tiles home above the leaving page', (tester) async {
    await pumpApp(tester);
    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await tester.pumpAndSettle();
    final content = tester.getRect(find.byType(InstancesPage));

    await tester.tap(find.byKey(const ValueKey('nav-/')));
    await pumpMidTransition(tester);

    final snapshot = tester.getRect(find.byKey(pageTransitionSnapshotKey));
    final incoming = tester.getRect(find.byType(DashboardPage));

    // Same motion downwards, still tiled: home arrives from above and the
    // branch snapshot leaves below it.
    expect(snapshot.top, greaterThan(content.top));
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

  testWidgets('an interrupted switch releases only the first snapshot', (
    tester,
  ) async {
    await pumpApp(tester);
    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await pumpMidTransition(tester);

    // Second switch while the first dissolve is still running: its snapshot is
    // replaced and must be released exactly once.
    await tester.tap(find.byKey(const ValueKey('nav-/stats')));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 80));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(find.byKey(pageTransitionSnapshotKey), findsNothing);
  });

  testWidgets('unmounting mid transition releases the snapshot once', (
    tester,
  ) async {
    await pumpApp(tester);
    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await pumpMidTransition(tester);

    // Tearing the tree down mid dissolve must release the adopted snapshot
    // exactly once: a second `dispose` trips the `ui.Image` assertion.
    await tester.pumpWidget(const SizedBox.shrink());
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
  });

  testWidgets('a solid canvas covers instead of sliding', (tester) async {
    await pumpSolidApp(tester);
    final dashboard = tester.getRect(find.byType(DashboardPage));

    // Entering a branch: the dashboard snapshot stays put and the branch page
    // rises over it, so the opaque canvas is never moved away from the window.
    await tester.tap(find.byKey(const ValueKey('nav-/instances')));
    await pumpMidTransition(tester);
    final coverSnapshot = tester.getRect(
      find.byKey(pageTransitionSnapshotKey),
    );
    final entering = tester.getRect(find.byType(InstancesPage));
    expect(coverSnapshot.top, dashboard.top);
    expect(coverSnapshot.left, dashboard.left);
    expect(entering.top, greaterThan(dashboard.top));

    await tester.pumpAndSettle();

    // Returning home: the branch page stays and the dashboard slides down over
    // it, which is the pre-wallpaper direction.
    final branch = tester.getRect(find.byType(InstancesPage));
    await tester.tap(find.byKey(const ValueKey('nav-/')));
    await pumpMidTransition(tester);
    final retractSnapshot = tester.getRect(
      find.byKey(pageTransitionSnapshotKey),
    );
    final home = tester.getRect(find.byType(DashboardPage));
    expect(retractSnapshot.top, greaterThan(branch.top));
    expect(home.top, branch.top);

    await tester.pumpAndSettle();
    expect(tester.getRect(find.byType(DashboardPage)).top, dashboard.top);
  });
}
