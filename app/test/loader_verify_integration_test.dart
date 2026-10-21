import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final coreName = Platform.isWindows
      ? 'archoera_core.dll'
      : Platform.isMacOS
      ? 'libarchoera_core.dylib'
      : 'libarchoera_core.so';
  final moduleName = Platform.isWindows
      ? 'archoera_cs.dll'
      : Platform.isMacOS
      ? 'libarchoera_cs.dylib'
      : 'archoera_cs.so';
  final core = File('build/native/$coreName');
  final module = File('build/native/$moduleName');
  final sample = File(
    '../.local/forge-samples/forge-1.20.1-47.2.0-installer.jar',
  );

  test(
    'loader.verify crosses C++/C# and reports the unlisted PATCHED artifact',
    () async {
      final root = await Directory.systemTemp.createTemp('archoera-loader-');
      addTearDown(() => root.delete(recursive: true));
      Directory(File('${root.path}/libraries').path).createSync(recursive: true);

      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final result = await client.verifyLoader(
        installerPath: sample.absolute.path,
        root: root.absolute.path,
        kind: 'forge',
      );

      expect(result['ok'], isFalse);
      final data = result['data'] as Map<String, Object?>;
      final manifest = data['manifest'] as Map<String, Object?>;
      final artifacts = manifest['artifacts'] as List<Object?>;
      final patched = artifacts
          .whereType<Map<String, Object?>>()
          .singleWhere((artifact) => artifact['key'] == 'PATCHED');

      expect(patched['requiredForLaunch'], isTrue);
      expect(patched['producerIndex'], 5);
      expect(patched['sha1'], '3e175b011146785588f1649a20d1834d10282a7c');

      final verification = data['verification'] as Map<String, Object?>;
      expect(verification['ok'], isFalse);
      expect(verification['missing'], isNotEmpty);
    },
    skip: core.existsSync() && module.existsSync() && sample.existsSync()
        ? false
        : 'Run build-core.sh/build-cs.sh and cache the Forge sample to enable this test.',
    timeout: const Timeout(Duration(seconds: 30)),
  );
}
