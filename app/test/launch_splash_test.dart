import 'package:archoera_cx/app/launch_splash.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('launch splash covers the shell and then removes itself', (
    tester,
  ) async {
    await tester.pumpWidget(
      ProviderScope(
        child: MaterialApp(
          theme: AppTheme.dark,
          home: const AppLaunchSplash(
            enabled: true,
            child: Text('shell ready'),
          ),
        ),
      ),
    );

    expect(find.text('shell ready'), findsOneWidget);
    expect(find.byKey(const ValueKey('app-launch-splash')), findsOneWidget);

    await tester.pump(const Duration(milliseconds: 1000));
    expect(find.byKey(const ValueKey('app-launch-splash')), findsOneWidget);

    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('app-launch-splash')), findsNothing);
    expect(find.text('shell ready'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('launch splash is skipped when disabled', (tester) async {
    await tester.pumpWidget(
      ProviderScope(
        child: MaterialApp(
          theme: AppTheme.dark,
          home: const AppLaunchSplash(
            enabled: false,
            child: Text('shell ready'),
          ),
        ),
      ),
    );

    expect(find.text('shell ready'), findsOneWidget);
    expect(find.byKey(const ValueKey('app-launch-splash')), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
