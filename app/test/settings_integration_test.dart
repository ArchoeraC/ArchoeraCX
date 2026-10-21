import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final enabled = Platform.environment['ARCHOERACX_SETTINGS_TESTS'] == '1';
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
    'settings.get/set crosses C++ and C# and persists the instances root',
    () async {
      final directory = await Directory.systemTemp.createTemp('archoera-settings-');
      addTearDown(() => directory.delete(recursive: true));
      final target = '${directory.path}/instances';

      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final before = await client.getSettings();
      expect(before.dataRoot, isNotEmpty);

      final saved = await client.setInstancesRoot(target);
      expect(saved.instancesRoot, target);

      final reloaded = await client.getSettings();
      expect(reloaded.instancesRoot, target);
      expect(File('${reloaded.dataRoot}/settings.json').existsSync(), isTrue);
    },
    skip: enabled && core.existsSync() && module.existsSync()
        ? false
        : 'Set ARCHOERACX_SETTINGS_TESTS=1 and build core/module to run.',
    timeout: const Timeout(Duration(seconds: 20)),
  );
}
