import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/router.dart';
import 'package:archoera_cx/services/core/settings/app_settings.dart';
import 'package:archoera_cx/stores/platform.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/stores/settings.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

Future<void> _pump(
  WidgetTester tester, {
  Size size = const Size(1440, 900),
  bool linux = true,
  Future<({int width, int height})?> Function()? resolveSize,
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
        linuxPlatformProvider.overrideWithValue(linux),
        maximizedWindowSizeProvider.overrideWithValue(
          resolveSize ?? () async => (width: 1920, height: 1080),
        ),
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

AppSettings? _settings(WidgetTester tester) =>
    ProviderScope.containerOf(tester.element(find.byType(ArchoeraApp)))
        .read(appSettingsProvider)
        .value;

Future<void> _selectWindowMode(WidgetTester tester, String label) async {
  final dropdown = find.byKey(const ValueKey('settings-window-mode'));
  await tester.ensureVisible(dropdown);
  await tester.pumpAndSettle();
  await tester.tap(dropdown);
  await tester.pumpAndSettle();
  await tester.tap(find.text(label).last);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('maximized resolves the display size and clears fullscreen', (
    tester,
  ) async {
    await _pump(tester);

    await _selectWindowMode(tester, 'Maximized');

    final settings = _settings(tester);
    expect(settings?.maximized, isTrue);
    expect(settings?.fullscreen, isFalse);
    expect(settings?.windowWidth, 1920);
    expect(settings?.windowHeight, 1080);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a failed display lookup keeps the current window size', (
    tester,
  ) async {
    await _pump(tester, resolveSize: () async => null);

    await _selectWindowMode(tester, 'Maximized');

    final settings = _settings(tester);
    expect(settings?.maximized, isTrue);
    expect(settings?.windowWidth, 854);
    expect(settings?.windowHeight, 480);
    expect(tester.takeException(), isNull);
  });

  testWidgets('fullscreen and windowed keep the maximized flag off', (
    tester,
  ) async {
    await _pump(tester);

    await _selectWindowMode(tester, 'Fullscreen');
    expect(_settings(tester)?.fullscreen, isTrue);
    expect(_settings(tester)?.maximized, isFalse);

    await _selectWindowMode(tester, 'Windowed');
    expect(_settings(tester)?.fullscreen, isFalse);
    expect(_settings(tester)?.maximized, isFalse);
    expect(tester.takeException(), isNull);
  });

  testWidgets('Linux performance switches save and are hidden elsewhere', (
    tester,
  ) async {
    // Linux: the three convenience switches exist and persist.
    await _pump(tester);
    expect(find.text('Performance (Linux)'), findsOneWidget);
    expect(find.text('Feral GameMode'), findsOneWidget);
    expect(find.text('MangoHud'), findsOneWidget);
    expect(find.text('Zink'), findsOneWidget);

    final gameMode = find.byKey(const ValueKey('settings-gamemode'));
    await tester.ensureVisible(gameMode);
    await tester.pumpAndSettle();
    await tester.tap(gameMode);
    await tester.pumpAndSettle();
    expect(_settings(tester)?.enableGameMode, isTrue);

    // Portable platforms hide the whole Linux-only surface.
    await _pump(tester, linux: false);
    expect(find.text('Performance (Linux)'), findsNothing);
    expect(find.text('Feral GameMode'), findsNothing);
    expect(find.text('Advanced native libraries (developer)'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('developer mode reveals native paths and validates them', (
    tester,
  ) async {
    await _pump(tester);

    expect(find.byKey(const ValueKey('settings-native-glfw')), findsNothing);

    final toggle = find.byKey(const ValueKey('settings-developer-mode'));
    await tester.ensureVisible(toggle);
    await tester.pumpAndSettle();
    await tester.tap(toggle);
    await tester.pumpAndSettle();

    expect(_settings(tester)?.developerMode, isTrue);
    expect(find.byKey(const ValueKey('settings-native-glfw')), findsOneWidget);
    expect(
      find.byKey(const ValueKey('settings-native-openal')),
      findsOneWidget,
    );
    expect(find.byKey(const ValueKey('settings-native-sdl')), findsOneWidget);
    expect(
      find.textContaining('unrelated to the download/compliance'),
      findsOneWidget,
    );

    // A relative path stays local with an inline error.
    final glfw = find.byKey(const ValueKey('settings-native-glfw'));
    await tester.enterText(glfw, 'libglfw.so');
    await tester.pump(const Duration(milliseconds: 600));
    await tester.pumpAndSettle();
    expect(find.text('Enter an absolute file path.'), findsOneWidget);
    expect(_settings(tester)?.nativeGlfwPath, isNull);

    // An absolute path persists.
    await tester.enterText(glfw, '/usr/lib/libglfw.so.3');
    await tester.pump(const Duration(milliseconds: 600));
    await tester.pumpAndSettle();
    expect(find.text('Enter an absolute file path.'), findsNothing);
    expect(_settings(tester)?.nativeGlfwPath, '/usr/lib/libglfw.so.3');

    // Turning the switch off hides the fields but keeps the stored path.
    await tester.ensureVisible(toggle);
    await tester.pumpAndSettle();
    await tester.tap(toggle);
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('settings-native-glfw')), findsNothing);
    expect(_settings(tester)?.nativeGlfwPath, '/usr/lib/libglfw.so.3');
    expect(tester.takeException(), isNull);
  });

  testWidgets('runtime parity panels fit a narrow window', (tester) async {
    await _pump(tester, size: const Size(720, 900));

    expect(find.text('Performance (Linux)'), findsOneWidget);
    expect(
      find.byKey(const ValueKey('settings-developer-mode')),
      findsOneWidget,
    );
    expect(tester.takeException(), isNull);
  });
}
