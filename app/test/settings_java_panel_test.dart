import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/stores/settings.dart';
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

    // Java selection (auto/current) lives on the dedicated Java page, so this
    // section only links there and keeps the memory/GC/JVM/game options.
    expect(find.text('Java runtimes'), findsOneWidget);
    expect(find.textContaining('Browse every scan result'), findsOneWidget);
    expect(find.text('Automatic (per game version)'), findsNothing);
    expect(find.text('Current Java'), findsNothing);
    expect(find.textContaining('View all'), findsNothing);
    expect(find.text('Scan Java'), findsNothing);
    expect(find.text('System memory'), findsOneWidget);
    expect(find.text('Automatic memory'), findsOneWidget);
    // Automatic sizing hides the manual heap fields by default.
    expect(find.text('Minimum memory (MB)'), findsNothing);
    expect(find.text('Maximum memory (MB)'), findsNothing);
    // Game arguments moved to the Launch section.
    expect(find.text('Additional game arguments'), findsNothing);
    expect(tester.takeException(), isNull);

    // Switching to manual brings the fields back so a value can be forced.
    await tester.tap(find.byKey(const ValueKey('settings-memory-auto')));
    await tester.pumpAndSettle();
    expect(find.text('Minimum memory (MB)'), findsOneWidget);
    expect(find.text('Maximum memory (MB)'), findsOneWidget);
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

  testWidgets('settings exposes launch, task and notification options', (
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

    // Launch: window size, fullscreen, game arguments and process options.
    await tester.tap(find.text('Launch').first);
    await tester.pumpAndSettle();
    expect(find.text('Game window'), findsOneWidget);
    expect(find.text('Window mode'), findsOneWidget);
    expect(find.text('Window size'), findsOneWidget);
    expect(find.text('854 × 480'), findsOneWidget);
    expect(find.text('Additional game arguments'), findsOneWidget);
    expect(find.text('Commands'), findsOneWidget);
    expect(find.text('Pre-load command'), findsOneWidget);
    expect(find.text('Pre-launch command'), findsOneWidget);
    expect(find.text('Post-exit command'), findsOneWidget);
    expect(find.text('Wrapper & environment'), findsOneWidget);
    expect(find.text('Wrapper command'), findsOneWidget);
    expect(find.text('Environment variables'), findsOneWidget);
    expect(tester.takeException(), isNull);

    // Tasks & logs: both limits are segmented controls.
    await tester.tap(find.text('Tasks & logs').first);
    await tester.pumpAndSettle();
    expect(find.text('Task history'), findsOneWidget);
    expect(find.text('Default log lines'), findsOneWidget);
    expect(find.text('400'), findsOneWidget);
    expect(tester.takeException(), isNull);

    // Notifications: login and download notifications can be muted.
    await tester.tap(find.text('Notifications').first);
    await tester.pumpAndSettle();
    expect(find.text('Desktop notifications'), findsOneWidget);
    expect(find.text('Download notifications'), findsOneWidget);
    expect(find.byType(Switch), findsWidgets);
    expect(tester.takeException(), isNull);
  });

  testWidgets('launch wrapper and environment save and validate', (
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
    await tester.tap(find.text('Launch').first);
    await tester.pumpAndSettle();

    final wrapper = find.byKey(const ValueKey('settings-wrapper-command'));
    final environment = find.byKey(const ValueKey('settings-environment'));
    await tester.ensureVisible(wrapper);
    await tester.pumpAndSettle();
    await tester.enterText(wrapper, 'mangohud');
    await tester.ensureVisible(environment);
    await tester.pumpAndSettle();
    await tester.enterText(environment, 'DRI_PRIME=1');
    await tester.pump(const Duration(milliseconds: 600));
    await tester.pumpAndSettle();

    final container = ProviderScope.containerOf(
      tester.element(find.byType(ArchoeraApp)),
    );
    expect(
      container.read(appSettingsProvider).value?.wrapperCommand,
      'mangohud',
    );
    expect(
      container.read(appSettingsProvider).value?.environment,
      const <String, String>{'DRI_PRIME': '1'},
    );

    // An invalid line stays local and never reaches the settings store.
    await tester.enterText(environment, 'BAD KEY=1');
    await tester.pump(const Duration(milliseconds: 600));
    await tester.pumpAndSettle();
    expect(find.textContaining('not a valid variable name'), findsOneWidget);
    expect(
      container.read(appSettingsProvider).value?.environment,
      const <String, String>{'DRI_PRIME': '1'},
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('launch lifecycle commands save and validate', (tester) async {
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
    await tester.tap(find.text('Launch').first);
    await tester.pumpAndSettle();

    final preLoad = find.byKey(const ValueKey('settings-pre-load-command'));
    final preLaunch = find.byKey(const ValueKey('settings-pre-launch-command'));
    final postExit = find.byKey(const ValueKey('settings-post-exit-command'));
    await tester.ensureVisible(preLaunch);
    await tester.pumpAndSettle();
    await tester.enterText(preLoad, 'echo prepare');
    await tester.enterText(preLaunch, 'echo start');
    await tester.enterText(postExit, 'echo done');
    await tester.pump(const Duration(milliseconds: 600));
    await tester.pumpAndSettle();

    final container = ProviderScope.containerOf(
      tester.element(find.byType(ArchoeraApp)),
    );
    final saved = container.read(appSettingsProvider).value;
    expect(saved?.preLoadCommand, 'echo prepare');
    expect(saved?.preLaunchCommand, 'echo start');
    expect(saved?.postExitCommand, 'echo done');

    // An unclosed quote stays local and never reaches the settings store.
    await tester.enterText(preLaunch, 'echo "unclosed');
    await tester.pump(const Duration(milliseconds: 600));
    await tester.pumpAndSettle();
    expect(find.text('Unclosed " quote.'), findsOneWidget);
    expect(
      container.read(appSettingsProvider).value?.preLaunchCommand,
      'echo start',
    );
    expect(tester.takeException(), isNull);
  });
}
