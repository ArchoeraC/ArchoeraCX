import 'package:archoera_cx/app/elevation_gate.dart';
import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/system/process_elevation.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget harness({
  required ProcessElevation elevation,
  required ValueChanged<bool> onDecision,
}) {
  return MaterialApp(
    theme: AppTheme.light,
    locale: const Locale('en'),
    localizationsDelegates: AppLocalizations.localizationsDelegates,
    supportedLocales: AppLocalizations.supportedLocales,
    home: ElevationGatePage(elevation: elevation, onDecision: onDecision),
  );
}

void main() {
  testWidgets('exit refuses the elevated start', (tester) async {
    bool? decision;
    await tester.pumpWidget(
      harness(
        elevation: const ProcessElevation(elevated: true, kind: 'root'),
        onDecision: (proceed) => decision = proceed,
      ),
    );

    expect(find.text("I know what you're trying to do"), findsOneWidget);
    expect(find.textContaining('running as root'), findsOneWidget);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Exit'));
    expect(decision, isFalse);
  });

  testWidgets('the red confirm button accepts the risk', (tester) async {
    bool? decision;
    await tester.pumpWidget(
      harness(
        elevation: const ProcessElevation(
          elevated: true,
          kind: 'administrator',
        ),
        onDecision: (proceed) => decision = proceed,
      ),
    );

    expect(find.textContaining('running as administrator'), findsOneWidget);

    await tester.tap(
      find.widgetWithText(FilledButton, "I know what I'm doing"),
    );
    expect(decision, isTrue);
  });

  test('parses the native elevation payload', () {
    final elevated = ProcessElevation.tryParse(<String, Object?>{
      'elevated': true,
      'kind': 'root',
    });
    expect(elevated, isNotNull);
    expect(elevated!.elevated, isTrue);
    expect(elevated.kind, 'root');

    final normal = ProcessElevation.tryParse(<String, Object?>{
      'elevated': false,
      'kind': null,
    });
    expect(normal, isNotNull);
    expect(normal!.elevated, isFalse);
    expect(normal.kind, isNull);

    expect(ProcessElevation.tryParse('root'), isNull);
  });
}
