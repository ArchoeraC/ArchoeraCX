import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final libraryName = Platform.isWindows
      ? 'archoera_core.dll'
      : Platform.isMacOS
      ? 'libarchoera_core.dylib'
      : 'libarchoera_core.so';
  final library = File('build/native/$libraryName');

  test(
    'system.pingAsync crosses the C ABI',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final progress = <(int, int)>[];
      final result = await client.pingAsync(
        onProgress: (done, total) => progress.add((done, total)),
      );

      expect(result.pong, isTrue);
      expect(result.method, 'system.pingAsync');
      expect(result.rawJson['ok'], isTrue);
      expect(result.data['pong'], isTrue);

      expect(progress, isNotEmpty, reason: 'the async task must emit progress');
      expect(progress.last.$2, greaterThan(0));
      expect(progress.last.$1, greaterThan(0));
    },
    skip: library.existsSync()
        ? false
        : 'Run the CMake native build to enable the FFI smoke test.',
    timeout: const Timeout(Duration(seconds: 20)),
  );
}
