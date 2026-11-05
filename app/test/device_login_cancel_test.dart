import 'dart:io';

import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/pages/accounts_page.dart';
import 'package:archoera_cx/services/core/core_client.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final enabled =
      Platform.environment['ARCHOERACX_MICROSOFT_START_TESTS'] == '1' &&
      File('build/native/libarchoera_core.so').existsSync() &&
      File('build/native/archoera_cs.so').existsSync();

  testWidgets(
    'cancel closes the dialog without popping the page below it',
    (tester) async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      await tester.pumpWidget(
        ProviderScope(
          child: MaterialApp(
            theme: AppTheme.dark,
            locale: const Locale('en'),
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            supportedLocales: AppLocalizations.supportedLocales,
            home: _Host(client: client),
          ),
        ),
      );
      await tester.pump();

      await tester.tap(find.text('open'));
      await tester.pump();

      // The native start only creates the loopback listener; wait for its
      // task event and the resulting dialog rebuild.
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 600)),
      );
      await tester.pump();
      expect(find.text('Cancel sign-in'), findsOneWidget);

      await tester.tap(find.text('Cancel sign-in'));
      await tester.pump();
      expect(find.byType(AlertDialog), findsNothing);
      expect(find.text('open'), findsOneWidget);

      // Let the flow cancel RPC and the poll's E_CANCELLED settle. The second
      // pop used to fire here and removed the page under the dialog.
      await tester.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 600)),
      );
      await tester.pump();
      expect(find.text('open'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
    // Requires ARCHOERACX_MICROSOFT_START_TESTS=1 and the built native libs.
    skip: !enabled,
    timeout: const Timeout(Duration(seconds: 60)),
  );
}

class _Host extends StatelessWidget {
  const _Host({required this.client});

  final CoreClient client;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: TextButton(
          onPressed: () {
            showDialog<void>(
              context: context,
              barrierDismissible: false,
              builder: (context) => DeviceLoginDialog(
                client: client,
                // Never launch a real browser from a widget test.
                openAuthorizationUrl: (_) async => true,
              ),
            );
          },
          child: const Text('open'),
        ),
      ),
    );
  }
}
