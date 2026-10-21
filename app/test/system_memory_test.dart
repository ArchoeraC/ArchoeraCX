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
    'system.memory crosses the C ABI',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final memory = await client.getSystemMemory();
      expect(memory, isNotNull);
      expect(memory!.isKnown, isTrue);
      expect(memory.totalMb, greaterThan(0));
      expect(memory.availableMb, inInclusiveRange(0, memory.totalMb));
    },
    skip: library.existsSync()
        ? false
        : 'Run the CMake native build to enable the FFI smoke test.',
    timeout: const Timeout(Duration(seconds: 20)),
  );
}
