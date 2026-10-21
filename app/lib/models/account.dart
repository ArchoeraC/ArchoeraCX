/// Account metadata mirrored from the C# account store. Tokens and refresh
/// credentials never cross the native ABI.
enum AccountKind { microsoft, offline, authlib }

final class Account {
  const Account({
    required this.id,
    required this.kind,
    required this.displayName,
    required this.active,
    this.uuid,
    this.xuid,
    this.skinUrl,
    this.capeUrl,
    this.createdAt,
    this.updatedAt,
    this.lastLoginAt,
  });

  final String id;
  final AccountKind kind;
  final String displayName;
  final bool active;
  final String? uuid;
  final String? xuid;
  final String? skinUrl;
  final String? capeUrl;
  final DateTime? createdAt;
  final DateTime? updatedAt;
  final DateTime? lastLoginAt;

  static Account? fromNative(Object? value) {
    if (value is! Map) return null;
    final id = value['id']?.toString();
    final displayName = (value['displayName'] ?? value['name'])
        ?.toString()
        .trim();
    if (id == null ||
        id.isEmpty ||
        displayName == null ||
        displayName.isEmpty) {
      return null;
    }
    return Account(
      id: id,
      kind: _parseKind(value['kind']?.toString()),
      displayName: displayName,
      active: value['active'] == true,
      uuid: _stringOrNull(value['uuid']),
      xuid: _stringOrNull(value['xuid']),
      skinUrl: _stringOrNull(value['skinUrl']),
      capeUrl: _stringOrNull(value['capeUrl']),
      createdAt: _dateOrNull(value['createdAt']),
      updatedAt: _dateOrNull(value['updatedAt']),
      lastLoginAt: _dateOrNull(value['lastLoginAt']),
    );
  }

  static AccountKind _parseKind(String? value) => switch (value) {
    'microsoft' => AccountKind.microsoft,
    'authlib' => AccountKind.authlib,
    _ => AccountKind.offline,
  };

  static String? _stringOrNull(Object? value) {
    final text = value?.toString().trim();
    return text == null || text.isEmpty ? null : text;
  }

  static DateTime? _dateOrNull(Object? value) {
    final text = _stringOrNull(value);
    return text == null ? null : DateTime.tryParse(text);
  }
}

/// The flow used to sign a Microsoft account in.
enum MicrosoftLoginFlow {
  /// Browser-based authorization code flow with a loopback redirect.
  authorizationCode,

  /// Legacy device-code flow; the user enters a short code in the browser.
  deviceCode,
}

/// The display-only part of a Microsoft sign-in challenge. Device codes,
/// authorization codes and tokens remain inside C# and never cross the ABI.
///
/// Depending on [mode] either the device-code fields ([userCode],
/// [verificationUri]) or the authorization-code fields ([authorizationUrl],
/// [redirectUri]) are populated.
final class MicrosoftDeviceCodeStart {
  const MicrosoftDeviceCodeStart({
    required this.flowId,
    this.mode = MicrosoftLoginFlow.deviceCode,
    this.userCode = '',
    this.verificationUri = '',
    required this.expiresIn,
    required this.interval,
    required this.message,
    this.authorizationUrl,
    this.redirectUri,
    this.expiresAt,
  });

  final String flowId;
  final MicrosoftLoginFlow mode;
  final String userCode;
  final String verificationUri;
  final int expiresIn;
  final int interval;
  final String message;
  final String? authorizationUrl;
  final String? redirectUri;
  final DateTime? expiresAt;

  /// Whether the browser must be opened at [authorizationUrl].
  bool get isAuthorizationCode => mode == MicrosoftLoginFlow.authorizationCode;

  static MicrosoftDeviceCodeStart? fromNative(Object? value) {
    if (value is! Map) return null;
    final flowId = value['flowId']?.toString();
    if (flowId == null || flowId.isEmpty) return null;

    final userCode = value['userCode']?.toString().trim() ?? '';
    final verificationUri = value['verificationUri']?.toString().trim() ?? '';
    final authorizationUrl = value['authorizationUrl']?.toString().trim() ?? '';
    final redirectUri = value['redirectUri']?.toString().trim() ?? '';
    final modeText = value['mode']?.toString().trim() ?? '';

    final isAuthorizationCode =
        modeText == 'authorization_code' ||
        (modeText.isEmpty && authorizationUrl.isNotEmpty && userCode.isEmpty);

    if (isAuthorizationCode) {
      if (authorizationUrl.isEmpty) return null;
    } else if (userCode.isEmpty || verificationUri.isEmpty) {
      return null;
    }

    return MicrosoftDeviceCodeStart(
      flowId: flowId,
      mode: isAuthorizationCode
          ? MicrosoftLoginFlow.authorizationCode
          : MicrosoftLoginFlow.deviceCode,
      userCode: userCode,
      verificationUri: verificationUri,
      expiresIn:
          _asInt(value['expiresIn']) ?? (isAuthorizationCode ? 300 : 900),
      interval: _asInt(value['interval']) ?? 5,
      message: value['message']?.toString() ?? '',
      authorizationUrl: authorizationUrl.isEmpty ? null : authorizationUrl,
      redirectUri: redirectUri.isEmpty ? null : redirectUri,
      expiresAt: DateTime.tryParse(value['expiresAt']?.toString() ?? ''),
    );
  }

  static int? _asInt(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    return int.tryParse(value?.toString() ?? '');
  }
}
