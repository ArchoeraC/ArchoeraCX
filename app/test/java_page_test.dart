import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/pages/java_page.dart';
import 'package:archoera_cx/services/core/core_client.dart';
import 'package:archoera_cx/services/core/java_scan.dart';
import 'package:archoera_cx/services/java/java_path_store.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/stores/settings.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/theme/icons/app_icons.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues(<String, Object>{});
  });

  JavaRuntime runtime(String path, String version, String arch) {
    return JavaRuntime(
      path: path,
      version: version,
      major: int.parse(version.split('.').first),
      vendor: 'Eclipse Adoptium',
      arch: arch,
      source: 'SYSTEM',
      home: path.replaceAll('/bin/java', ''),
    );
  }

  JavaScanResult scanResult(List<JavaRuntime> items) {
    return JavaScanResult(
      items: items,
      count: items.length,
      rawJson: const <String, Object?>{'ok': true},
      schemaVersion: 1,
    );
  }

  Future<ProviderContainer> pumpPage(
    WidgetTester tester, {
    required JavaScanCallback scan,
    PickDirectory? pickDirectory,
    PickExecutable? pickExecutable,
    Size size = const Size(1280, 900),
  }) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

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
          home: Scaffold(
            body: JavaPage(
              scan: scan,
              pathStore: const JavaPathStore(),
              pickDirectory: pickDirectory,
              pickExecutable: pickExecutable,
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    return container;
  }

  testWidgets('tiles every scanned runtime without a dialog', (tester) async {
    final items = [
      for (var index = 1; index <= 6; index++)
        runtime('/opt/jdk-$index/bin/java', '$index.0.$index', 'amd64'),
    ];

    await pumpPage(tester, scan: _scanOf(scanResult(items)));

    expect(find.text('6 Java runtimes found'), findsOneWidget);
    for (var index = 1; index <= 6; index++) {
      expect(find.text('/opt/jdk-$index'), findsOneWidget);
    }
    // The old "view all" dialog (and its trigger) is gone.
    expect(find.textContaining('View all'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('opens the Java runtime install picker', (tester) async {
    await pumpPage(
      tester,
      scan: _scanOf(
        scanResult([runtime('/opt/jdk-21/bin/java', '21.0.1', 'amd64')]),
      ),
    );

    await tester.tap(find.text('Install Java'));
    await tester.pumpAndSettle();

    expect(find.text('Install a Java runtime'), findsOneWidget);
    // Java 21 also labels the scanned runtime, so only assert presence.
    expect(find.text('Java 8'), findsWidgets);
    expect(find.text('Java 17'), findsWidgets);
    expect(find.text('Java 21'), findsWidgets);
    expect(find.text('Java 25'), findsWidgets);
    expect(find.text('Recommended'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('keeps a long list readable on a narrow window', (tester) async {
    final items = [
      for (var index = 1; index <= 66; index++)
        JavaRuntime(
          path: '/opt/java/some-fairly-long-vendor-name/jdk-$index/bin/java',
          version: '$index.0.${index + 1}',
          major: index,
          vendor: 'Some Fairly Long Vendor Name '.padRight(40, 'x'),
          arch: 'amd64',
          source: 'SYSTEM',
          home: '/opt/java/some-fairly-long-vendor-name/jdk-$index',
        ),
    ];

    await pumpPage(
      tester,
      scan: _scanOf(scanResult(items)),
      size: const Size(720, 900),
    );

    expect(find.text('66 Java runtimes found'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('filters the flattened list', (tester) async {
    await pumpPage(
      tester,
      scan: _scanOf(
        scanResult([
          runtime('/opt/jdk-21/bin/java', '21.0.4', 'amd64'),
          runtime('/opt/jdk-17/bin/java', '17.0.12', 'aarch64'),
        ]),
      ),
    );

    await tester.enterText(find.byType(TextField), '17');
    await tester.pumpAndSettle();

    expect(find.text('1 Java runtime found'), findsOneWidget);
    expect(find.text('/opt/jdk-21'), findsNothing);
    expect(find.text('/opt/jdk-17'), findsOneWidget);

    await tester.enterText(find.byType(TextField), 'nope');
    await tester.pumpAndSettle();
    expect(find.text('No Java matches this filter.'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('selecting a runtime persists the manual choice', (tester) async {
    final container = await pumpPage(
      tester,
      scan: _scanOf(
        scanResult([
          runtime('/opt/jdk-21/bin/java', '21.0.4', 'amd64'),
          runtime('/opt/jdk-17/bin/java', '17.0.12', 'amd64'),
        ]),
      ),
    );

    await tester.tap(find.text('/opt/jdk-17'));
    await tester.pumpAndSettle();

    final settings = container.read(appSettingsProvider).value;
    expect(settings!.javaAuto, isFalse);
    expect(settings.javaPath, '/opt/jdk-17/bin/java');
    expect(find.text('In use'), findsOneWidget);
    expect(tester.takeException(), isNull);

    // Let the success toast timer expire before the test ends.
    await tester.pump(const Duration(seconds: 4));
  });

  testWidgets('auto selection stays available from the page', (tester) async {
    final container = await pumpPage(
      tester,
      scan: _scanOf(
        scanResult([runtime('/opt/jdk-21/bin/java', '21.0.4', 'amd64')]),
      ),
    );

    expect(container.read(appSettingsProvider).value!.javaAuto, isTrue);

    await tester.tap(find.byType(Switch));
    await tester.pumpAndSettle();
    expect(container.read(appSettingsProvider).value!.javaAuto, isFalse);

    await tester.tap(find.byType(Switch));
    await tester.pumpAndSettle();
    expect(container.read(appSettingsProvider).value!.javaAuto, isTrue);
    expect(find.text('In use'), findsNothing);
    expect(tester.takeException(), isNull);

    // Let the toast timers expire before the test ends.
    await tester.pump(const Duration(seconds: 8));
  });

  testWidgets('offers manual fallbacks when nothing is found', (tester) async {
    await pumpPage(tester, scan: _scanOf(scanResult(const <JavaRuntime>[])));

    expect(find.text('No Java found'), findsOneWidget);
    expect(find.text('Add folder'), findsOneWidget);
    expect(find.text('Select java executable'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('explains an unavailable scan', (tester) async {
    await pumpPage(
      tester,
      scan:
          ({
            List<String> roots = const <String>[],
            List<String> executables = const <String>[],
            bool includeDefaults = true,
            void Function(int done, int total)? onProgress,
          }) async {
            throw const CoreException(
              status: 7,
              code: 'E_UNSUPPORTED',
              message: 'java.scan is not supported by this core build.',
            );
          },
    );

    expect(find.textContaining('java.scan is not supported'), findsOneWidget);
    expect(find.byIcon(AppIcons.info), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('lists and removes custom scan paths', (tester) async {
    SharedPreferences.setMockInitialValues(<String, Object>{
      JavaPathStore.rootsKey: '["/custom/jdks"]',
      JavaPathStore.executablesKey: '["/custom/java/bin/java"]',
    });

    await pumpPage(
      tester,
      scan: _scanOf(
        scanResult([runtime('/opt/jdk-21/bin/java', '21.0.4', 'amd64')]),
      ),
    );

    expect(find.text('Custom paths'), findsOneWidget);
    expect(find.text('/custom/jdks'), findsOneWidget);
    expect(find.text('/custom/java/bin/java'), findsOneWidget);

    await tester.tap(find.byTooltip('Remove /custom/jdks'));
    await tester.pumpAndSettle();
    expect(find.text('/custom/jdks'), findsNothing);
    expect(find.text('/custom/java/bin/java'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}

/// Wraps a fixed result in the page's scan callback signature.
JavaScanCallback _scanOf(JavaScanResult result) {
  return ({
    List<String> roots = const <String>[],
    List<String> executables = const <String>[],
    bool includeDefaults = true,
    void Function(int done, int total)? onProgress,
  }) async {
    onProgress?.call(1, 1);
    return result;
  };
}
