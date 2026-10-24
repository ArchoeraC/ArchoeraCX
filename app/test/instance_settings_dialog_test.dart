import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/models/game_instance.dart';
import 'package:archoera_cx/services/core/java/java_scan.dart';
import 'package:archoera_cx/stores/java.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/theme/icons/app_icons.dart';
import 'package:archoera_cx/widgets/common/toast.dart';
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

/// Fixed scan result so the picker test never needs the native core.
class _FakeJavaRuntimes extends JavaRuntimesNotifier {
  _FakeJavaRuntimes(this.items);

  final List<JavaRuntime> items;

  @override
  Future<List<JavaRuntime>> build() async => items;

  @override
  Future<List<JavaRuntime>> rescan() async {
    state = AsyncData(items);
    return items;
  }
}

Future<void> _open(
  WidgetTester tester,
  GameInstance instance, {
  List<Override> overrides = const <Override>[],
}) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: [coreClientProvider.overrideWithValue(null), ...overrides],
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

Future<void> _drainToasts(WidgetTester tester) async {
  await tester.pump(const Duration(seconds: 4));
  await tester.pumpAndSettle();
}

Future<void> _tapVisible(WidgetTester tester, Finder finder) async {
  await tester.ensureVisible(finder);
  await tester.pumpAndSettle();
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Future<void> _enterVisible(
  WidgetTester tester,
  Finder finder,
  String text,
) async {
  await tester.ensureVisible(finder);
  await tester.pumpAndSettle();
  await tester.enterText(finder, text);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('renders every override field and starts empty', (tester) async {
    await _open(tester, _instance());

    expect(find.text('Instance settings'), findsOneWidget);
    expect(find.text('Minimum memory (MB)'), findsOneWidget);
    expect(find.text('Maximum memory (MB)'), findsOneWidget);
    expect(find.text('Garbage collector'), findsOneWidget);
    expect(find.text('JVM arguments'), findsOneWidget);
    expect(find.text('Game arguments'), findsOneWidget);
    expect(find.text('Java executable'), findsOneWidget);
    expect(find.text('Game window'), findsOneWidget);
    expect(find.text('Window width'), findsOneWidget);
    expect(find.text('Window height'), findsOneWidget);
    expect(find.text('Override wrapper command'), findsOneWidget);
    expect(find.text('Override environment variables'), findsOneWidget);
    expect(find.text('Auto-join'), findsOneWidget);

    // Overrides start off, so their fields are hidden.
    expect(find.text('Wrapper command'), findsNothing);
    expect(find.text('Environment variables'), findsNothing);
    expect(find.text('Server address'), findsNothing);

    // Nothing is overridden, so the GC and window dropdowns inherit.
    expect(find.text('Follow global'), findsWidgets);
    expect(find.text('Automatic'), findsNothing);
  });

  testWidgets('shows the stored overrides', (tester) async {
    await _open(
      tester,
      _instance(
        overrides: const InstanceOverrides(
          memoryMinMb: 1024,
          memoryMaxMb: 4096,
          gcMode: 'g1gc',
          jvmArgs: '-XX:+UseStringDeduplication',
          gameArgs: '--demo',
          wrapperCommand: 'mangohud',
          environment: <String, String>{'DRI_PRIME': '1'},
          joinServerOnLaunch: true,
          joinServerAddress: 'example.com:25565',
          joinWorldName: 'New World',
          javaPath: '/usr/lib/jvm/java-21/bin/java',
          windowWidth: 1600,
          windowHeight: 900,
          fullscreen: true,
        ),
      ),
    );

    expect(find.text('1024'), findsOneWidget);
    expect(find.text('4096'), findsOneWidget);
    expect(find.text('G1GC'), findsOneWidget);
    expect(find.text('-XX:+UseStringDeduplication'), findsOneWidget);
    expect(find.text('--demo'), findsOneWidget);
    expect(find.text('mangohud'), findsOneWidget);
    expect(find.text('DRI_PRIME=1'), findsOneWidget);
    expect(find.text('example.com:25565'), findsOneWidget);
    expect(find.text('New World'), findsOneWidget);
    expect(find.text('/usr/lib/jvm/java-21/bin/java'), findsOneWidget);
    expect(find.text('1600'), findsOneWidget);
    expect(find.text('900'), findsOneWidget);
    expect(find.text('Fullscreen'), findsOneWidget);
  });

  testWidgets('picks a scanned Java runtime from the dropdown', (tester) async {
    await _open(
      tester,
      _instance(),
      overrides: [
        javaRuntimesProvider.overrideWith(
          () => _FakeJavaRuntimes(const <JavaRuntime>[
            JavaRuntime(
              path: '/opt/jdk-21/bin/java',
              version: '21.0.4',
              major: 21,
              vendor: 'Eclipse Adoptium',
              home: '/opt/jdk-21',
            ),
          ]),
        ),
      ],
    );

    await tester.ensureVisible(find.byIcon(AppIcons.chevronDown));
    await tester.pumpAndSettle();
    await tester.tap(find.byIcon(AppIcons.chevronDown));
    await tester.pumpAndSettle();

    expect(find.text('21.0.4'), findsOneWidget);
    await tester.tap(find.text('/opt/jdk-21/bin/java'));
    await tester.pumpAndSettle();

    final field = tester.widget<TextField>(
      find.widgetWithText(TextField, 'Java executable'),
    );
    expect(field.controller?.text, '/opt/jdk-21/bin/java');
    expect(tester.takeException(), isNull);
  });

  testWidgets('rejects out-of-range memory without closing', (tester) async {
    await _open(tester, _instance());

    await tester.enterText(
      find.widgetWithText(TextField, 'Maximum memory (MB)'),
      '999999',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(find.textContaining('128–65536'), findsWidgets);
    expect(find.text('Instance settings'), findsOneWidget);

    await _drainToasts(tester);
  });

  testWidgets('rejects a min larger than max', (tester) async {
    await _open(tester, _instance());

    await tester.enterText(
      find.widgetWithText(TextField, 'Minimum memory (MB)'),
      '8192',
    );
    await tester.enterText(
      find.widgetWithText(TextField, 'Maximum memory (MB)'),
      '1024',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(find.textContaining('128–65536'), findsWidgets);

    await _drainToasts(tester);
  });

  testWidgets('rejects a relative Java path', (tester) async {
    await _open(tester, _instance());

    await tester.enterText(
      find.widgetWithText(TextField, 'Java executable'),
      'bin/java',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(find.textContaining('absolute path'), findsWidgets);

    await _drainToasts(tester);
  });

  testWidgets('shows and validates the environment override inline', (
    tester,
  ) async {
    await _open(tester, _instance());

    await _tapVisible(
      tester,
      find.byKey(const ValueKey('instance-environment-override')),
    );
    await _enterVisible(
      tester,
      find.widgetWithText(TextField, 'Environment variables'),
      'BAD KEY=1',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(find.textContaining('not a valid variable name'), findsWidgets);
    expect(find.text('Instance settings'), findsOneWidget);

    await _drainToasts(tester);
  });

  testWidgets('rejects an invalid auto-join target inline', (tester) async {
    await _open(tester, _instance());

    await _tapVisible(tester, find.byKey(const ValueKey('instance-auto-join')));
    await _enterVisible(
      tester,
      find.widgetWithText(TextField, 'Server address'),
      'example.com:0',
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(
      find.text('Enter host, host:port, [IPv6], or [IPv6]:port.'),
      findsWidgets,
    );
    expect(find.text('Instance settings'), findsOneWidget);

    await _drainToasts(tester);
  });

  testWidgets('auto-join needs a server or a world', (tester) async {
    await _open(tester, _instance());

    await _tapVisible(tester, find.byKey(const ValueKey('instance-auto-join')));
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(
      find.text('Enter a server address or a singleplayer world.'),
      findsWidgets,
    );
    expect(find.text('Instance settings'), findsOneWidget);

    await _drainToasts(tester);
  });

  testWidgets('reports the disconnected core instead of crashing', (
    tester,
  ) async {
    await _open(tester, _instance());

    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(find.textContaining('core'), findsWidgets);
    expect(find.text('Instance settings'), findsOneWidget);

    await _drainToasts(tester);
  });
}
