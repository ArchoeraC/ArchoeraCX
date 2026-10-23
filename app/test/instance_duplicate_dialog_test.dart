import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/services/core/core_client.dart';
import 'package:archoera_cx/stores/downloads.dart';
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

/// Records the copy instead of touching the native core.
class _RecordingDownloads extends DownloadsNotifier {
  final List<String> names = <String>[];

  @override
  Future<void> duplicateInstance(
    GameInstance instance, {
    required String name,
    required String successMessage,
    required String failureMessage,
    required String cancelledMessage,
    String? Function(CoreException error)? describeError,
  }) async {
    names.add(name);
  }
}

Future<void> _open(
  WidgetTester tester,
  GameInstance instance, {
  _RecordingDownloads? downloads,
}) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        if (downloads != null) downloadsProvider.overrideWith(() => downloads),
      ],
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

  testWidgets('duplicating queues the copy and closes the dialog', (
    tester,
  ) async {
    final downloads = _RecordingDownloads();
    await _open(tester, _instance(), downloads: downloads);

    await tester.tap(find.widgetWithText(FilledButton, 'Duplicate'));
    await tester.pumpAndSettle();

    expect(downloads.names, ['Test instance copy']);
    // The copy used to hold this dialog open for the whole folder walk.
    expect(find.text('Duplicate instance'), findsNothing);
    expect(find.textContaining('queued'), findsOneWidget);

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
