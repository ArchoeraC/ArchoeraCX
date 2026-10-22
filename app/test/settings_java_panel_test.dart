import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues(<String, Object>{});
  });

  testWidgets('settings java panel keeps runtimes and memory together', (
    tester,
  ) async {
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
    await tester.tap(find.text('Settings'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Java').last);
    await tester.pumpAndSettle();

    // The Java section holds the selection rows, the tiled runtime list and the
    // memory options; the old "view all" dialog is gone.
    expect(find.text('Discovered Java'), findsOneWidget);
    expect(find.text('Scan Java'), findsOneWidget);
    expect(find.textContaining('View all'), findsNothing);
    expect(find.text('System memory'), findsOneWidget);
    expect(find.text('Minimum memory (MB)'), findsOneWidget);
    expect(find.text('Additional game arguments'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
