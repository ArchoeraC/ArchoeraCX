import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final enabled =
      Platform.environment['ARCHOERACX_MICROSOFT_START_TESTS'] == '1';
  final clientId = Platform.environment['ARCHOERACX_MICROSOFT_CLIENT_ID'];
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
    'runtime client id starts a Microsoft device-code flow',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final start = await client.startMicrosoftLogin();
      expect(start.flowId, isNotEmpty);
      expect(start.userCode, isNotEmpty);
      expect(start.verificationUri, startsWith('https://'));

      await client.cancelMicrosoftLogin(start.flowId);
    },
    skip:
        enabled &&
            clientId != null &&
            clientId.trim().isNotEmpty &&
            core.existsSync() &&
            module.existsSync()
        ? false
        : 'Set ARCHOERACX_MICROSOFT_START_TESTS=1, '
              'ARCHOERACX_MICROSOFT_CLIENT_ID, and build core/module.',
    timeout: const Timeout(Duration(seconds: 45)),
  );
}
