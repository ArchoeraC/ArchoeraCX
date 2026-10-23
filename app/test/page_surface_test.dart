import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/app/shell.dart';
import 'package:archoera_cx/pages/java_page.dart';
import 'package:archoera_cx/pages/settings_page.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/widgets/common/page_surface.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues(<String, Object>{});
  });

  Future<GoRouter> pumpShell(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1440, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [coreClientProvider.overrideWithValue(null)],
        child: const ArchoeraApp(),
      ),
    );
    await tester.pumpAndSettle();
    return GoRouter.of(tester.element(find.byType(AppShell)));
  }

  testWidgets('every route paints its own surface', (tester) async {
    final router = await pumpShell(tester);
    expect(find.byType(AppPageSurface), findsOneWidget);

    router.go('/settings/java');
    await tester.pumpAndSettle();

    // The parent settings route stays mounted but offstage; both routes own a
    // surface, so nothing from the settings page can paint above this one.
    expect(find.byType(AppPageSurface), findsOneWidget);
    expect(
      find.byType(AppPageSurface, skipOffstage: false),
      findsAtLeastNWidgets(2),
    );
    expect(
      find.ancestor(
        of: find.byType(JavaPage),
        matching: find.byType(AppPageSurface),
      ),
      findsOneWidget,
    );
    expect(
      find.byType(SettingsPage, skipOffstage: false),
      findsOneWidget,
    );
    expect(find.byType(SettingsPage), findsNothing);
  });

  testWidgets('sidebar ink is bounded to its own item', (tester) async {
    final router = await pumpShell(tester);
    router.go('/settings');
    await tester.pumpAndSettle();

    final tile = find.widgetWithText(ListTile, 'Java');
    expect(tile, findsOneWidget);

    // The nearest material must be the item itself (206x48), not the shared
    // shell material behind the navigator.
    final surface = find.ancestor(of: tile, matching: find.byType(Material));
    final size = tester.getSize(surface.first);
    expect(size.width, lessThan(300));
    expect(size.height, lessThan(60));
  });
}
