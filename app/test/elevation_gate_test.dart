import 'dart:math';

import 'package:archoera_cx/app/elevation_gate.dart';
import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/system/process_elevation.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/theme/tokens.dart';
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

({List<String> headlines, List<String> confirms}) gateVariants(
  WidgetTester tester,
) => elevationGateVariants(gateL10n(tester));

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

    shownVariant(tester, gateVariants(tester).headlines);
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
    final label = shownVariant(tester, gateVariants(tester).confirms);

    await tester.tap(find.widgetWithText(FilledButton, label));
    expect(decision, isTrue);
  });

  testWidgets('headline and confirm label rotate between launches', (
    tester,
  ) async {
    final seenHeadlines = <String>{};
    final seenConfirms = <String>{};

    for (var seed = 0; seed < 64; seed++) {
      await tester.pumpWidget(
        harness(
          key: ValueKey<int>(seed),
          elevation: const ProcessElevation(elevated: true, kind: 'root'),
          onDecision: (_) {},
          random: Random(seed),
        ),
      );
      final variants = gateVariants(tester);
      seenHeadlines.add(shownVariant(tester, variants.headlines));
      seenConfirms.add(shownVariant(tester, variants.confirms));
    }

    final variants = gateVariants(tester);
    expect(seenHeadlines, variants.headlines.toSet());
    expect(seenConfirms, variants.confirms.toSet());
  });

  testWidgets('the gate surface keeps the shared angular dialog shape', (
    tester,
  ) async {
    await tester.pumpWidget(
      harness(
        elevation: const ProcessElevation(elevated: true, kind: 'root'),
        onDecision: (_) {},
        random: Random(1),
      ),
    );

    // The panel is the only container padded with the large spacing; the
    // badge is the 44x44 warning square.
    final panel = tester.widgetList<Container>(find.byType(Container)).firstWhere(
      (container) => container.padding == const EdgeInsets.all(AppSpacing.lg),
    );
    final badge = tester.widgetList<Container>(find.byType(Container)).firstWhere(
      (container) =>
          container.constraints?.maxWidth == 44 &&
          container.constraints?.maxHeight == 44,
    );

    expect(panel.decoration, isA<ShapeDecoration>());
    expect(
      (panel.decoration as ShapeDecoration).shape,
      isA<BeveledRectangleBorder>(),
      reason: 'the panel must use the shared beveled dialog shape',
    );
    expect(badge.decoration, isA<ShapeDecoration>());
    expect(
      (badge.decoration as ShapeDecoration).shape,
      isA<BeveledRectangleBorder>(),
      reason: 'the warning badge must be beveled like AccountMark',
    );
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
