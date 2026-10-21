import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final enabled = Platform.environment['ARCHOERACX_INSTANCE_TESTS'] == '1';
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

  test(
    'instance create/list/delete crosses C++ and C# and writes instance.json',
    () async {
      final directory = await Directory.systemTemp.createTemp(
        'archoera-instance-',
      );
      addTearDown(() => directory.delete(recursive: true));
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final root = '${directory.path}/custom instance';
      final created = await client.createInstance(
        name: 'Integration Instance',
        gameVersion: '1.21.4',
        loader: 'fabric',
        loaderVersion: '0.16.10',
        rootPath: root,
      );

      expect(created.name, 'Integration Instance');
      expect(created.rootPath, root);
      expect(File('$root/.archoera/instance.json').existsSync(), isTrue);

      final listed = await client.listInstances();
      expect(listed.any((instance) => instance.id == created.id), isTrue);

      final offline = await client.prepareLaunch(created.id);
      expect(offline.ready, isTrue);
      final online = await client.prepareLaunch(created.id, mode: 'online');
      expect(online.ready, isFalse);
      expect(online.reason, isNotNull);

      expect(await client.deleteInstance(created.id), isTrue);
      final remaining = await client.listInstances();
      expect(remaining.any((instance) => instance.id == created.id), isFalse);
    },
    skip: enabled && core.existsSync() && module.existsSync()
        ? false
        : 'Set ARCHOERACX_INSTANCE_TESTS=1 and build core/module to run.',
    timeout: const Timeout(Duration(seconds: 20)),
  );
}
