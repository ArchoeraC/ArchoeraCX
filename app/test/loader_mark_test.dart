import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/loader_mark.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('draws every loader mark at small and large sizes', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.dark,
        home: Scaffold(
          body: Wrap(
            children: [
              for (final loader in GameLoader.values)
                Padding(
                  padding: const EdgeInsets.all(8),
                  child: Column(
                    children: [
                      LoaderMark(loader: loader, size: 24),
                      LoaderMark(loader: loader, size: 96),
                    ],
                  ),
                ),
            ],
          ),
        ),
      ),
    );

    expect(
      find.byType(LoaderMark),
      findsNWidgets(GameLoader.values.length * 2),
    );
    expect(tester.takeException(), isNull);
  });
}
