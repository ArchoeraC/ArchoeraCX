import 'dart:ui' as ui;

import 'package:archoera_cx/render/app_shaders.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('a missing scope reads as no shader programs', () {
    expect(AppShaderPrograms.empty.isEmpty, isTrue);
    expect(AppShaderPrograms.empty.transition, isNull);
    expect(AppShaderPrograms.empty.glow, isNull);
  });

  testWidgets('consumers outside a scope get the empty program set', (
    tester,
  ) async {
    late AppShaderPrograms seen;
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) {
            seen = AppShaderScope.of(context);
            expect(AppShaderScope.maybeOf(context), isNull);
            return const SizedBox.shrink();
          },
        ),
      ),
    );
    expect(seen.isEmpty, isTrue);
  });

  testWidgets('every declared program compiles and paints', (tester) async {
    await tester.pumpWidget(
      const AppShaderScope(child: MaterialApp(home: Text('probe'))),
    );
    // Loading is a microtask; one extra frame publishes the result.
    await tester.pump();
    final programs = AppShaderScope.of(tester.element(find.text('probe')));

    expect(programs.transition, isNotNull);
    expect(programs.glow, isNotNull);

    // Painting with the exact uniform layout documented in each `.frag` is
    // the contract test: a mismatched setFloat index fails here instead of
    // rendering garbage in the app.
    await tester.pumpWidget(
      Directionality(
        textDirection: TextDirection.ltr,
        child: CustomPaint(
          size: const Size(320, 180),
          painter: _ProbePainter([
            (programs.transition!, 4),
            (programs.glow!, 10),
          ]),
        ),
      ),
    );
    expect(tester.takeException(), isNull);
  });
}

/// Paints each program once with a full set of uniform values.
class _ProbePainter extends CustomPainter {
  _ProbePainter(this.programs);

  final List<(ui.FragmentProgram, int)> programs;

  @override
  void paint(Canvas canvas, Size size) {
    for (final (program, uniformCount) in programs) {
      final shader = program.fragmentShader();
      try {
        // Values only need to be in range for the shader to run; the point is
        // that the count matches the GLSL declaration.
        for (var index = 0; index < uniformCount; index++) {
          shader.setFloat(index, 0.5);
        }
        canvas.drawRect(Offset.zero & size, Paint()..shader = shader);
      } finally {
        shader.dispose();
      }
    }
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
