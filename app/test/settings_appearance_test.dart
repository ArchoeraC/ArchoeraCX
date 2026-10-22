import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/appearance.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

Future<ProviderContainer> pumpSettings(
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
      overrides: [coreClientProvider.overrideWithValue(null)],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();
  final container = ProviderScope.containerOf(
    tester.element(find.byType(ArchoeraApp)),
  );
  await tester.tap(find.byIcon(Icons.tune_outlined));
  await tester.pumpAndSettle();
  return container;
}

void main() {
  testWidgets('appearance section groups options without inner tabs', (
    tester,
  ) async {
    await pumpSettings(tester);

    // Groups replace the old personalization/language tab bar.
    expect(find.text('Theme'), findsOneWidget);
    expect(find.text('Accent'), findsOneWidget);
    expect(find.text('Interface'), findsOneWidget);
    expect(find.text('Language & region'), findsOneWidget);
    expect(find.text('Personalization'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('theme mode and accent mode update the appearance store', (
    tester,
  ) async {
    final container = await pumpSettings(tester);

    expect(find.text('Color mode'), findsOneWidget);
    await tester.tap(find.text('Dark'));
    await tester.pumpAndSettle();
    expect(container.read(appearanceProvider).themeMode, ThemeMode.dark);
    expect(
      tester.widget<MaterialApp>(find.byType(MaterialApp)).themeMode,
      ThemeMode.dark,
    );

    expect(find.text('Accent color'), findsOneWidget);
    await tester.tap(find.text('Custom'));
    await tester.pumpAndSettle();
    expect(container.read(appearanceProvider).accentMode, AccentMode.custom);
    // The preset strip and the custom picker replace the system hint.
    expect(find.text('#4F7CFF'), findsWidgets);
    expect(tester.takeException(), isNull);
  });

  testWidgets('reset restores the default appearance', (tester) async {
    final container = await pumpSettings(tester);
    await tester.tap(find.text('Dark'));
    await tester.pumpAndSettle();
    expect(container.read(appearanceProvider).themeMode, ThemeMode.dark);

    await tester.ensureVisible(find.text('Reset appearance'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Reset appearance'));
    await tester.pumpAndSettle();

    expect(container.read(appearanceProvider).themeMode, ThemeMode.system);
    expect(container.read(appearanceProvider).accentMode, AccentMode.system);
    expect(tester.takeException(), isNull);

    // Let the confirmation toast expire before the test ends.
    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('appearance options fit a narrow window', (tester) async {
    await pumpSettings(tester, size: const Size(720, 800));

    expect(find.text('Theme'), findsOneWidget);
    expect(find.text('Interface density'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
