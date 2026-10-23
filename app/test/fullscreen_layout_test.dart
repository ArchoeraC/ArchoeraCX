import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

Future<void> _pumpInstances(WidgetTester tester, Size size) async {
  SharedPreferences.setMockInitialValues(<String, Object>{});
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
  await tester.tap(find.byKey(const ValueKey('nav-/instances')));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('fullscreen uses the full window width', (tester) async {
    await _pumpInstances(tester, const Size(1920, 1080));

    final headerX = tester
        .getTopLeft(
          find.text('Isolated game directories with shared libraries and assets.'),
        )
        .dx;
    // Full-bleed content: only the shell padding (32 px) separates it from the
    // window edge.
    expect(headerX, lessThan(60));

    // The command bar follows the same padding instead of a narrower column.
    final navX = tester.getTopLeft(find.text('Dashboard')).dx;
    expect(navX, lessThan(120));

    // Cards flow from the left edge; no centred island with big side gaps.
    final firstCard = tester.getTopLeft(find.text('Launch').first);
    expect(firstCard.dx, lessThan(900));
    expect(tester.takeException(), isNull);
  });

  testWidgets('fullscreen instances use a card grid', (tester) async {
    await _pumpInstances(tester, const Size(1920, 1080));

    final launches = find.text('Launch');
    expect(launches.evaluate().length, greaterThanOrEqualTo(3));
    final rows = <double>{};
    for (var index = 0; index < 3; index++) {
      rows.add(tester.getRect(launches.at(index)).top);
    }
    // Grid cards sit on the same row; a list would stack them vertically.
    expect(rows.length, lessThan(3));
    expect(tester.takeException(), isNull);
  });

  testWidgets('narrow instances stay a single list', (tester) async {
    await _pumpInstances(tester, const Size(900, 800));

    final launches = find.text('Launch');
    final rows = <double>{};
    for (var index = 0; index < 3; index++) {
      rows.add(tester.getRect(launches.at(index)).top);
    }
    expect(rows.length, 3);
    expect(tester.takeException(), isNull);
  });
}
