import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/router.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/stores/instances.dart';
import 'package:archoera_cx/stores/preferences.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Serves one instance with overrides so the page renders the marker and the
/// folder action without a native core.
class _OverriddenInstances extends InstancesNotifier {
  @override
  Future<List<GameInstance>> build() async => const [
    GameInstance(
      id: 'overridden',
      name: 'Overridden',
      gameVersion: '1.21.4',
      loader: GameLoader.fabric,
      rootPath: '/tmp/archoera-test-instance',
      overrides: InstanceOverrides(memoryMaxMb: 4096),
    ),
  ];
}

Future<ProviderContainer> pumpShell(
  WidgetTester tester, {
  String initialLocation = '/',
  List<Override> overrides = const [],
  Size size = const Size(1380, 900),
  Map<String, Object> prefs = const <String, Object>{},
}) async {
  SharedPreferences.setMockInitialValues(<String, Object>{...prefs});
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        routerProvider.overrideWithValue(
          buildAppRouter(initialLocation: initialLocation),
        ),
        ...overrides,
      ],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();
  return ProviderScope.containerOf(tester.element(find.byType(ArchoeraApp)));
}

void main() {
  group('startup page preference', () {
    test('readStartPagePreference accepts known routes only', () async {
      SharedPreferences.setMockInitialValues(<String, Object>{});
      expect(await readStartPagePreference(), isNull);

      SharedPreferences.setMockInitialValues(<String, Object>{
        'ui.startPage': '/wiki',
      });
      expect(await readStartPagePreference(), '/wiki');

      SharedPreferences.setMockInitialValues(<String, Object>{
        'ui.startPage': '/not-a-route',
      });
      expect(await readStartPagePreference(), isNull);
    });

    test('setStartPage persists and ignores unknown routes', () async {
      SharedPreferences.setMockInitialValues(<String, Object>{});
      final container = ProviderContainer();
      addTearDown(container.dispose);

      // Let the controller finish restoring before changing the value.
      container.read(preferencesProvider);
      await Future<void>.delayed(Duration.zero);

      await container
          .read(preferencesProvider.notifier)
          .setStartPage('/download');
      expect(container.read(preferencesProvider).startPage, '/download');
      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getString('ui.startPage'), '/download');

      await container
          .read(preferencesProvider.notifier)
          .setStartPage('/not-a-route');
      expect(container.read(preferencesProvider).startPage, '/download');
    });
  });

  testWidgets('storage section opens the data and instance folders', (
    tester,
  ) async {
    await pumpShell(tester, initialLocation: '/settings');
    await tester.tap(find.text('Storage'));
    await tester.pumpAndSettle();

    expect(find.text('Open data folder'), findsOneWidget);
    expect(find.text('Open default instances folder'), findsOneWidget);

    // No native core is attached, so the core-reported failure surfaces as a
    // toast instead of silently doing nothing.
    await tester.tap(find.text('Open data folder'));
    await tester.pumpAndSettle();
    expect(find.text('Could not open that folder.'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('appearance section exposes the startup page picker', (
    tester,
  ) async {
    await pumpShell(tester, initialLocation: '/settings');

    expect(find.text('Startup page'), findsOneWidget);
    expect(
      find.text('Applied the next time the launcher starts.'),
      findsOneWidget,
    );
    final dropdown = tester.widget<DropdownButton<String>>(
      find.byKey(const ValueKey('start-page-dropdown')),
    );
    expect(dropdown.value, '/');
    expect(dropdown.items, hasLength(6));
    expect(tester.takeException(), isNull);
  });

  testWidgets('storage section exposes the partial download cleanup row', (
    tester,
  ) async {
    await pumpShell(tester, initialLocation: '/settings');
    await tester.tap(find.text('Storage'));
    await tester.pumpAndSettle();

    expect(find.text('Clean partial downloads'), findsOneWidget);
    expect(
      find.text(
        'Removes `.part` files untouched for more than three days; '
        'active downloads are kept.',
      ),
      findsOneWidget,
    );

    // Without a native core the action reports the missing connection instead
    // of pretending the cleanup ran.
    await tester.tap(find.text('Clean partial downloads'));
    await tester.pumpAndSettle();
    expect(find.text('The native core is not connected yet.'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('appearance section exposes the close-to-tray switch', (
    tester,
  ) async {
    await pumpShell(
      tester,
      initialLocation: '/settings',
      prefs: const {'ui.closeToTray': false},
    );

    expect(find.text('Close to tray'), findsOneWidget);

    final tileRow = find
        .ancestor(of: find.text('Close to tray'), matching: find.byType(Row))
        .first;
    final toggle = find.descendant(of: tileRow, matching: find.byType(Switch));
    expect(toggle, findsOneWidget);
    expect(tester.widget<Switch>(toggle).value, isFalse);
    expect(tester.takeException(), isNull);
  });

  testWidgets('instances page marks overrides and opens the folder', (
    tester,
  ) async {
    await pumpShell(
      tester,
      initialLocation: '/instances',
      overrides: [instancesProvider.overrideWith(_OverriddenInstances.new)],
    );

    final badge = find.byWidgetPredicate(
      (widget) =>
          widget is Tooltip &&
          (widget.message ?? '').startsWith('Overrides global settings'),
    );
    expect(badge, findsOneWidget);
    expect(tester.widget<Tooltip>(badge).message, contains('memory'));

    await tester.tap(find.byTooltip('Manage').first);
    await tester.pumpAndSettle();
    expect(find.text('Open instance folder'), findsOneWidget);

    await tester.tap(find.text('Open instance folder'));
    await tester.pumpAndSettle();
    expect(find.text('Could not open that folder.'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pump(const Duration(seconds: 4));
  });
}
