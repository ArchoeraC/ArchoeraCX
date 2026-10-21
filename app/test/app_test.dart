import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/shell.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('renders the ArchoeraCX app shell', (tester) async {
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

    expect(find.byType(AppShell), findsOneWidget);
    expect(find.textContaining('ArchoeraCX'), findsWidgets);
    expect(find.text('Quick launch'), findsOneWidget);
    expect(find.text('Instances'), findsWidgets);
    expect(find.text('Download'), findsWidgets);
    expect(find.text('Settings'), findsWidgets);
  });

  testWidgets('fits the shell on narrow windows', (tester) async {
    tester.view.physicalSize = const Size(800, 600);
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

    expect(find.byType(AppShell), findsOneWidget);

    await tester.tap(find.byIcon(Icons.download_outlined));
    await tester.pumpAndSettle();
    expect(find.text('Game versions'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('fits the shell at 700px', (tester) async {
    tester.view.physicalSize = const Size(700, 640);
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

    expect(find.byType(AppShell), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.byIcon(Icons.download_outlined));
    await tester.pumpAndSettle();
    expect(find.text('Game versions'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('switches the UI language from settings', (tester) async {
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
    await tester.tap(find.text('Language'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('简体中文'));
    await tester.pumpAndSettle();

    expect(find.text('设置'), findsWidgets);
    expect(find.text('外观'), findsWidgets);
    expect(tester.takeException(), isNull);
  });

  testWidgets('covers page switches and retracts back to home', (tester) async {
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

    await tester.tap(find.text('Download'));
    await tester.pump(const Duration(milliseconds: 120));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Settings'));
    await tester.pump(const Duration(milliseconds: 120));
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(Icons.space_dashboard_outlined));
    await tester.pump(const Duration(milliseconds: 120));
    await tester.pumpAndSettle();

    expect(find.text('Quick launch'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('navigates between shell pages', (tester) async {
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

    await tester.tap(find.text('Download'));
    await tester.pumpAndSettle();
    expect(find.text('Game versions'), findsOneWidget);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('Accounts'));
    await tester.pumpAndSettle();
    expect(find.text('Add account'), findsWidgets);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('Settings'));
    await tester.pumpAndSettle();
    expect(find.text('Appearance'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
