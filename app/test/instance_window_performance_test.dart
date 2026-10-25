import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/stores/platform.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/app_dropdown.dart';
import 'package:archoera_cx/widgets/common/toast.dart';
import 'package:archoera_cx/widgets/instances/instance_performance_overrides.dart';
import 'package:archoera_cx/widgets/instances/instance_settings_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart';
import 'package:flutter_test/flutter_test.dart';

GameInstance _instance({InstanceOverrides? overrides}) => GameInstance(
  id: 'instance-1',
  name: 'Test instance',
  gameVersion: '1.21.4',
  loader: GameLoader.fabric,
  loaderVersion: '0.16.10',
  rootPath: '/tmp/archoera-instance',
  overrides: overrides,
);

Future<void> _open(
  WidgetTester tester,
  GameInstance instance, {
  Size size = const Size(1100, 900),
  bool linux = true,
  Future<({int width, int height})?> Function()? resolveSize,
  List<Override> overrides = const <Override>[],
}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        linuxPlatformProvider.overrideWithValue(linux),
        maximizedWindowSizeProvider.overrideWithValue(
          resolveSize ?? () async => (width: 1920, height: 1080),
        ),
        ...overrides,
      ],
      child: MaterialApp(
        theme: AppTheme.light,
        locale: const Locale('en'),
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: ToastOverlay(
          child: Builder(
            builder: (context) => Scaffold(
              body: Center(
                child: TextButton(
                  onPressed: () =>
                      showInstanceSettingsDialog(context, instance),
                  child: const Text('Open'),
                ),
              ),
            ),
          ),
        ),
      ),
    ),
  );

  await tester.tap(find.text('Open'));
  await tester.pumpAndSettle();
}

Future<void> _selectWindowMode(WidgetTester tester, String label) async {
  final dropdown = find.byKey(const ValueKey('instance-window-mode'));
  await tester.ensureVisible(dropdown);
  await tester.pumpAndSettle();
  await tester.tap(dropdown);
  await tester.pumpAndSettle();
  await tester.tap(find.text(label).last);
  await tester.pumpAndSettle();
}

String _fieldText(WidgetTester tester, String label) {
  final field = find.widgetWithText(TextField, label);
  return tester.widget<TextField>(field).controller!.text;
}

void main() {
  testWidgets('maximized resolves the usable display size into the fields', (
    tester,
  ) async {
    await _open(tester, _instance());

    await _selectWindowMode(tester, 'Maximized');

    expect(_fieldText(tester, 'Window width'), '1920');
    expect(_fieldText(tester, 'Window height'), '1080');
    expect(tester.takeException(), isNull);
  });

  testWidgets('a failed display lookup keeps the typed window size', (
    tester,
  ) async {
    await _open(tester, _instance(), resolveSize: () async => null);

    await tester.enterText(
      find.widgetWithText(TextField, 'Window width'),
      '1280',
    );
    await tester.enterText(
      find.widgetWithText(TextField, 'Window height'),
      '720',
    );
    await _selectWindowMode(tester, 'Maximized');

    expect(_fieldText(tester, 'Window width'), '1280');
    expect(_fieldText(tester, 'Window height'), '720');
    expect(tester.takeException(), isNull);
  });

  testWidgets('reads a stored maximized override as the maximized option', (
    tester,
  ) async {
    await _open(
      tester,
      _instance(
        overrides: const InstanceOverrides(
          maximized: true,
          windowWidth: 2560,
          windowHeight: 1440,
        ),
      ),
    );

    expect(
      find.text(
        'Set from the primary display\'s usable size when you choose maximized.',
      ),
      findsWidgets,
    );
    expect(_fieldText(tester, 'Window width'), '2560');
    expect(tester.takeException(), isNull);
  });

  testWidgets('performance overrides are inherit/on/off on Linux', (
    tester,
  ) async {
    await _open(tester, _instance());

    expect(find.byType(InstancePerformanceOverrides), findsOneWidget);
    expect(find.text('Feral GameMode'), findsOneWidget);
    expect(find.text('MangoHud'), findsOneWidget);
    expect(find.text('Zink'), findsOneWidget);

    // Explicit off must survive as a distinguishable third state.
    final zink = find.byKey(const ValueKey('instance-zink-override'));
    await tester.ensureVisible(zink);
    await tester.pumpAndSettle();
    await tester.tap(zink);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Off').last);
    await tester.pumpAndSettle();

    expect(
      tester.widget<AppDropdown<String>>(zink).value,
      InstancePerformanceOverrides.off,
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('performance overrides stay off the portable dialog', (
    tester,
  ) async {
    await _open(tester, _instance(), linux: false);

    expect(find.byType(InstancePerformanceOverrides), findsNothing);
    expect(find.text('Feral GameMode'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('window and performance rows fit a narrow dialog', (
    tester,
  ) async {
    await _open(tester, _instance(), size: const Size(420, 700));

    await _selectWindowMode(tester, 'Maximized');
    expect(find.byType(InstancePerformanceOverrides), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
