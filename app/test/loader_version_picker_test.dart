import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/loader_versions.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/instances/loader_version_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('selects an older loader version from the picker', (
    tester,
  ) async {
    String? selected;
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        locale: const Locale('en'),
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () async {
                selected = await showLoaderVersionPicker(
                  context,
                  versions: const [
                    LoaderVersionInfo(version: '0.20.1', latest: true),
                    LoaderVersionInfo(
                      version: '0.16.10',
                      stable: true,
                      recommended: true,
                    ),
                    LoaderVersionInfo(version: '0.16.9', stable: true),
                  ],
                  selected: '0.20.1',
                  gameVersion: '1.21.1',
                );
              },
              child: const Text('Open'),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    expect(find.text('Select loader version'), findsOneWidget);
    expect(find.text('Latest'), findsOneWidget);
    expect(find.text('Recommended'), findsOneWidget);

    await tester.enterText(find.byType(TextField), '0.16');
    await tester.pump();
    expect(find.text('0.20.1'), findsNothing);
    expect(find.text('0.16.10'), findsOneWidget);
    expect(find.text('0.16.9'), findsOneWidget);

    await tester.tap(find.text('0.16.9'));
    await tester.pumpAndSettle();
    expect(selected, '0.16.9');
    expect(find.text('Select loader version'), findsNothing);
  });
}
