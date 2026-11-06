import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/models/account.dart';
import 'package:archoera_cx/pages/accounts_page.dart';
import 'package:archoera_cx/stores/accounts.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

class _FakeAccounts extends AccountsNotifier {
  static String? selectedId;

  static final _seed = [
    _account('beta', 'BetaStudio2', active: true),
    _account('alt', 'Alt Account'),
  ];

  @override
  Future<List<Account>> build() async => _seed;

  @override
  Future<void> select(
    String id, {
    bool requirePremium = false,
    bool requireMicrosoft = false,
  }) async {
    selectedId = id;
    state = AsyncData([
      for (final account in _seed)
        _account(account.id, account.displayName, active: account.id == id),
    ]);
  }
}

Account _account(String id, String name, {bool active = false}) => Account(
  id: id,
  kind: AccountKind.microsoft,
  displayName: name,
  active: active,
);

Future<void> pumpShell(WidgetTester tester) async {
  SharedPreferences.setMockInitialValues(<String, Object>{});
  tester.view.physicalSize = const Size(1440, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        accountsProvider.overrideWith(_FakeAccounts.new),
      ],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  setUp(() => _FakeAccounts.selectedId = null);

  testWidgets('short tap still opens the accounts page', (tester) async {
    await pumpShell(tester);

    await tester.tap(find.byKey(const ValueKey('command-bar-account')));
    await tester.pumpAndSettle();

    expect(find.byType(AccountsPage), findsOneWidget);
    expect(_FakeAccounts.selectedId, isNull);
  });

  testWidgets('long-press opens the quick switcher', (tester) async {
    await pumpShell(tester);

    await tester.longPress(find.byKey(const ValueKey('command-bar-account')));
    await tester.pumpAndSettle();

    expect(find.text('Alt Account'), findsOneWidget);
    expect(find.text('Manage accounts'), findsOneWidget);

    await tester.tap(find.text('Alt Account'));
    await tester.pumpAndSettle();

    expect(_FakeAccounts.selectedId, 'alt');
    expect(find.textContaining('Switched to Alt Account'), findsOneWidget);
    // Let the toast timer finish before the test binding tears down.
    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('the manage entry opens the accounts page', (tester) async {
    await pumpShell(tester);

    await tester.longPress(find.byKey(const ValueKey('command-bar-account')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Manage accounts'));
    await tester.pumpAndSettle();

    expect(find.byType(AccountsPage), findsOneWidget);
    expect(_FakeAccounts.selectedId, isNull);
  });

  testWidgets('mouse wheel cycles accounts without breaking the tap', (
    tester,
  ) async {
    await pumpShell(tester);

    final center = tester.getCenter(
      find.byKey(const ValueKey('command-bar-account')),
    );
    await tester.sendEventToBinding(
      PointerScrollEvent(position: center, scrollDelta: const Offset(0, 40)),
    );
    await tester.pumpAndSettle();

    expect(_FakeAccounts.selectedId, 'alt');
    expect(find.byType(AccountsPage), findsNothing);
    await tester.pump(const Duration(seconds: 4));
  });
}
