import 'dart:convert';
import 'dart:io';

import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/appearance.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/theme/tokens.dart';
import 'package:archoera_cx/widgets/common/app_panel.dart';
import 'package:archoera_cx/widgets/layout/image_background.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// 1×1 transparent PNG, enough for `FileImage` to decode in widget tests.
final _pixelPng = base64Decode(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8A'
  'AQUBAScY42YAAAAASUVORK5CYII=',
);

Future<ProviderContainer> _pumpApp(
  WidgetTester tester, {
  Size size = const Size(1440, 900),
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
  return ProviderScope.containerOf(tester.element(find.byType(ArchoeraApp)));
}

void main() {
  test('image style only activates once a path exists', () {
    const solid = AppearanceSettings();
    expect(solid.hasImageBackground, isFalse);

    final image = solid.copyWith(
      appearanceStyle: AppearanceStyle.image,
      backgroundImage: '/tmp/wallpaper.png',
    );
    expect(image.hasImageBackground, isTrue);

    final noFile = image.copyWith(clearBackgroundImage: true);
    expect(noFile.backgroundImage, isNull);
    expect(noFile.hasImageBackground, isFalse);
    expect(noFile.appearanceStyle, AppearanceStyle.image);
  });

  test('image theme keeps light text and a transparent canvas', () {
    final theme = AppTheme.darkWith(imageBackground: true);
    final tokens = theme.extension<AppTokens>()!;
    expect(tokens.imageBackground, isTrue);
    expect(tokens.bg.a, 0);
    expect(tokens.textPrimary, AppTokens.dark.textPrimary);
    expect(tokens.surface1.a, lessThan(1));
    expect(tokens.surface1.a, greaterThan(0.9));

    // Menus, dialogs and inputs float over the wallpaper, so their theme
    // fills have to stay translucent as well.
    expect(theme.popupMenuTheme.color!.a, lessThan(1));
    expect(theme.dialogTheme.backgroundColor!.a, lessThan(1));
    expect(theme.inputDecorationTheme.fillColor!.a, lessThan(0.7));
  });

  testWidgets('image mode frosts panels so the wallpaper reads through', (
    tester,
  ) async {
    Widget harness(ThemeData theme) => MaterialApp(
      theme: theme,
      home: Scaffold(body: AppPanel(child: const Text('card'))),
    );

    await tester.pumpWidget(harness(AppTheme.darkWith(imageBackground: true)));
    expect(find.byType(BackdropFilter), findsOneWidget);

    await tester.pumpWidget(harness(AppTheme.dark));
    // MaterialApp cross-fades theme changes; let it settle first.
    await tester.pumpAndSettle();
    expect(find.byType(BackdropFilter), findsNothing);
  });

  testWidgets('image mode forces dark colors and disables theme switches', (
    tester,
  ) async {
    final file = File(
      '${Directory.systemTemp.path}/archoera-wallpaper-'
      '${DateTime.now().microsecondsSinceEpoch}.png',
    );
    file.writeAsBytesSync(_pixelPng);
    addTearDown(() {
      if (file.existsSync()) file.deleteSync();
    });

    final container = await _pumpApp(tester);
    final notifier = container.read(appearanceProvider.notifier);
    await notifier.setThemeMode(ThemeMode.light);
    await notifier.setAppearanceStyle(AppearanceStyle.image);
    await notifier.setBackgroundImage(file.path);
    await tester.pumpAndSettle();

    expect(container.read(appearanceProvider).hasImageBackground, isTrue);
    expect(
      tester.widget<MaterialApp>(find.byType(MaterialApp)).themeMode,
      ThemeMode.dark,
    );
    expect(find.byType(ImageBackground), findsOneWidget);

    await tester.tap(find.byKey(const ValueKey('nav-/settings')));
    await tester.pumpAndSettle();

    expect(find.text('Background'), findsOneWidget);
    expect(find.text('Background style'), findsOneWidget);
    expect(find.text('Color mode'), findsOneWidget);
    // The global tint switch is forced on and cannot be changed.
    final switches = tester.widgetList<Switch>(find.byType(Switch)).toList();
    expect(
      switches.any((item) => item.value && item.onChanged == null),
      isTrue,
      reason: 'image mode must force the global tint switch on and disable it',
    );

    // Removing the image restores the user's stored light mode.
    await notifier.setBackgroundImage(null);
    await tester.pumpAndSettle();
    expect(
      tester.widget<MaterialApp>(find.byType(MaterialApp)).themeMode,
      ThemeMode.light,
    );
    expect(find.byType(ImageBackground), findsNothing);
  });

  testWidgets('image controls fit a narrow window', (tester) async {
    final file = File(
      '${Directory.systemTemp.path}/archoera-wallpaper-narrow-'
      '${DateTime.now().microsecondsSinceEpoch}.png',
    );
    file.writeAsBytesSync(_pixelPng);
    addTearDown(() {
      if (file.existsSync()) file.deleteSync();
    });

    final container = await _pumpApp(tester, size: const Size(720, 800));
    final notifier = container.read(appearanceProvider.notifier);
    await notifier.setAppearanceStyle(AppearanceStyle.image);
    await notifier.setBackgroundImage(file.path);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('nav-/settings')));
    await tester.pumpAndSettle();

    expect(find.text('Background image'), findsOneWidget);
    expect(find.text('Blur'), findsOneWidget);
    expect(find.text('Dim'), findsOneWidget);
    expect(find.text('Zoom'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
