import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  testWidgets('instances page has a manual refresh action', (tester) async {
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
    await tester.tap(find.byIcon(Icons.grid_view_outlined));
    await tester.pumpAndSettle();

    final refresh = find.widgetWithText(OutlinedButton, 'Refresh');
    expect(refresh, findsOneWidget);

    await tester.tap(refresh);
    await tester.pumpAndSettle();

    // The refresh runs instance.sync and confirms it through the global toast.
    expect(find.text('Instance folders synced.'), findsOneWidget);
    expect(tester.takeException(), isNull);

    // Let the toast timer expire before teardown.
    await tester.pump(const Duration(seconds: 4));
  });
}
