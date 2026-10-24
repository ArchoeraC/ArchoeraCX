import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/stores/settings.dart';
import 'package:archoera_cx/theme/icons/app_icons.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

Future<ProviderContainer> pumpDownloads(
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
  await tester.tap(find.byTooltip('Settings'));
  await tester.pumpAndSettle();
  final downloads = find.text('Downloads');
  await tester.ensureVisible(downloads);
  await tester.pumpAndSettle();
  await tester.tap(downloads);
  await tester.pumpAndSettle();
  return ProviderScope.containerOf(tester.element(find.byType(ArchoeraApp)));
}

void main() {
  testWidgets('download section exposes source and concurrency controls', (
    tester,
  ) async {
    final container = await pumpDownloads(tester);

    expect(find.text('Source priority'), findsOneWidget);
    expect(find.text('Parallel downloads'), findsOneWidget);
    expect(find.text('Connections per host'), findsOneWidget);
    expect(find.text('Automatic'), findsOneWidget);
    expect(find.text('Download proxy'), findsOneWidget);
    expect(find.text('Follow the system proxy'), findsOneWidget);
    // `auto` explains what it resolves to (no core in tests: still detecting).
    expect(find.textContaining('Automatic ·'), findsOneWidget);
    expect(find.text('Official first'), findsOneWidget);
    expect(find.text('Mirror first'), findsOneWidget);

    // Defaults come from the native settings document.
    final settings = container.read(appSettingsProvider).value;
    expect(settings?.downloadSource, 'auto');
    expect(settings?.downloadConcurrency, 16);
    expect(settings?.downloadPerHost, 8);
    expect(settings?.downloadChunkSizeKb, 4096);
    expect(settings?.downloadRangeThresholdKb, 8192);
    expect(tester.takeException(), isNull);
  });

  testWidgets('choosing a source and raising concurrency persists', (
    tester,
  ) async {
    final container = await pumpDownloads(tester);

    await tester.tap(find.text('Mirror first'));
    await tester.pumpAndSettle();
    expect(container.read(appSettingsProvider).value?.downloadSource, 'mirror');

    await tester.tap(
      find.descendant(
        of: find.byKey(const ValueKey('download-concurrency-stepper')),
        matching: find.byIcon(AppIcons.add),
      ),
    );
    await tester.pumpAndSettle();
    expect(container.read(appSettingsProvider).value?.downloadConcurrency, 17);

    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('crossing 32 streams needs an explicit acknowledgement', (
    tester,
  ) async {
    final container = await pumpDownloads(tester);

    await container
        .read(appSettingsProvider.notifier)
        .setDownloadPreferences(downloadConcurrency: 32);
    await tester.pumpAndSettle();
    expect(find.text('32'), findsOneWidget);

    // 32 -> 33 asks first; cancelling keeps the safe value.
    await tester.tap(
      find.descendant(
        of: find.byKey(const ValueKey('download-concurrency-stepper')),
        matching: find.byIcon(AppIcons.add),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Enable very high concurrency?'), findsOneWidget);
    expect(
      find.textContaining('Unless you know what you are doing'),
      findsOneWidget,
    );

    await tester.tap(find.widgetWithText(TextButton, 'Cancel'));
    await tester.pumpAndSettle();
    expect(container.read(appSettingsProvider).value?.downloadConcurrency, 32);
    expect(find.textContaining('disk wear'), findsNothing);

    // Confirming stores 33 and keeps a persistent warning on screen.
    await tester.tap(
      find.descendant(
        of: find.byKey(const ValueKey('download-concurrency-stepper')),
        matching: find.byIcon(AppIcons.add),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Enable anyway'));
    await tester.pumpAndSettle();
    expect(container.read(appSettingsProvider).value?.downloadConcurrency, 33);
    expect(find.textContaining('33 streams'), findsOneWidget);

    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('manual proxy is validated and stored', (tester) async {
    final container = await pumpDownloads(tester);

    await tester.tap(find.text('Download proxy'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), 'ftp://127.0.0.1:1080');
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    expect(
      find.textContaining('Enter an http(s) or socks5 proxy URL'),
      findsOneWidget,
    );

    // SOCKS5 proxies are accepted (Clash-style local ports).
    await tester.enterText(find.byType(TextField), 'socks5h://127.0.0.1:7890');
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();
    expect(
      container.read(appSettingsProvider).value?.downloadProxy,
      'socks5h://127.0.0.1:7890',
    );

    await tester.tap(find.text('socks5h://127.0.0.1:7890'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), 'http://127.0.0.1:7890');
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(
      container.read(appSettingsProvider).value?.downloadProxy,
      'http://127.0.0.1:7890',
    );
    expect(find.text('http://127.0.0.1:7890'), findsOneWidget);

    // Back to the system proxy.
    await tester.tap(find.text('http://127.0.0.1:7890'));
    await tester.pumpAndSettle();
    await tester.tap(
      find.widgetWithText(TextButton, 'Follow the system proxy'),
    );
    await tester.pumpAndSettle();
    expect(container.read(appSettingsProvider).value?.downloadProxy, isNull);

    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('download section fits a narrow window', (tester) async {
    await pumpDownloads(tester, size: const Size(720, 900));

    expect(find.text('Parallel downloads'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
