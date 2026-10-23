import 'dart:convert';

import '../../../models/account.dart';
import '../core_transport.dart';

/// Account domain binding for the native core: the Microsoft sign-in flows
/// (`account.login.microsoft.*`), the offline local login and the vault-backed
/// account registry (`account.list/current/select/refresh/remove`).
///
/// Tokens and device codes never cross the ABI; the Dart side only ever sees
/// typed envelopes and opaque flow ids.
mixin CoreClientAccounts on CoreTransport {
  /// Begin Microsoft sign-in. The returned object never contains codes or
  /// tokens; call [pollMicrosoftLogin] with [flowId].
  ///
  /// [flow] selects the backend flow: `authorization_code` (default, opens the
  /// system browser and waits for the loopback callback) or `device_code`.
  Future<MicrosoftDeviceCodeStart> startMicrosoftLogin({
    String flow = 'authorization_code',
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.login.microsoft.start';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'flow': flow}),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    final start = MicrosoftDeviceCodeStart.fromNative(data);
    if (start == null) {
      throw CoreTransport.invalidEnvelope(
        methodName,
        'no usable sign-in challenge',
      );
    }
    return start;
  }

  /// Wait for the user to finish Microsoft authorization and the full
  /// Xbox Live → XSTS → Minecraft chain. This intentionally blocks until the
  /// C# module completes, expires, or is cancelled.
  Future<Account> pollMicrosoftLogin(
    String flowId, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.login.microsoft.poll';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'flowId': flowId}),
      onProgress: onProgress,
    );
    return _parseAccountEnvelope(this, jsonText, methodName);
  }

  Future<void> cancelMicrosoftLogin(String flowId) async {
    const methodName = 'account.login.microsoft.cancel';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'flowId': flowId}),
    );
    decodeEnvelope(jsonText, fallbackMethod: methodName);
  }

  Future<List<Account>> listAccounts({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.list';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return List<Account>.unmodifiable(
      asList(data['items']).map(Account.fromNative).whereType<Account>(),
    );
  }

  Future<Account?> currentAccount({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.current';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return Account.fromNative(data['account']);
  }

  Future<Account> selectAccount(
    String id, {
    bool requirePremium = false,
    bool requireMicrosoft = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.select';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'id': id,
        'requirePremium': requirePremium,
        'requireMicrosoft': requireMicrosoft,
      }),
      onProgress: onProgress,
    );
    return _parseAccountEnvelope(this, jsonText, methodName);
  }

  Future<Account> loginOffline(
    String name, {
    bool requirePremium = false,
    bool requireMicrosoft = false,
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.login.offline';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{
        'name': name,
        'requirePremium': requirePremium,
        'requireMicrosoft': requireMicrosoft,
      }),
      onProgress: onProgress,
    );
    return _parseAccountEnvelope(this, jsonText, methodName);
  }

  Future<Account> refreshAccount(
    String id, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.refresh';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'id': id}),
      onProgress: onProgress,
    );
    return _parseAccountEnvelope(this, jsonText, methodName);
  }

  Future<bool> removeAccount(
    String id, {
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'account.remove';
    final jsonText = await runAsync(
      methodName,
      paramsJson: jsonEncode(<String, Object?>{'id': id}),
      onProgress: onProgress,
    );
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final data = asMap(decoded['data'], methodName);
    return data['deleted'] == true;
  }
}

Account _parseAccountEnvelope(
  CoreTransport transport,
  String jsonText,
  String methodName,
) {
  final decoded = transport.decodeEnvelope(jsonText, fallbackMethod: methodName);
  final data = transport.asMap(decoded['data'], methodName);
  final account = Account.fromNative(data['account']);
  if (account == null) {
    throw CoreTransport.invalidEnvelope(methodName, 'no account object');
  }
  return account;
}
