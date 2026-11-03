import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/pages/java_install_page.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('renders the standalone installer when the core is unavailable', (
    tester,
  ) async {
    final container = ProviderContainer(
      overrides: [coreClientProvider.overrideWithValue(null)],
    );
    addTearDown(container.dispose);

    await tester.pumpWidget(
      UncontrolledProviderScope(
        container: container,
        child: MaterialApp(
          theme: AppTheme.light,
          locale: const Locale('en'),
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: const Scaffold(body: JavaInstallPage()),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Install Java'), findsOneWidget);
    expect(
      find.text('Choose a JDK distribution, version and install location.'),
      findsOneWidget,
    );
    expect(find.text('Could not load the JDK catalogue.'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
