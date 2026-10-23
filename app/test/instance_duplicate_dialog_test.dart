import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/toast.dart';
import 'package:archoera_cx/widgets/instances/instance_duplicate_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

GameInstance _instance() => const GameInstance(
  id: 'instance-1',
  name: 'Test instance',
  gameVersion: '1.21.4',
  loader: GameLoader.fabric,
  loaderVersion: '0.16.10',
  rootPath: '/tmp/archoera-instance',
);

Future<void> _open(WidgetTester tester, GameInstance instance) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: [coreClientProvider.overrideWithValue(null)],
      child: MaterialApp(
        theme: AppTheme.light,
        locale: const Locale('en'),
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: ToastOverlay(
          child: Builder(
            builder: (context) => Scaffold(
              body: Center(
                child: TextButton(
                  onPressed: () =>
                      showInstanceDuplicateDialog(context, instance),
                  child: const Text('Open'),
                ),
              ),
            ),
          ),
        ),
      ),
    ),
  );

  await tester.tap(find.text('Open'));
  await tester.pumpAndSettle();
}

Future<void> _drainToasts(WidgetTester tester) async {
  await tester.pump(const Duration(seconds: 4));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('opens without a core and suggests a copy name', (tester) async {
    await _open(tester, _instance());

    expect(find.text('Duplicate instance'), findsWidgets);
    expect(find.textContaining('logs excluded'), findsOneWidget);

    final field = tester.widget<TextField>(
      find.widgetWithText(TextField, 'New name'),
    );
    expect(field.controller?.text, 'Test instance copy');
  });

  testWidgets('rejects an empty name without closing', (tester) async {
    await _open(tester, _instance());

    await tester.enterText(find.widgetWithText(TextField, 'New name'), '');
    await tester.tap(find.widgetWithText(FilledButton, 'Duplicate'));
    await tester.pumpAndSettle();

    expect(find.text('Enter an instance name.'), findsWidgets);
    expect(find.text('Duplicate instance'), findsWidgets);

    await _drainToasts(tester);
  });

  testWidgets('cancel closes the dialog without touching the core', (
    tester,
  ) async {
    await _open(tester, _instance());

    await tester.tap(find.widgetWithText(TextButton, 'Cancel'));
    await tester.pumpAndSettle();

    expect(find.text('Duplicate instance'), findsNothing);
  });
}
