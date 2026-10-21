import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/instances/create_instance_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('creates an instance from the dialog', (tester) async {
    await tester.pumpWidget(
      ProviderScope(
        overrides: [coreClientProvider.overrideWithValue(null)],
        child: MaterialApp(
          theme: AppTheme.light,
          locale: const Locale('en'),
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Builder(
            builder: (context) => Scaffold(
              body: Center(
                child: TextButton(
                  onPressed: () => showCreateInstanceDialog(context),
                  child: const Text('Open'),
                ),
              ),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    expect(find.text('Create instance'), findsOneWidget);

    await tester.enterText(find.byType(TextField).first, 'My Test Instance');
    await tester.pump();
    await tester.tap(find.widgetWithText(FilledButton, 'Create'));
    await tester.pumpAndSettle();
    await tester.pump(const Duration(milliseconds: 400));

    expect(find.text('Instance created.'), findsOneWidget);
    expect(find.text('Create instance'), findsNothing);
  });
}
