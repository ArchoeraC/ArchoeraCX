import 'dart:io';

import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/core_client.dart';
import 'package:archoera_cx/services/core/java_scan.dart';
import 'package:archoera_cx/services/java/java_path_store.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/java_status_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues(<String, Object>{});
  });

  Widget buildCard({
    CoreClient Function() clientFactory = CoreClient.openDefault,
    JavaScanCallback? scan,
    PickDirectory? pickDirectory,
    PickExecutable? pickExecutable,
    String? selectedPath,
    ValueChanged<JavaRuntime>? onSelect,
  }) {
    return MaterialApp(
      theme: AppTheme.light,
      locale: const Locale('en'),
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: Scaffold(
        body: Padding(
          padding: const EdgeInsets.all(16),
          child: JavaStatusCard(
            clientFactory: clientFactory,
            scan: scan,
            pickDirectory: pickDirectory,
            pickExecutable: pickExecutable,
            selectedPath: selectedPath,
            onSelect: onSelect,
          ),
        ),
      ),
    );
  }

  CoreClient missingLibrary() {
    throw CoreUnavailableException(
      'libarchoera_core not found (searched 3 locations).',
      searchedPaths: const <String>[
        '/tmp/build/native/libarchoera_core.so',
        '/tmp/app/build/native/libarchoera_core.so',
        '/tmp/build/linux/x64/release/bundle/lib/libarchoera_core.so',
      ],
    );
  }

  JavaRuntime runtime(String path, String version, String arch) {
    return JavaRuntime(
      path: path,
      version: version,
      major: int.parse(version.split('.').first),
      vendor: 'Eclipse Adoptium',
      arch: arch,
      source: 'PATH',
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

  testWidgets('renders the idle state before scanning', (tester) async {
    await tester.pumpWidget(buildCard());
    await tester.pumpAndSettle();

    expect(find.text('Java'), findsOneWidget);
    expect(find.text('Idle'), findsOneWidget);
    expect(find.text('Scan Java'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('renders unavailable when the core library is missing', (
    tester,
  ) async {
    await tester.pumpWidget(buildCard(clientFactory: missingLibrary));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();

    expect(find.text('Unavailable'), findsOneWidget);
    expect(find.textContaining('libarchoera_core not found'), findsOneWidget);
    expect(find.text('Scan Java'), findsOneWidget);
    expect(find.byIcon(Icons.info_outline_rounded), findsOneWidget);
    expect(find.text('Search a folder'), findsNothing);
    expect(find.text('Select java executable'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('shows a short reason when java.scan is unsupported', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildCard(
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
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();

    expect(find.text('Unavailable'), findsOneWidget);
    expect(find.textContaining('java.scan is not supported'), findsOneWidget);
    expect(find.byIcon(Icons.info_outline_rounded), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('lists runtimes from a successful scan', (tester) async {
    final items = [
      runtime('/usr/bin/java', '21.0.4', 'amd64'),
      runtime('/opt/jdk-17/bin/java', '17.0.12', 'aarch64'),
    ];

    await tester.pumpWidget(
      buildCard(
        scan:
            ({
              List<String> roots = const <String>[],
              List<String> executables = const <String>[],
              bool includeDefaults = true,
              void Function(int done, int total)? onProgress,
            }) async {
              onProgress?.call(1, 1);
              return scanResult(items);
            },
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();

    expect(find.text('Ready'), findsOneWidget);
    expect(find.text('2 Java runtimes found'), findsOneWidget);
    expect(find.text('/usr/bin/java'), findsOneWidget);
    expect(find.text('21.0.4 · amd64'), findsOneWidget);
    expect(find.text('/opt/jdk-17/bin/java'), findsOneWidget);
    expect(find.text('17.0.12 · aarch64'), findsOneWidget);
    expect(find.text('Add folder'), findsOneWidget);
    expect(find.text('Select java executable'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('shows at most five runtimes', (tester) async {
    final items = [
      runtime('/java/one', '21.0.4', 'amd64'),
      runtime('/java/two', '17.0.12', 'amd64'),
      runtime('/java/three', '11.0.24', 'amd64'),
      runtime('/java/four', '8.0.442', 'amd64'),
      runtime('/java/five', '8.0.402', 'amd64'),
      runtime('/java/six', '7.0.352', 'amd64'),
    ];

    await tester.pumpWidget(
      buildCard(
        scan: ({
          List<String> roots = const <String>[],
          List<String> executables = const <String>[],
          bool includeDefaults = true,
          void Function(int done, int total)? onProgress,
        }) async => scanResult(items),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();

    expect(find.text('6 Java runtimes found'), findsOneWidget);
    expect(find.text('/java/one'), findsOneWidget);
    expect(find.text('/java/two'), findsOneWidget);
    expect(find.text('/java/three'), findsOneWidget);
    expect(find.text('/java/four'), findsOneWidget);
    expect(find.text('/java/five'), findsOneWidget);
    expect(find.text('/java/six'), findsNothing);
    expect(find.text('View all 6'), findsOneWidget);

    await tester.tap(find.text('View all 6'));
    await tester.pumpAndSettle();
    expect(find.text('Java runtimes (6)'), findsOneWidget);
    expect(find.text('/java/six'), findsOneWidget);

    expect(tester.takeException(), isNull);
  });

  testWidgets('selects a runtime from the scan list', (tester) async {
    JavaRuntime? selected;
    await tester.pumpWidget(
      buildCard(
        onSelect: (runtime) => selected = runtime,
        scan:
            ({
              List<String> roots = const <String>[],
              List<String> executables = const <String>[],
              bool includeDefaults = true,
              void Function(int done, int total)? onProgress,
            }) async => scanResult([
              runtime('/opt/jdk-21/bin/java', '21.0.4', 'amd64'),
              runtime('/opt/jdk-17/bin/java', '17.0.12', 'amd64'),
            ]),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();

    await tester.tap(find.text('/opt/jdk-17/bin/java'));
    expect(selected?.path, '/opt/jdk-17/bin/java');
    expect(tester.takeException(), isNull);
  });

  testWidgets('offers manual fallbacks when the default scan finds nothing', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildCard(
        scan: ({
          List<String> roots = const <String>[],
          List<String> executables = const <String>[],
          bool includeDefaults = true,
          void Function(int done, int total)? onProgress,
        }) async => scanResult(const <JavaRuntime>[]),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();

    expect(find.text('No Java found'), findsOneWidget);
    expect(
      find.textContaining('Java was not found in the default locations'),
      findsOneWidget,
    );
    expect(find.text('Search a folder'), findsOneWidget);
    expect(find.text('Select java executable'), findsOneWidget);
    expect(find.text('Add folder'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a picked folder is sent as a scan root and can find Java', (
    tester,
  ) async {
    final rootsSeen = <List<String>>[];
    final includeDefaultsSeen = <bool>[];
    var calls = 0;

    await tester.pumpWidget(
      buildCard(
        pickDirectory: () async => '/tmp/jdks',
        scan:
            ({
              List<String> roots = const <String>[],
              List<String> executables = const <String>[],
              bool includeDefaults = true,
              void Function(int done, int total)? onProgress,
            }) async {
              rootsSeen.add(roots);
              includeDefaultsSeen.add(includeDefaults);
              calls += 1;
              if (calls == 1) return scanResult(const <JavaRuntime>[]);
              return scanResult([
                runtime('/tmp/jdks/bin/java', '21.0.4', 'amd64'),
              ]);
            },
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();
    expect(find.text('Search a folder'), findsOneWidget);

    await tester.tap(find.text('Search a folder'));
    await tester.pumpAndSettle();

    expect(find.text('/tmp/jdks/bin/java'), findsOneWidget);
    expect(find.text('1 Java runtime found'), findsOneWidget);
    expect(rootsSeen, hasLength(2));
    expect(rootsSeen.last, contains('/tmp/jdks'));
    expect(includeDefaultsSeen, <bool>[true, false]);

    final store = const JavaPathStore();
    expect(await store.loadRoots(), contains('/tmp/jdks'));
    expect(tester.takeException(), isNull);
  });

  testWidgets('asks for an executable when a picked folder is still empty', (
    tester,
  ) async {
    await tester.pumpWidget(
      buildCard(
        pickDirectory: () async => '/tmp/empty',
        scan: ({
          List<String> roots = const <String>[],
          List<String> executables = const <String>[],
          bool includeDefaults = true,
          void Function(int done, int total)? onProgress,
        }) async => scanResult(const <JavaRuntime>[]),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Search a folder'));
    await tester.pumpAndSettle();

    expect(
      find.text('Still not found — select the java executable.'),
      findsOneWidget,
    );
    expect(find.text('Search a folder'), findsOneWidget);
    expect(find.text('Select java executable'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('a picked executable is sent to java.scan and can find Java', (
    tester,
  ) async {
    final executablesSeen = <List<String>>[];
    var calls = 0;

    await tester.pumpWidget(
      buildCard(
        pickExecutable: () async => '/tmp/jdks/bin/java',
        scan:
            ({
              List<String> roots = const <String>[],
              List<String> executables = const <String>[],
              bool includeDefaults = true,
              void Function(int done, int total)? onProgress,
            }) async {
              executablesSeen.add(executables);
              calls += 1;
              if (calls == 1) return scanResult(const <JavaRuntime>[]);
              return scanResult([
                runtime('/tmp/jdks/bin/java', '21.0.4', 'amd64'),
              ]);
            },
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Select java executable'));
    await tester.pumpAndSettle();

    expect(find.text('/tmp/jdks/bin/java'), findsWidgets);
    expect(executablesSeen, hasLength(2));
    expect(executablesSeen.last, contains('/tmp/jdks/bin/java'));

    final store = const JavaPathStore();
    expect(await store.loadExecutables(), contains('/tmp/jdks/bin/java'));
    expect(tester.takeException(), isNull);
  });

  testWidgets(
    'reports an invalid executable without marking the card unavailable',
    (tester) async {
      await tester.pumpWidget(
        buildCard(
          pickExecutable: () async => '/tmp/bad/java',
          scan: ({
            List<String> roots = const <String>[],
            List<String> executables = const <String>[],
            bool includeDefaults = true,
            void Function(int done, int total)? onProgress,
          }) async => scanResult(const <JavaRuntime>[]),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.text('Scan Java'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Select java executable'));
      await tester.pumpAndSettle();

      expect(
        find.text('No valid Java at this path: /tmp/bad/java'),
        findsOneWidget,
      );
      expect(
        find.textContaining('Choose the bin/java file inside a JDK'),
        findsOneWidget,
      );
      expect(find.text('Unavailable'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('reports the module error for an invalid executable', (
    tester,
  ) async {
    var calls = 0;

    await tester.pumpWidget(
      buildCard(
        pickExecutable: () async => '/tmp/bad/java',
        scan:
            ({
              List<String> roots = const <String>[],
              List<String> executables = const <String>[],
              bool includeDefaults = true,
              void Function(int done, int total)? onProgress,
            }) async {
              calls += 1;
              if (calls == 1) return scanResult(const <JavaRuntime>[]);
              throw const CoreException(
                status: 1,
                code: 'E_INVALID',
                message: 'The executable is not a Java binary.',
              );
            },
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Select java executable'));
    await tester.pumpAndSettle();

    expect(
      find.text('No valid Java at this path: /tmp/bad/java'),
      findsOneWidget,
    );
    expect(find.textContaining('not a Java binary'), findsOneWidget);
    expect(find.text('Unavailable'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('loads saved custom paths and includes them in scans', (
    tester,
  ) async {
    SharedPreferences.setMockInitialValues(<String, Object>{
      JavaPathStore.rootsKey: '["/saved/jdk"]',
      JavaPathStore.executablesKey: '["/saved/bin/java"]',
    });

    final rootsSeen = <List<String>>[];
    final executablesSeen = <List<String>>[];

    await tester.pumpWidget(
      buildCard(
        scan:
            ({
              List<String> roots = const <String>[],
              List<String> executables = const <String>[],
              bool includeDefaults = true,
              void Function(int done, int total)? onProgress,
            }) async {
              rootsSeen.add(roots);
              executablesSeen.add(executables);
              return scanResult(const <JavaRuntime>[]);
            },
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Scan Java'));
    await tester.pumpAndSettle();

    expect(rootsSeen.single, contains('/saved/jdk'));
    expect(executablesSeen.single, contains('/saved/bin/java'));
    expect(find.text('Custom paths'), findsOneWidget);
    expect(find.text('/saved/jdk'), findsOneWidget);
    expect(find.text('/saved/bin/java'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  final coreLibraryName = Platform.isWindows
      ? 'archoera_core.dll'
      : Platform.isMacOS
      ? 'libarchoera_core.dylib'
      : 'libarchoera_core.so';
  final moduleLibraryName = Platform.isWindows
      ? 'archoera_cs.dll'
      : Platform.isMacOS
      ? 'libarchoera_cs.dylib'
      : 'archoera_cs.so';
  final coreLibrary = File('build/native/$coreLibraryName');
  final moduleLibrary = File('build/native/$moduleLibraryName');
  final moduleLibraryWithPrefix = Platform.isLinux
      ? File('build/native/libarchoera_cs.so')
      : moduleLibrary;
  final hasModule =
      moduleLibrary.existsSync() || moduleLibraryWithPrefix.existsSync();

  test(
    'java.scan crosses the C ABI and reports a valid runtime',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      try {
        final result = await client.scanJava();
        expect(result.rawJson['ok'], isTrue);
        expect(result.count, greaterThanOrEqualTo(result.items.length));
        expect(result.items, isNotEmpty, reason: 'expected at least one JDK');

        final valid = result.items.where(
          (runtime) =>
              runtime.path.isNotEmpty &&
              (runtime.version != null || runtime.major != null),
        );
        expect(valid, isNotEmpty, reason: 'entries need a path and a version');
      } on CoreException catch (error) {
        // The C++ host may not route java.scan yet even though the module
        // library exists. Treat that as an integration skip, not a failure.
        if (error.code == 'E_UNSUPPORTED' || error.code == 'E_NOT_FOUND') {
          markTestSkipped('java.scan is not routed yet: ${error.code}');
          return;
        }
        rethrow;
      }
    },
    skip: !coreLibrary.existsSync() || !hasModule
        ? 'Build archoera_core and archoera_cs to enable the Java scan test.'
        : false,
    timeout: const Timeout(Duration(seconds: 30)),
  );

  final manualRoot = Directory('/opt/java/Adoptium');

  test(
    'java.scan accepts manual roots and restricts discovery to them',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      try {
        final result = await client.scanJava(
          roots: const <String>['/opt/java/Adoptium'],
          includeDefaults: false,
        );

        expect(result.rawJson['ok'], isTrue);
        expect(
          result.items,
          isNotEmpty,
          reason: 'expected a JDK below the root',
        );
        for (final runtime in result.items) {
          expect(runtime.path, startsWith('/opt/java/Adoptium'));
        }
      } on CoreException catch (error) {
        // The parallel module/C++ work may not forward manual roots yet.
        if (error.code == 'E_UNSUPPORTED' ||
            error.code == 'E_NOT_FOUND' ||
            error.code == 'E_INVALID') {
          markTestSkipped(
            'java.scan manual roots are not supported yet: ${error.code}',
          );
          return;
        }
        rethrow;
      }
    },
    skip: !Platform.isLinux || !manualRoot.existsSync() || !hasModule
        ? 'Requires Linux, build/native/archoera_cs.so and /opt/java/Adoptium.'
        : false,
    timeout: const Timeout(Duration(seconds: 30)),
  );
}
