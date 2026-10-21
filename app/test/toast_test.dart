import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/toast.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('shows and dismisses a toast above the page', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.dark,
        home: ToastOverlay(
          child: Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () => toast('Saved', type: ToastType.success),
                child: const Text('Show'),
              ),
            ),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Show'));
    await tester.pump();
    expect(find.text('Saved'), findsOneWidget);

    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
    expect(find.text('Saved'), findsNothing);
  });
}
