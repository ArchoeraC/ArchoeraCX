import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/widgets/common/pack_safety.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('destructive pack codes are classified', () {
    for (final code in <String>{
      'PACK_ARCHIVE_BOMB',
      'PACK_PATH_ESCAPE',
      'PACK_SYMLINK_ENTRY',
      'PACK_TOO_LARGE',
    }) {
      expect(isDestructivePackCode(code), isTrue, reason: code);
    }
    expect(isDestructivePackCode('PACK_ARCHIVE_INVALID'), isFalse);
    expect(isDestructivePackCode('PACK_INVALID'), isFalse);
    expect(isDestructivePackCode(null), isFalse);
  });

  testWidgets('blocked pack dialog explains the risk and closes', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        locale: const Locale('en'),
        home: Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: ElevatedButton(
                onPressed: () => showPackBlockedDialog(
                  context,
                  code: 'PACK_ARCHIVE_BOMB',
                  detail: 'overrides/x: expands more than 200:1',
                ),
                child: const Text('show'),
              ),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('show'));
    await tester.pumpAndSettle();
    expect(find.text('Unsafe modpack blocked'), findsOneWidget);
    expect(find.textContaining('zip bomb'), findsOneWidget);
    expect(find.text('PACK_ARCHIVE_BOMB'), findsOneWidget);

    await tester.tap(find.text('Close'));
    await tester.pumpAndSettle();
    expect(find.text('Unsafe modpack blocked'), findsNothing);
  });
}
