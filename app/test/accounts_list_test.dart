import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/models/account.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/accounts/accounts_list.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets('account row opens its menu on a secondary click', (
    tester,
  ) async {
    const account = Account(
      id: 'offline-1',
      kind: AccountKind.microsoft,
      displayName: 'Steve',
      active: true,
    );

    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        locale: const Locale('en'),
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: Scaffold(
          body: AccountsListSection(
            accounts: const [account],
            loading: false,
            onSelect: (_) {},
            onRefresh: (_) {},
            onRemove: (_) {},
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(
      find.text('Steve'),
      buttons: kSecondaryButton,
      kind: PointerDeviceKind.mouse,
    );
    await tester.pumpAndSettle();

    expect(find.text('Refresh session'), findsOneWidget);
    expect(find.text('Remove account'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
