import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final core = File(
    Platform.isWindows
        ? 'build/native/archoera_core.dll'
        : Platform.isMacOS
        ? 'build/native/libarchoera_core.dylib'
        : 'build/native/libarchoera_core.so',
  );
  final module = File(
    Platform.isWindows
        ? 'build/native/archoera_cs.dll'
        : Platform.isMacOS
        ? 'build/native/libarchoera_cs.dylib'
        : 'build/native/archoera_cs.so',
  );
  final downloader = File(
    Platform.isWindows
        ? 'modules/downloader/target/release/archoera-dl.exe'
        : 'modules/downloader/target/release/archoera-dl',
  );
  final enabled = Platform.environment['ARCHOERACX_NETWORK_TESTS'] == '1';

  test(
    'Modrinth search, plan and Rust download cross the native module',
    () async {
      final root = await Directory.systemTemp.createTemp('archoera-modrinth-');
      addTearDown(() => root.delete(recursive: true));

      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final search = await client.searchModrinth(
        query: 'sodium',
        kind: 'mod',
        gameVersion: '1.21.1',
        loader: 'fabric',
      );
      expect(search.items, isNotEmpty);

      final project = search.items.first;
      final versions = await client.modrinthVersions(
        projectId: project.id,
        kind: 'mod',
        gameVersion: '1.21.1',
        loader: 'fabric',
      );
      expect(versions, isNotEmpty);

      final plan = await client.planModrinthDownload(
        projectId: project.id,
        versionId: versions.first.id,
        kind: 'mod',
        gameVersion: '1.21.1',
        loader: 'fabric',
        root: root.path,
      );
      expect(plan['files'], isA<List<Object?>>());

      final result = await client.executeDownload(plan);
      expect(result.ok, isTrue, reason: result.stderr);
      expect(result.status, 'completed');
    },
    skip:
        enabled &&
            core.existsSync() &&
            module.existsSync() &&
            downloader.existsSync()
        ? false
        : 'Set ARCHOERACX_NETWORK_TESTS=1 and build core/module/downloader to run.',
    timeout: const Timeout(Duration(minutes: 2)),
  );

  test(
    'Modrinth project details and loader versions cross the native module',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final search = await client.searchModrinth(
        query: 'sodium',
        kind: 'mod',
        gameVersion: '1.21.1',
        loader: 'fabric',
      );
      expect(search.items, isNotEmpty);
      final details = await client.modrinthProject(
        projectId: search.items.first.id,
      );
      expect(details.id, search.items.first.id);
      expect(details.gallery, isNotEmpty);

      // Vanilla deliberately returns no remote list; this still exercises the
      // C++ route and the C# registry without depending on a slow mirror.
      final loaders = await client.getLoaderVersions(
        kind: 'vanilla',
        gameVersion: '1.21.1',
      );
      expect(loaders, isEmpty);
    },
    skip: enabled && core.existsSync() && module.existsSync()
        ? false
        : 'Set ARCHOERACX_NETWORK_TESTS=1 and build core/module to run.',
    timeout: const Timeout(Duration(minutes: 2)),
  );
}
