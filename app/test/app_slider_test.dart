import 'package:archoera_cx/theme/tokens.dart';
import 'package:archoera_cx/widgets/common/app_slider.dart';
import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Future<void> pumpSlider(
    WidgetTester tester, {
    required double value,
    required ValueChanged<double> onChanged,
    int? divisions,
    Map<double, String>? marks,
  }) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: ThemeData.dark().copyWith(extensions: const [AppTokens.dark]),
        home: Scaffold(
          body: Center(
            child: SizedBox(
              width: 240,
              child: AppSlider(
                value: value,
                min: 0,
                max: 80,
                divisions: divisions,
                valueLabel: '${value.round()}',
                valueFormatter: (value) => '${value.round()}',
                semanticLabel: 'Blur',
                marks: marks,
                onChanged: onChanged,
              ),
            ),
          ),
        ),
      ),
    );
  }

  testWidgets('paints a flat track with a 16 px thumb', (tester) async {
    await pumpSlider(tester, value: 40, onChanged: (_) {});

    expect(
      tester.getSize(find.byKey(const ValueKey('app-slider-thumb'))),
      const Size.square(16),
    );
    expect(tester.getSize(find.byType(AppSlider)), const Size(240, 28));
  });

  testWidgets('optionally prints the range under the track', (tester) async {
    await pumpSlider(
      tester,
      value: 40,
      marks: {0.0: '0', 40.0: '40', 80.0: '80'},
      onChanged: (_) {},
    );

    expect(tester.getSize(find.byType(AppSlider)), const Size(240, 48));
    expect(find.text('0'), findsOneWidget);
    expect(find.text('40'), findsOneWidget);
    expect(find.text('80'), findsOneWidget);

    // Labels sit under the track and stay inside the slider's own width.
    final slider = tester.getRect(find.byType(AppSlider));
    final min = tester.getRect(find.text('0'));
    final mid = tester.getRect(find.text('40'));
    final max = tester.getRect(find.text('80'));
    expect(min.top, greaterThanOrEqualTo(slider.top + 28));
    expect(min.left, closeTo(slider.left, 0.5));
    expect(max.right, closeTo(slider.right, 0.5));
    expect(mid.center.dx, closeTo(slider.center.dx, 0.5));
  });

  testWidgets('tapping the track jumps to that position', (tester) async {
    final changes = <double>[];
    await pumpSlider(tester, value: 0, onChanged: changes.add);

    final rect = tester.getRect(find.byType(AppSlider));
    await tester.tapAt(Offset(rect.center.dx, rect.center.dy));

    expect(changes, isNotEmpty);
    expect(changes.last, closeTo(40, 0.001));
  });

  testWidgets('dragging to the end reaches the maximum', (tester) async {
    final changes = <double>[];
    await pumpSlider(tester, value: 0, onChanged: changes.add);

    final rect = tester.getRect(find.byType(AppSlider));
    await tester.dragFrom(
      Offset(rect.left + 1, rect.center.dy),
      Offset(rect.width + 40, 0),
    );

    expect(changes, isNotEmpty);
    expect(changes.last, 80);
  });

  testWidgets('snaps to the division grid while dragging', (tester) async {
    final changes = <double>[];
    await pumpSlider(
      tester,
      value: 0,
      divisions: 8,
      onChanged: changes.add,
    );

    final rect = tester.getRect(find.byType(AppSlider));
    // 54% of 240 px maps to 43.2, which is nearest to the 40 step.
    await tester.tapAt(Offset(rect.left + rect.width * 0.54, rect.center.dy));

    expect(changes, isNotEmpty);
    expect(changes.last, 40);
  });

  testWidgets('exposes its value to accessibility services', (tester) async {
    final handle = tester.ensureSemantics();
    await pumpSlider(tester, value: 40, onChanged: (_) {});

    final data = tester.getSemantics(find.byType(AppSlider)).getSemanticsData();
    expect(data.label, 'Blur');
    expect(data.value, '40');
    expect(data.flagsCollection.isSlider, isTrue);
    expect(data.hasAction(SemanticsAction.increase), isTrue);
    expect(data.hasAction(SemanticsAction.decrease), isTrue);
    handle.dispose();
  });
}
