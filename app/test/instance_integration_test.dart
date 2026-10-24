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

      // Per-instance launch options cross the same C++/C# route.
      final tuned = await client.updateInstanceOverrides(
        instanceId: created.id,
        wrapperCommand: 'prime-run',
        environment: <String, String>{'DRI_PRIME': '1'},
        joinServerOnLaunch: true,
        joinServerAddress: 'mc.example.org:25566',
        preLoadCommand: 'echo load',
        preLaunchCommand: 'echo launch',
        postExitCommand: 'echo exit',
      );
      expect(tuned.overrides?.wrapperCommand, 'prime-run');
      expect(tuned.overrides?.environment?['DRI_PRIME'], '1');
      expect(tuned.overrides?.joinServerOnLaunch, isTrue);
      expect(tuned.overrides?.joinServerAddress, 'mc.example.org:25566');
      expect(tuned.overrides?.preLoadCommand, 'echo load');
      expect(tuned.overrides?.preLaunchCommand, 'echo launch');
      expect(tuned.overrides?.postExitCommand, 'echo exit');

      final listed = await client.listInstances();
      expect(listed.any((instance) => instance.id == created.id), isTrue);

      // The heap preview crosses the C++ route into the C# launch module.
      final memory = await client.getLaunchMemory(created.id);
      expect(memory, isNotNull);
      expect(memory!.isAuto, isTrue);
      expect(memory.modCount, 0);
      expect(memory.recommendedMb, greaterThanOrEqualTo(1024));
      expect(memory.systemTotalMb, greaterThan(0));

      final offline = await client.prepareLaunch(created.id);
      expect(offline.ready, isFalse);
      expect(offline.reason, 'ACCOUNT_REQUIRED');
      final online = await client.prepareLaunch(created.id, mode: 'online');
      expect(online.ready, isFalse);
      expect(online.reason, isNotNull);

      final deletion = await client.deleteInstance(created.id);
      expect(deletion.deleted, isTrue);
      expect(deletion.filesDeleted, isFalse);
      expect(Directory(root).existsSync(), isTrue);
      final remaining = await client.listInstances();
      expect(remaining.any((instance) => instance.id == created.id), isFalse);
    },
    skip: enabled && core.existsSync() && module.existsSync()
        ? false
        : 'Set ARCHOERACX_INSTANCE_TESTS=1 and build core/module to run.',
    timeout: const Timeout(Duration(seconds: 20)),
  );

  test(
    'instance.delete with deleteFiles removes a managed instance directory',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      // No explicit rootPath: the instance lands under the configured
      // instances root, which is the only place recursive deletion is allowed.
      final created = await client.createInstance(
        name: 'Files Instance ${DateTime.now().microsecondsSinceEpoch}',
        gameVersion: '1.21.4',
        loader: 'vanilla',
        loaderVersion: '',
      );
      final root = created.rootPath;
      File('$root/options.txt').writeAsStringSync('fov:70');
      expect(Directory(root).existsSync(), isTrue);

      final deletion = await client.deleteInstance(
        created.id,
        deleteFiles: true,
      );
      expect(deletion.deleted, isTrue);
      expect(deletion.filesDeleted, isTrue);
      expect(deletion.filesSkippedReason, isNull);
      expect(Directory(root).existsSync(), isFalse);
    },
    skip: enabled && core.existsSync() && module.existsSync()
        ? false
        : 'Set ARCHOERACX_INSTANCE_TESTS=1 and build core/module to run.',
    timeout: const Timeout(Duration(seconds: 20)),
  );

  test(
    'instance.delete keeps files for a custom root outside the managed root',
    () async {
      final directory = await Directory.systemTemp.createTemp(
        'archoera-instance-outside-',
      );
      addTearDown(() => directory.delete(recursive: true));
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final root = '${directory.path}/outside instance';
      final created = await client.createInstance(
        name: 'Outside Instance',
        gameVersion: '1.21.4',
        loader: 'vanilla',
        loaderVersion: '',
        rootPath: root,
      );
      File('$root/options.txt').writeAsStringSync('fov:70');

      final deletion = await client.deleteInstance(
        created.id,
        deleteFiles: true,
      );
      expect(deletion.deleted, isTrue);
      expect(deletion.filesDeleted, isFalse);
      expect(deletion.filesSkippedReason, 'outside_root');
      expect(Directory(root).existsSync(), isTrue);
    },
    skip: enabled && core.existsSync() && module.existsSync()
        ? false
        : 'Set ARCHOERACX_INSTANCE_TESTS=1 and build core/module to run.',
    timeout: const Timeout(Duration(seconds: 20)),
  );
}
