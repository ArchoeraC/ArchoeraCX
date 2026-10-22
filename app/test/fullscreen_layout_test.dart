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
  await tester.tap(find.byIcon(Icons.grid_view_outlined));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('fullscreen widens the centered content column', (tester) async {
    await _pumpInstances(tester, const Size(1920, 1080));

    final headerX = tester
        .getTopLeft(
          find.text('Isolated game directories with shared libraries and assets.'),
        )
        .dx;
    // 1640 px column inside 1920 px + 32 px page padding.
    expect(headerX, greaterThan(120));

    // The command bar contents follow the same column: the first nav item is
    // no longer glued to the window edge.
    final navX = tester.getTopLeft(find.text('Dashboard')).dx;
    expect(navX, greaterThan(120));
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
