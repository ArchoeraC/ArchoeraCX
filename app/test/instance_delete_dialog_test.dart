import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/stores/instances.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/icons/app_icons.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Instances that live in a real folder, so the delete dialog offers the
/// "also delete local files" option.
class _FakeInstances extends InstancesNotifier {
  @override
  Future<List<GameInstance>> build() async => const [
    GameInstance(
      id: 'inst-deletable',
      name: 'Deletable',
      gameVersion: '1.21.4',
      loader: GameLoader.fabric,
      rootPath: '/tmp/archoera-deletable',
    ),
    GameInstance(
      id: 'inst-other',
      name: 'Other',
      gameVersion: '1.20.1',
      loader: GameLoader.vanilla,
      rootPath: '/tmp/archoera-other',
    ),
  ];
}

Future<void> pumpInstances(WidgetTester tester) async {
  SharedPreferences.setMockInitialValues(<String, Object>{});
  tester.view.physicalSize = const Size(1380, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        instancesProvider.overrideWith(_FakeInstances.new),
      ],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const ValueKey('nav-/instances')));
  await tester.pumpAndSettle();
}

Future<void> openDeleteDialog(WidgetTester tester) async {
  await tester.tap(find.byIcon(AppIcons.more).first);
  await tester.pumpAndSettle();
  await tester.tap(find.text('Delete instance').last);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('delete dialog offers local files but keeps them by default', (
    tester,
  ) async {
    await pumpInstances(tester);
    await openDeleteDialog(tester);

    expect(find.text('Delete “Deletable”?'), findsOneWidget);
    expect(find.text('Also delete local files'), findsOneWidget);
    expect(
      tester.widget<CheckboxListTile>(find.byType(CheckboxListTile)).value,
      isFalse,
    );

    await tester.tap(find.widgetWithText(FilledButton, 'Delete'));
    await tester.pumpAndSettle();

    // No second confirmation when files are kept.
    expect(find.text('Delete local files?'), findsNothing);
    expect(find.text('Deletable'), findsNothing);
    expect(find.text('Other'), findsOneWidget);

    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('deleting local files asks for a second confirmation', (
    tester,
  ) async {
    await pumpInstances(tester);
    await openDeleteDialog(tester);

    await tester.tap(find.byType(CheckboxListTile));
    await tester.pumpAndSettle();
    expect(
      tester.widget<CheckboxListTile>(find.byType(CheckboxListTile)).value,
      isTrue,
    );

    await tester.tap(find.widgetWithText(FilledButton, 'Delete'));
    await tester.pumpAndSettle();

    // The second dialog names the folder and can be cancelled.
    expect(find.text('Delete local files?'), findsOneWidget);
    expect(
      find.textContaining('/tmp/archoera-deletable'),
      findsOneWidget,
    );

    await tester.tap(find.widgetWithText(TextButton, 'Cancel'));
    await tester.pumpAndSettle();

    expect(find.text('Deletable'), findsOneWidget);
    expect(find.text('Delete local files?'), findsNothing);
  });

  testWidgets('confirming twice removes the instance', (tester) async {
    await pumpInstances(tester);
    await openDeleteDialog(tester);

    await tester.tap(find.byType(CheckboxListTile));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Delete'));
    await tester.pumpAndSettle();

    await tester.tap(find.widgetWithText(FilledButton, 'Delete permanently'));
    await tester.pumpAndSettle();

    expect(find.text('Deletable'), findsNothing);
    expect(find.text('Other'), findsOneWidget);

    await tester.pump(const Duration(seconds: 4));
  });
}
