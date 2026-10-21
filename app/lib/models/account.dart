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

/// The display-only part of a Microsoft device-code challenge. The device code
/// itself remains inside C# and is never returned to Dart.
final class MicrosoftDeviceCodeStart {
  const MicrosoftDeviceCodeStart({
    required this.flowId,
    required this.userCode,
    required this.verificationUri,
    required this.expiresIn,
    required this.interval,
    required this.message,
    this.expiresAt,
  });

  final String flowId;
  final String userCode;
  final String verificationUri;
  final int expiresIn;
  final int interval;
  final String message;
  final DateTime? expiresAt;

  static MicrosoftDeviceCodeStart? fromNative(Object? value) {
    if (value is! Map) return null;
    final flowId = value['flowId']?.toString();
    final userCode = value['userCode']?.toString();
    final verificationUri = value['verificationUri']?.toString();
    if (flowId == null ||
        flowId.isEmpty ||
        userCode == null ||
        userCode.isEmpty ||
        verificationUri == null ||
        verificationUri.isEmpty) {
      return null;
    }
    return MicrosoftDeviceCodeStart(
      flowId: flowId,
      userCode: userCode,
      verificationUri: verificationUri,
      expiresIn: _asInt(value['expiresIn']) ?? 900,
      interval: _asInt(value['interval']) ?? 5,
      message: value['message']?.toString() ?? '',
      expiresAt: DateTime.tryParse(value['expiresAt']?.toString() ?? ''),
    );
  }

  static int? _asInt(Object? value) {
    if (value is int) return value;
    if (value is num) return value.toInt();
    return int.tryParse(value?.toString() ?? '');
  }
}
