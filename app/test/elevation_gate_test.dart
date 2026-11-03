import 'dart:math';

import 'package:archoera_cx/app/elevation_gate.dart';
import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/system/process_elevation.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget harness({
  required ProcessElevation elevation,
  required ValueChanged<bool> onDecision,
  Key? key,
  Random? random,
}) {
  return MaterialApp(
    theme: AppTheme.light,
    locale: const Locale('en'),
    localizationsDelegates: AppLocalizations.localizationsDelegates,
    supportedLocales: AppLocalizations.supportedLocales,
    home: ElevationGatePage(
      key: key,
      elevation: elevation,
      onDecision: onDecision,
      random: random,
    ),
  );
}

List<String> headlines(AppLocalizations l10n) => <String>[
  l10n.elevationTitle,
  l10n.elevationTitleJustKnew,
  l10n.elevationTitleBegging,
];

List<String> confirms(AppLocalizations l10n) => <String>[
  l10n.elevationContinue,
  l10n.elevationContinueBegging,
  l10n.elevationContinueInsist,
];

/// Returns the one candidate the gate is currently showing.
String shownVariant(WidgetTester tester, List<String> candidates) {
  final visible = candidates
      .where((text) => find.text(text).evaluate().isNotEmpty)
      .toList();
  expect(visible, hasLength(1));
  return visible.single;
}

AppLocalizations gateL10n(WidgetTester tester) =>
    AppLocalizations.of(tester.element(find.byType(ElevationGatePage)));

void main() {
  testWidgets('exit refuses the elevated start', (tester) async {
    bool? decision;
    await tester.pumpWidget(
      harness(
        elevation: const ProcessElevation(elevated: true, kind: 'root'),
        onDecision: (proceed) => decision = proceed,
        random: Random(1),
      ),
    );

    shownVariant(tester, headlines(gateL10n(tester)));
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
        random: Random(2),
      ),
    );

    expect(find.textContaining('running as administrator'), findsOneWidget);
    final label = shownVariant(tester, confirms(gateL10n(tester)));

    await tester.tap(find.widgetWithText(FilledButton, label));
    expect(decision, isTrue);
  });

  testWidgets('headline and confirm label rotate between launches', (
    tester,
  ) async {
    final seenHeadlines = <String>{};
    final seenConfirms = <String>{};

    for (var seed = 0; seed < 32; seed++) {
      await tester.pumpWidget(
        harness(
          key: ValueKey<int>(seed),
          elevation: const ProcessElevation(elevated: true, kind: 'root'),
          onDecision: (_) {},
          random: Random(seed),
        ),
      );
      final l10n = gateL10n(tester);
      seenHeadlines.add(shownVariant(tester, headlines(l10n)));
      seenConfirms.add(shownVariant(tester, confirms(l10n)));
    }

    final l10n = gateL10n(tester);
    expect(seenHeadlines, headlines(l10n).toSet());
    expect(seenConfirms, confirms(l10n).toSet());
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
