import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/services/core/launch/launch_log.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/instances/game_log_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('launch log models', () {
    test('parses a log list with the running launch flagged', () {
      final list = LaunchLogList.tryParse({
        'instanceId': 'inst',
        'instanceName': 'AutumnTown',
        'logDirectory': '/games/AutumnTown/.archoera/logs',
        'running': true,
        'pid': 4242,
        'activeLog': 'launch-20261022-120000.log',
        'logs': [
          {
            'fileName': 'launch-20261022-120000.log',
            'size': 2048,
            'modifiedAt': '2026-10-22T12:00:00.0000000Z',
            'active': true,
          },
          {'fileName': 'launch-20261021-090000.log', 'size': 512},
          {'size': 1},
        ],
      });

      expect(list, isNotNull);
      expect(list!.running, isTrue);
      expect(list.pid, 4242);
      expect(list.activeLog, 'launch-20261022-120000.log');
      expect(list.logs.length, 2);
      expect(list.logs.first.active, isTrue);
      expect(list.logs.first.size, 2048);
      expect(list.logs.first.modifiedAt, isNotNull);
      expect(list.logs.last.active, isFalse);
    });

    test('parses a snapshot and keeps only string lines', () {
      final snapshot = LaunchLogSnapshot.tryParse({
        'instanceId': 'inst',
        'fileName': 'launch-20261022-120000.log',
        'size': 900,
        'truncated': true,
        'running': false,
        'lines': ['[12:00:00] [main/INFO] boot', 42, '[12:00:01] done'],
      });

      expect(snapshot, isNotNull);
      expect(snapshot!.truncated, isTrue);
      expect(snapshot.fileName, 'launch-20261022-120000.log');
      expect(snapshot.lines, [
        '[12:00:00] [main/INFO] boot',
        '[12:00:01] done',
      ]);
    });

    test('rejects payloads without an instance id', () {
      expect(LaunchLogList.tryParse({'logs': <Object>[]}), isNull);
      expect(LaunchLogSnapshot.tryParse({'lines': <Object>[]}), isNull);
      expect(LaunchLogList.tryParse(null), isNull);
    });
  });

  testWidgets('log dialog explains that the core is unavailable', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1280, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [coreClientProvider.overrideWithValue(null)],
        child: MaterialApp(
          theme: AppTheme.light,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: Builder(
            builder: (context) => Scaffold(
              body: Center(
                child: ElevatedButton(
                  onPressed: () => showGameLogDialog(
                    context,
                    const GameInstance(
                      id: 'inst',
                      name: 'AutumnTown',
                      gameVersion: '1.21.1',
                      loader: GameLoader.neoforge,
                      loaderVersion: '21.1.235',
                      rootPath: '/games/AutumnTown',
                      modCount: 45,
                    ),
                  ),
                  child: const Text('open'),
                ),
              ),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();

    expect(find.text('Game log'), findsOneWidget);
    expect(find.text('The native core is not connected yet.'), findsOneWidget);
    expect(find.text('Follow'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
