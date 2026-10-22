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

    // The Java section keeps the selection rows and memory options; the runtime
    // list lives on its own page, so neither the old five-row list nor the
    // "view all" dialog remains here.
    expect(find.text('Java runtimes'), findsOneWidget);
    expect(find.textContaining('Browse every scan result'), findsOneWidget);
    expect(find.text('Automatic (per game version)'), findsOneWidget);
    expect(find.textContaining('View all'), findsNothing);
    expect(find.text('Scan Java'), findsNothing);
    expect(find.text('System memory'), findsOneWidget);
    expect(find.text('Minimum memory (MB)'), findsOneWidget);
    expect(find.text('Additional game arguments'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('Java runtimes'));
    await tester.pumpAndSettle();

    expect(
      find.textContaining('Every Java runtime found on this machine'),
      findsOneWidget,
    );
    expect(find.byTooltip('Back'), findsOneWidget);
    expect(find.text('System memory'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
