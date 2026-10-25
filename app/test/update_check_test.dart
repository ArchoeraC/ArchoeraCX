import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/utils/app_info.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues(<String, Object>{});
  });

  testWidgets('about panel offers an update check with the current version', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1440, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        // No core client: the entry point is visible but no version check is
        // issued, so the UI never reaches the network on its own.
        overrides: [coreClientProvider.overrideWithValue(null)],
        child: const ArchoeraApp(),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Settings'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('About').first);
    await tester.pumpAndSettle();

    // The update check is the last block in About, below the legal disclaimer.
    expect(find.text('Check for updates'), findsOneWidget);
    expect(
      find.textContaining('Compares against the newest GitHub release'),
      findsOneWidget,
    );
    expect(find.text('Current version'), findsOneWidget);
    expect(find.text(AppInfo.version), findsWidgets);
    expect(
      tester.getTopLeft(find.text('Check for updates')).dy,
      greaterThan(
        tester.getTopLeft(find.textContaining('INDEPENDENT LAUNCHER')).dy,
      ),
    );
    expect(tester.takeException(), isNull);
  });
}
