import 'dart:io';

import 'package:archoera_cx/models/account.dart';
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

  /// The UI opens the browser with the default flow, so the default must be
  /// the authorization-code challenge; device code stays available as the
  /// explicit fallback.
  test(
    'default flow starts a Microsoft authorization-code challenge',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final start = await client.startMicrosoftLogin();
      addTearDown(() => client.cancelMicrosoftLogin(start.flowId));

      expect(start.flowId, isNotEmpty);
      expect(start.mode, MicrosoftLoginFlow.authorizationCode);
      expect(start.isAuthorizationCode, isTrue);
      expect(start.userCode, isEmpty);
      expect(
        start.authorizationUrl,
        startsWith('https://login.microsoftonline.com/'),
      );
      expect(start.authorizationUrl, contains('client_id='));
      expect(start.authorizationUrl, contains('code_challenge='));
      expect(start.authorizationUrl, contains('code_challenge_method=S256'));
      expect(start.redirectUri, startsWith('http://localhost:'));
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

  test(
    'cancel returns immediately and aborts a pending poll',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final start = await client.startMicrosoftLogin();
      final poll = client.pollMicrosoftLogin(start.flowId);
      await Future<void>.delayed(const Duration(milliseconds: 100));

      final stopwatch = Stopwatch()..start();
      await client
          .cancelMicrosoftLogin(start.flowId)
          .timeout(const Duration(seconds: 2));
      stopwatch.stop();

      expect(stopwatch.elapsed, lessThan(const Duration(seconds: 2)));
      await expectLater(poll, throwsA(isA<CoreException>()));
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

  test(
    'cancel after the loopback callback aborts the token exchange',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final start = await client.startMicrosoftLogin();
      final authorizationUrl = Uri.parse(start.authorizationUrl!);
      final state = authorizationUrl.queryParameters['state'];
      expect(state, isNotNull);
      final redirect = Uri.parse(start.redirectUri!);

      final poll = client.pollMicrosoftLogin(start.flowId);
      await Future<void>.delayed(const Duration(milliseconds: 150));

      final http = HttpClient();
      final request = await http.getUrl(
        Uri.parse(
          'http://localhost:${redirect.port}/callback'
          '?code=bogus-code&state=${Uri.encodeQueryComponent(state!)}',
        ),
      );
      final response = await request.close();
      await response.drain<void>();
      http.close(force: true);

      final stopwatch = Stopwatch()..start();
      await client
          .cancelMicrosoftLogin(start.flowId)
          .timeout(const Duration(seconds: 3));
      stopwatch.stop();
      expect(stopwatch.elapsed, lessThan(const Duration(seconds: 3)));

      await expectLater(poll, throwsA(isA<CoreException>()));
      final closeWatch = Stopwatch()..start();
      client.close();
      closeWatch.stop();
      expect(closeWatch.elapsed, lessThan(const Duration(seconds: 3)));
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

  test(
    'device code flow still issues a user code',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final start = await client.startMicrosoftLogin(flow: 'device_code');
      addTearDown(() => client.cancelMicrosoftLogin(start.flowId));

      expect(start.flowId, isNotEmpty);
      expect(start.mode, MicrosoftLoginFlow.deviceCode);
      expect(start.isAuthorizationCode, isFalse);
      expect(start.userCode, isNotEmpty);
      expect(start.verificationUri, startsWith('https://'));
      expect(start.interval, greaterThan(0));
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
