import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/appearance.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

Future<ProviderContainer> pumpShell(WidgetTester tester) async {
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
  return ProviderScope.containerOf(tester.element(find.byType(ArchoeraApp)));
}

Color? brandColor(WidgetTester tester) =>
    tester.widget<Text>(find.text('ArchoeraCX').first).style?.color;

void main() {
  testWidgets('top bar brand follows the accent color', (tester) async {
    final container = await pumpShell(tester);

    final initial = brandColor(tester);
    expect(initial, isNotNull);

    const accent = Color(0xFFFF7A45);
    container
        .read(appearanceProvider.notifier)
        .setAccentMode(AccentMode.custom);
    await container.read(appearanceProvider.notifier).setAccentColor(accent);
    await tester.pumpAndSettle();

    expect(brandColor(tester)?.toARGB32(), accent.toARGB32());
  });
}
