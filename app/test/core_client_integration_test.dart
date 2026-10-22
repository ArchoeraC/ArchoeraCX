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

  test(
    'system.appearance returns a typed schema',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final appearance = await client.getSystemAppearance();
      expect(appearance, isNotNull);
      expect(appearance!.theme, anyOf('light', 'dark', 'unknown'));
      expect(appearance.source, isNotEmpty);
    },
    skip: library.existsSync()
        ? false
        : 'Run the CMake native build to enable the FFI smoke test.',
    timeout: const Timeout(Duration(seconds: 20)),
  );

  test(
    'system.openUrl rejects non-http urls before touching the desktop',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      expect(await client.openExternalUrl('file:///etc/passwd'), isFalse);
      expect(await client.openExternalUrl('javascript:alert(1)'), isFalse);
    },
    skip: library.existsSync()
        ? false
        : 'Run the CMake native build to enable the FFI smoke test.',
    timeout: const Timeout(Duration(seconds: 20)),
  );

  test(
    'system.identity reports a client id digest, never the id',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final identity = await client.getBuildIdentity();
      expect(identity, isNotNull);
      expect(identity!.channel, anyOf('official', 'custom', 'unconfigured'));
      expect(identity.official, identity.channel == 'official');

      if (identity.hasClientId) {
        expect(identity.clientIdSha256, matches(RegExp(r'^[0-9a-f]{64}$')));
        expect(identity.clientIdFingerprint, hasLength(12));
        expect(
          identity.clientIdSha256,
          startsWith(identity.clientIdFingerprint!),
        );
      } else {
        expect(identity.channel, 'unconfigured');
        expect(identity.official, isFalse);
      }
    },
    skip: library.existsSync()
        ? false
        : 'Run the CMake native build to enable the FFI smoke test.',
    timeout: const Timeout(Duration(seconds: 20)),
  );
}
