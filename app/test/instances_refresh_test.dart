import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/pages/download_page.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Boots the real shell and opens the instances branch at a desktop size.
Future<void> _openInstances(WidgetTester tester) async {
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
  await tester.tap(find.byKey(const ValueKey('nav-/instances')));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('instances page has a manual refresh action', (tester) async {
    await _openInstances(tester);

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

  testWidgets('opens the action menu on a secondary click', (tester) async {
    await _openInstances(tester);

    await tester.tap(
      find.text('1.21.4 · Fabric'),
      buttons: kSecondaryButton,
      kind: PointerDeviceKind.mouse,
    );
    await tester.pumpAndSettle();

    // The same entries the "more" button shows are now reachable directly.
    expect(find.text('Duplicate instance'), findsOneWidget);
    expect(find.text('Manage mods'), findsOneWidget);
    expect(find.text('Delete instance'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('sends the create action to the download catalogue', (
    tester,
  ) async {
    await _openInstances(tester);

    await tester.tap(find.widgetWithText(FilledButton, 'Create instance'));
    await tester.pumpAndSettle();

    // Creating starts by choosing a game version on the download page.
    expect(find.byType(DownloadPage), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
