import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/providers.dart';
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

    // Launch: window size, fullscreen and the game argument field.
    await tester.tap(find.text('Launch').first);
    await tester.pumpAndSettle();
    expect(find.text('Game window'), findsOneWidget);
    expect(find.text('Start fullscreen'), findsOneWidget);
    expect(find.text('Window size'), findsOneWidget);
    expect(find.text('854 × 480'), findsOneWidget);
    expect(find.text('Additional game arguments'), findsOneWidget);
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
}
