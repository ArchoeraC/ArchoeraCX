import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/diagnostics/crash_analysis.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/toast.dart';
import 'package:archoera_cx/widgets/instances/crash_report_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses the native crash analysis envelope', () {
    final analysis = CrashAnalysis.tryParse({
      'outcome': 'crashed',
      'exitCode': 1,
      'logPath': '/tmp/launch.log',
      'crashReportPath': null,
      'hsErrPath': null,
      'analyzedAt': '2026-10-30T12:00:00Z',
      'findings': [
        {
          'ruleId': 'java.too_old',
          'severity': 'blocker',
          'confidence': 95,
          'args': {'needed': '17', 'current': '8'},
          'evidence': ['class file version 61.0'],
        },
      ],
    });

    expect(analysis, isNotNull);
    expect(analysis!.abnormal, isTrue);
    expect(analysis.exitCode, 1);
    expect(analysis.primary?.ruleId, 'java.too_old');
    expect(analysis.primary?.args['needed'], '17');
  });

  testWidgets('renders the localized primary finding and evidence', (
    tester,
  ) async {
    final analysis = CrashAnalysis.tryParse({
      'outcome': 'crashed',
      'exitCode': 1,
      'findings': [
        {
          'ruleId': 'java.too_old',
          'severity': 'blocker',
          'confidence': 95,
          'args': {'needed': '17', 'current': '8'},
          'evidence': ['class file version 61.0'],
        },
      ],
    })!;

    await _pumpDialog(tester, analysis);

    expect(find.text('游戏异常退出'), findsOneWidget);
    expect(find.text('Java 版本过低'), findsOneWidget);
    expect(find.textContaining('Java 17'), findsWidgets);
    expect(find.text('证据'), findsOneWidget);
    expect(
      find.textContaining('请向他人提供错误日志'),
      findsOneWidget,
      reason: 'the dialog must ask for the log instead of a screenshot',
    );
    expect(
      find.textContaining('如果此次终止由您本人操作'),
      findsNothing,
      reason: 'a normal crash was not a user termination',
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('a force-stop signal is called out as expected', (tester) async {
    final analysis = CrashAnalysis.tryParse({
      'outcome': 'crashed',
      'exitCode': 137,
      'findings': <Object?>[],
    })!;

    await _pumpDialog(tester, analysis);

    expect(
      find.textContaining('如果此次终止由您本人操作'),
      findsOneWidget,
      reason: 'SIGKILL comes from the force-stop button or an external kill',
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('a segmentation fault is not mistaken for a force stop', (
    tester,
  ) async {
    final analysis = CrashAnalysis.tryParse({
      'outcome': 'crashed',
      'exitCode': 139,
      'findings': <Object?>[],
    })!;

    await _pumpDialog(tester, analysis);

    expect(find.textContaining('如果此次终止由您本人操作'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}

Future<void> _pumpDialog(WidgetTester tester, CrashAnalysis analysis) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: AppTheme.light,
      locale: const Locale('zh'),
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: ToastOverlay(
        child: Builder(
          builder: (context) => Scaffold(
            body: TextButton(
              onPressed: () => showCrashReportDialog(context, analysis),
              child: const Text('Open'),
            ),
          ),
        ),
      ),
    ),
  );
  await tester.tap(find.text('Open'));
  await tester.pumpAndSettle();
}
