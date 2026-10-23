import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/router.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// The "share loader files" switch is the global default the Forge/NeoForge
/// install dialog starts from, so it has to exist, flip and persist in the
/// launch section.
Future<void> _pumpSettings(
  WidgetTester tester, {
  Size size = const Size(1380, 900),
}) async {
  SharedPreferences.setMockInitialValues(<String, Object>{});
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        routerProvider.overrideWithValue(
          buildAppRouter(initialLocation: '/settings'),
        ),
      ],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();
  await tester.tap(find.text('Launch').first);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('the launch section exposes the loader sharing switch', (
    tester,
  ) async {
    await _pumpSettings(tester);

    final label = find.text('Share loader files');
    expect(label, findsOneWidget);

    // The launch section also offers the game optimization switch.
    expect(find.text('Game optimization'), findsOneWidget);
    expect(
      tester
          .widget<Switch>(
            find.byKey(const ValueKey('settings-game-optimization')),
          )
          .value,
      isTrue,
    );

    final toggle = find.byKey(const ValueKey('settings-share-loader-files'));
    expect(tester.widget<Switch>(toggle).value, isTrue);

    await tester.ensureVisible(toggle);
    await tester.tap(toggle);
    await tester.pumpAndSettle();

    expect(tester.widget<Switch>(toggle).value, isFalse);
    expect(tester.takeException(), isNull);
  });

  testWidgets('the switch fits a narrow window', (tester) async {
    await _pumpSettings(tester, size: const Size(720, 900));

    expect(find.text('Share loader files'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
