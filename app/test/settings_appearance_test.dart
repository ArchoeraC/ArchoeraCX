import 'dart:convert';
import 'dart:io';

import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/appearance.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/widgets/common/app_slider.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// 1×1 transparent PNG, enough for `FileImage` to decode in widget tests.
final _pixelPng = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8A'
  'AQUBAScY42YAAAAASUVORK5CYII=',
);

Future<ProviderContainer> pumpSettings(
  WidgetTester tester, {
  Size size = const Size(1380, 900),
  Map<String, Object> preferences = const <String, Object>{},
  Future<double?> Function(String path)? luminanceSampler,
}) async {
  SharedPreferences.setMockInitialValues(preferences);
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        if (luminanceSampler != null)
          wallpaperLuminanceProvider.overrideWithValue(luminanceSampler),
      ],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();
  final container = ProviderScope.containerOf(
    tester.element(find.byType(ArchoeraApp)),
  );
  await tester.tap(find.byKey(const ValueKey('nav-/settings')));
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

    expect(container.read(appearanceProvider).themeMode, ThemeMode.dark);
    expect(container.read(appearanceProvider).accentMode, AccentMode.system);
    expect(tester.takeException(), isNull);

    // Let the confirmation toast expire before the test ends.
    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('appearance groups stack in one column on wide windows', (
    tester,
  ) async {
    await pumpSettings(tester);

    // Wide windows used to split the section into two columns; the groups now
    // follow the same single-column rhythm as storage/java/about.
    final theme = tester.getTopLeft(find.text('Theme'));
    final accent = tester.getTopLeft(find.text('Accent'));
    final interface = tester.getTopLeft(find.text('Interface'));
    final language = tester.getTopLeft(find.text('Language & region'));

    expect(accent.dy, greaterThan(theme.dy));
    expect(interface.dy, greaterThan(accent.dy));
    expect(language.dy, greaterThan(interface.dy));
    expect(accent.dx, theme.dx);
    expect(interface.dx, theme.dx);
    expect(language.dx, theme.dx);
    expect(tester.takeException(), isNull);
  });

  testWidgets('background sliders use a fixed column on wide windows', (
    tester,
  ) async {
    final directory = Directory.systemTemp.createTempSync('archoera-slider');
    final image = File('${directory.path}/wall.png')
      ..writeAsBytesSync(_pixelPng);
    addTearDown(() => directory.deleteSync(recursive: true));

    await pumpSettings(
      tester,
      preferences: <String, Object>{
        'ui.appearanceStyle': 'image',
        'ui.backgroundImage': image.path,
      },
    );
    await tester.ensureVisible(find.byType(AppSlider).first);
    await tester.pumpAndSettle();

    final rects = [
      for (final element in find.byType(AppSlider).evaluate())
        tester.getRect(find.byWidget(element.widget)),
    ];
    expect(rects, hasLength(3));
    // The track no longer runs the full row width: every control keeps the
    // same 320 px column and the same right edge.
    for (final rect in rects) {
      expect(rect.width, 320);
      expect(rect.right, closeTo(rects.first.right, 0.5));
    }
    // Range labels are printed under the track.
    expect(find.text('80'), findsOneWidget);
    expect(find.text('90%'), findsOneWidget);
    expect(find.text('2.0x'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a bright wallpaper raises the scrim floor', (tester) async {
    final directory = Directory.systemTemp.createTempSync('archoera-dim');
    final image = File('${directory.path}/wall.png')
      ..writeAsBytesSync(_pixelPng);
    addTearDown(() => directory.deleteSync(recursive: true));

    final container = await pumpSettings(
      tester,
      preferences: <String, Object>{
        'ui.appearanceStyle': 'image',
        'ui.backgroundImage': image.path,
        'ui.backgroundDim': 0.4,
      },
      luminanceSampler: (_) async => 1.0,
    );
    await tester.pumpAndSettle();

    final appearance = container.read(appearanceProvider);
    expect(appearance.backgroundDimFloor, closeTo(0.55, 0.01));
    expect(appearance.effectiveBackgroundDim, closeTo(0.55, 0.01));

    // The dim slider starts at the measured floor, so the user sees why it
    // cannot go lower instead of watching the scrim refuse to move.
    final slider = tester.widget<AppSlider>(find.byType(AppSlider).at(1));
    expect(slider.min, closeTo(0.55, 0.01));
    expect(slider.value, closeTo(0.55, 0.01));

    // A value below the floor is clamped; the stored intent never goes under.
    await container.read(appearanceProvider.notifier).setBackgroundDim(0.32);
    expect(
      container.read(appearanceProvider).backgroundDim,
      closeTo(0.55, 0.01),
    );
  });

  testWidgets('a dark wallpaper keeps the default scrim floor', (tester) async {
    final directory = Directory.systemTemp.createTempSync('archoera-dim');
    final image = File('${directory.path}/wall.png')
      ..writeAsBytesSync(_pixelPng);
    addTearDown(() => directory.deleteSync(recursive: true));

    final container = await pumpSettings(
      tester,
      preferences: <String, Object>{
        'ui.appearanceStyle': 'image',
        'ui.backgroundImage': image.path,
        'ui.backgroundDim': 0.4,
      },
      luminanceSampler: (_) async => 0.1,
    );
    await tester.pumpAndSettle();

    final appearance = container.read(appearanceProvider);
    expect(appearance.backgroundDimFloor, closeTo(0.3, 0.001));
    expect(appearance.effectiveBackgroundDim, closeTo(0.4, 0.001));
  });

  testWidgets('widget tests never sample the wallpaper file', (tester) async {
    final directory = Directory.systemTemp.createTempSync('archoera-sample');
    final image = File('${directory.path}/wall.png')
      ..writeAsBytesSync(_pixelPng);
    addTearDown(() => directory.deleteSync(recursive: true));

    final container = await pumpSettings(
      tester,
      preferences: <String, Object>{
        'ui.appearanceStyle': 'image',
        'ui.backgroundImage': image.path,
      },
    );

    // Reading the file inside the fake-async zone keeps the handle open past
    // teardown, which fails the delete on Windows. The default sampler must
    // stay inert; real measurements come from an explicit override.
    final sampler = container.read(wallpaperLuminanceProvider);
    expect(await tester.runAsync(() => sampler(image.path)), isNull);
    expect(container.read(appearanceProvider).backgroundDimFloor, 0.3);
  });

  testWidgets('appearance options fit a narrow window', (tester) async {
    final directory = Directory.systemTemp.createTempSync('archoera-slider');
    final image = File('${directory.path}/wall.png')
      ..writeAsBytesSync(_pixelPng);
    addTearDown(() => directory.deleteSync(recursive: true));

    await pumpSettings(
      tester,
      size: const Size(720, 800),
      preferences: <String, Object>{
        'ui.appearanceStyle': 'image',
        'ui.backgroundImage': image.path,
      },
    );

    expect(find.text('Theme'), findsOneWidget);
    expect(find.text('Interface density'), findsOneWidget);

    // Narrow panels stack the control under the text instead of squeezing the
    // fixed column, so the track uses the row width again.
    await tester.ensureVisible(find.byType(AppSlider).first);
    await tester.pumpAndSettle();
    final rect = tester.getRect(find.byType(AppSlider).first);
    expect(rect.width, greaterThan(320));
    expect(tester.takeException(), isNull);
  });
}
