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

      // Loader sharing defaults on and survives the C++/C# boundary.
      expect(reloaded.shareLoaderFiles, isTrue);
      final isolated = await client.setRuntimeOptions(shareLoaderFiles: false);
      expect(isolated.shareLoaderFiles, isFalse);
      final afterToggle = await client.getSettings();
      expect(afterToggle.shareLoaderFiles, isFalse);
      // Put the default back so repeated runs stay deterministic.
      await client.setRuntimeOptions(shareLoaderFiles: true);

      // Automatic memory and game optimization are the new defaults and round
      // trip through settings.set/get as well.
      expect(reloaded.memoryAuto, isTrue);
      expect(reloaded.gameOptimization, isTrue);
      final manual = await client.setRuntimeOptions(
        memoryMode: 'manual',
        gameOptimization: false,
      );
      expect(manual.memoryAuto, isFalse);
      expect(manual.gameOptimization, isFalse);
      final afterMemory = await client.getSettings();
      expect(afterMemory.memoryMode, 'manual');
      expect(afterMemory.gameOptimization, isFalse);
      await client.setRuntimeOptions(memoryMode: 'auto', gameOptimization: true);

      // Launch wrapper and environment round trip through settings.set/get.
      final withLaunch = await client.setRuntimeOptions(
        wrapperCommand: 'gamemoderun --keep "my wrapper"',
        environment: <String, String>{
          'DRI_PRIME': '1',
          'MESA_LOADER_DRIVER_OVERRIDE': 'zink',
        },
        preLaunchCommand: 'echo before',
        postExitCommand: 'echo after',
      );
      expect(withLaunch.wrapperCommand, 'gamemoderun --keep "my wrapper"');
      expect(withLaunch.environment['DRI_PRIME'], '1');
      expect(withLaunch.preLaunchCommand, 'echo before');
      expect(withLaunch.postExitCommand, 'echo after');
      final reloadedLaunch = await client.getSettings();
      expect(reloadedLaunch.wrapperCommand, 'gamemoderun --keep "my wrapper"');
      expect(reloadedLaunch.environment['MESA_LOADER_DRIVER_OVERRIDE'], 'zink');
      expect(reloadedLaunch.preLaunchCommand, 'echo before');
      // Explicit null clears; a later run starts from a clean document.
      await client.setRuntimeOptions(
        wrapperCommand: null,
        environment: null,
        preLaunchCommand: null,
        postExitCommand: null,
      );

      // Window mode, Linux performance presets and the separate native-library
      // developer switch round trip too.
      final parity = await client.setRuntimeOptions(
        maximized: true,
        enableGameMode: true,
        enableMangoHud: true,
        useZink: true,
        developerMode: true,
        nativeGlfwPath: Platform.isLinux ? '/bin/true' : null,
      );
      expect(parity.maximized, isTrue);
      expect(parity.fullscreen, isFalse);
      expect(parity.enableGameMode, isTrue);
      expect(parity.enableMangoHud, isTrue);
      expect(parity.useZink, isTrue);
      expect(parity.developerMode, isTrue);
      final reloadedParity = await client.getSettings();
      expect(reloadedParity.maximized, isTrue);
      expect(reloadedParity.developerMode, isTrue);
      await client.setRuntimeOptions(
        maximized: false,
        enableGameMode: false,
        enableMangoHud: false,
        useZink: false,
        developerMode: false,
        nativeGlfwPath: null,
      );
    },
    skip: enabled && core.existsSync() && module.existsSync()
        ? false
        : 'Set ARCHOERACX_SETTINGS_TESTS=1 and build core/module to run.',
    timeout: const Timeout(Duration(seconds: 20)),
  );
}
