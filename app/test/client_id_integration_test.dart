import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

/// Proves the Microsoft client id reached the C# NativeAOT module at compile
/// time. `system.identity` returns only a domain-separated digest, so this
/// test can run on CI without echoing the secret.
///
/// Enabled by `ARCHOERACX_CLIENT_ID_TESTS=1`; release jobs additionally set
/// `ARCHOERACX_REQUIRE_OFFICIAL_BUILD=1`, which fails a tag build if the
/// compiled id does not match the published official digest.
void main() {
  final libraryName = Platform.isWindows
      ? 'archoera_core.dll'
      : Platform.isMacOS
      ? 'libarchoera_core.dylib'
      : 'libarchoera_core.so';
  final library = File('build/native/$libraryName');
  final enabled =
      Platform.environment['ARCHOERACX_CLIENT_ID_TESTS'] == '1' ||
      Platform.environment['ARCHOERACX_REQUIRE_OFFICIAL_BUILD'] == '1';
  final requireOfficial =
      Platform.environment['ARCHOERACX_REQUIRE_OFFICIAL_BUILD'] == '1';

  test(
    'compiled module carries the injected Microsoft client id',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final identity = await client.getBuildIdentity();
      expect(identity, isNotNull);
      expect(
        identity!.channel,
        isNot('unconfigured'),
        reason:
            'build-native did not receive ARCHOERACX_MICROSOFT_CLIENT_ID; '
            'the release would ship without sign-in support.',
      );
      expect(identity.hasClientId, isTrue);
      expect(identity.clientIdSha256, matches(RegExp(r'^[0-9a-f]{64}$')));
      expect(identity.clientIdFingerprint, hasLength(12));
      expect(identity.clientIdSha256, startsWith(identity.clientIdFingerprint!));

      if (requireOfficial) {
        expect(
          identity.official,
          isTrue,
          reason:
              'release builds must use the official Microsoft client id; '
              'update OfficialClientIdSha256 with the rotated id.',
        );
      }
    },
    skip: !enabled
        ? 'Set ARCHOERACX_CLIENT_ID_TESTS=1 after building the native module.'
        : library.existsSync()
        ? false
        : 'Run tool/build-native first; build/native/$libraryName is missing.',
    timeout: const Timeout(Duration(seconds: 30)),
  );
}
