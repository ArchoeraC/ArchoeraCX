import 'dart:io';

import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/core_client.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/core_status_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Widget buildCard({required CoreClient Function() clientFactory}) {
    return MaterialApp(
      theme: AppTheme.light,
      locale: const Locale('en'),
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: Scaffold(
        body: Padding(
          padding: const EdgeInsets.all(16),
          child: CoreStatusCard(clientFactory: clientFactory),
        ),
      ),
    );
  }

  CoreClient missingLibrary() {
    throw CoreUnavailableException(
      'libarchoera_core not found (searched 3 locations).',
      searchedPaths: const <String>[
        '/tmp/build/native/libarchoera_core.so',
        '/tmp/app/build/native/libarchoera_core.so',
        '/tmp/build/linux/x64/release/bundle/lib/libarchoera_core.so',
      ],
    );
  }

  testWidgets('renders the unavailable state without a native library', (
    tester,
  ) async {
    await tester.pumpWidget(buildCard(clientFactory: missingLibrary));
    await tester.pumpAndSettle();

    expect(find.text('Core unavailable'), findsOneWidget);
    expect(find.textContaining('libarchoera_core not found'), findsOneWidget);
    expect(find.text('Ping core'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('ping failure stays on the unavailable state', (tester) async {
    await tester.pumpWidget(buildCard(clientFactory: missingLibrary));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Ping core'));
    await tester.pumpAndSettle();

    expect(find.text('Core unavailable'), findsOneWidget);
    expect(find.textContaining('libarchoera_core not found'), findsOneWidget);
    expect(find.textContaining('Ping failed'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  final libraryName = Platform.isWindows
      ? 'archoera_core.dll'
      : Platform.isMacOS
      ? 'libarchoera_core.dylib'
      : 'libarchoera_core.so';
  final library = File('build/native/$libraryName');

  testWidgets('pings the native core when available', (tester) async {
    await tester.pumpWidget(buildCard(clientFactory: CoreClient.openDefault));
    await tester.pumpAndSettle();

    expect(find.text('Idle'), findsOneWidget);

    await tester.tap(find.text('Ping core'));
    await tester.pump();

    // The ping future is completed by the native wake callback, which is
    // delivered on the real event loop. Widget tests use fake async by
    // default, so yield to real time between pumps instead of using a timer
    // inside the app.
    var connected = false;
    for (var attempt = 0; attempt < 100 && !connected; attempt++) {
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 20)),
      );
      await tester.pump();
      connected = find.text('Connected').evaluate().isNotEmpty;
    }

    expect(
      connected,
      isTrue,
      reason: 'pingAsync should complete through the event pump',
    );

    await tester.pumpAndSettle();
    expect(find.text('pong: true'), findsOneWidget);
    expect(find.text('Connected'), findsOneWidget);
    expect(tester.takeException(), isNull);
  }, skip: !library.existsSync());
}
