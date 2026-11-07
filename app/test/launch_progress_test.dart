import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/launch/launch_progress.dart';
import 'package:archoera_cx/widgets/launch/launch_progress_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('LaunchProgressController', () {
    test('marks reported stages as reached', () {
      final progress = LaunchProgressController();

      progress.report(1, 8);
      progress.report(2, 8);

      expect(progress.furthest, 2);
      expect(progress.reached, <int>{1, 2});
      expect(progress.skipped, isEmpty);
    });

    test('marks an unreported stage as skipped when the next one arrives', () {
      final progress = LaunchProgressController();

      progress.report(5, 8);
      progress.report(7, 8);

      expect(progress.furthest, 7);
      expect(progress.reached, <int>{5, 7});
      expect(progress.skipped, <int>{6});
    });

    test('ignores duplicate and out-of-order reports', () {
      final progress = LaunchProgressController();
      var notifications = 0;
      progress.addListener(() => notifications++);

      progress.report(3, 8);
      progress.report(3, 8);
      progress.report(2, 8);

      expect(progress.furthest, 3);
      expect(progress.reached, <int>{3});
      expect(notifications, 1);
    });

    test('clamps reports above the expected total', () {
      final progress = LaunchProgressController();

      progress.report(12, 8);

      expect(progress.furthest, progress.total);
    });

    test('hasReports stays false until the first report', () {
      final progress = LaunchProgressController();

      expect(progress.hasReports, isFalse);
      progress.report(0, 8);
      expect(progress.hasReports, isFalse);
    });

    test('ignores late reports after the dialog was dismissed', () {
      final progress = LaunchProgressController();

      progress.report(2, 8);
      progress.dispose();

      expect(() => progress.report(4, 8), returnsNormally);
      expect(progress.furthest, 2);
    });
  });

  testWidgets('closes the progress dialog while the launch keeps running', (
    tester,
  ) async {
    final progress = await _openProgressDialog(tester);

    await tester.tap(find.text('Run in background'));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.byType(LaunchProgressDialog), findsNothing);
    // The launch itself is not cancelled: a later stage report after the
    // dialog (and its controller) is gone must be harmless.
    expect(() => progress.report(6, 8), returnsNormally);
  });

  testWidgets('closes the progress dialog when the barrier is tapped', (
    tester,
  ) async {
    await _openProgressDialog(tester);

    // A tap outside the dialog lands on the barrier, which must dismiss it.
    await tester.tapAt(const Offset(10, 10));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 300));
    expect(find.byType(LaunchProgressDialog), findsNothing);
  });
}

Future<LaunchProgressController> _openProgressDialog(
  WidgetTester tester,
) async {
  final progress = LaunchProgressController();
  progress.report(1, 8);
  await tester.pumpWidget(
    MaterialApp(
      locale: const Locale('en'),
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: Builder(
        builder: (context) => TextButton(
          onPressed: () => showDialog<void>(
            context: context,
            barrierDismissible: true,
            builder: (_) =>
                LaunchProgressDialog(instanceName: 'Test', progress: progress),
          ),
          child: const Text('Open'),
        ),
      ),
    ),
  );

  await tester.tap(find.text('Open'));
  // The running stage spinner never settles, so pump fixed frames instead of
  // waiting for an idle frame.
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 300));
  expect(find.text('Starting Test'), findsOneWidget);
  return progress;
}
