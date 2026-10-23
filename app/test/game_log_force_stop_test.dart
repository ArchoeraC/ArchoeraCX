import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/launch/launch_result.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/instances/game_log_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses the force-stop result', () {
    final stopped = LaunchStopResult.fromNative({
      'instanceId': 'inst',
      'stopped': true,
      'pid': 4242,
    });
    expect(stopped.instanceId, 'inst');
    expect(stopped.stopped, isTrue);
    expect(stopped.pid, 4242);

    final already = LaunchStopResult.fromNative({
      'instanceId': 'inst',
      'stopped': false,
    });
    expect(already.stopped, isFalse);
    expect(already.pid, isNull);
  });

  testWidgets('two warnings guard force-stopping the game', (tester) async {
    bool? result;
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: Builder(
            builder: (context) => Center(
              child: ElevatedButton(
                onPressed: () async {
                  result = await confirmForceStopGame(
                    context,
                    instanceName: 'AutumnTown',
                    pid: 4242,
                  );
                },
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    );

    // Cancelling the first warning must not reach the second one.
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    expect(find.text('Force stop the game?'), findsOneWidget);
    expect(find.textContaining('AutumnTown'), findsWidgets);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(find.text('Terminate anyway?'), findsNothing);
    expect(result, isFalse);

    // Continuing reaches the final confirmation; cancelling it also aborts.
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();
    expect(find.text('Terminate anyway?'), findsOneWidget);
    expect(find.textContaining('4242'), findsOneWidget);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(result, isFalse);

    // Only both confirmations together return true.
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Continue'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Force stop'));
    await tester.pumpAndSettle();
    expect(result, isTrue);
    expect(find.text('Terminate anyway?'), findsNothing);
  });
}
