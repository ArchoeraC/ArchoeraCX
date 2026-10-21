import 'dart:io';

import 'package:archoera_cx/models/account.dart';
import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final enabled = Platform.environment['ARCHOERACX_ACCOUNT_TESTS'] == '1';
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
    'offline account crosses C++/C# registry and is persisted',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final account = await client.loginOffline('OfflineTest');
      expect(account.displayName, 'OfflineTest');
      expect(account.kind, AccountKind.offline);

      final listed = await client.listAccounts();
      expect(listed.any((item) => item.id == account.id), isTrue);

      final current = await client.currentAccount();
      expect(current?.id, account.id);

      expect(await client.removeAccount(account.id), isTrue);
      final remaining = await client.listAccounts();
      expect(remaining.any((item) => item.id == account.id), isFalse);
    },
    skip: enabled && core.existsSync() && module.existsSync()
        ? false
        : 'Set ARCHOERACX_ACCOUNT_TESTS=1 and build core/module to run.',
    timeout: const Timeout(Duration(seconds: 20)),
  );
}
