/// Launch-process options shared by global settings and per-instance
/// overrides: wrapper command, child environment and auto-join targets.
///
/// The C# owner remains authoritative for the wire contract. The helpers here
/// only exist so the UI can reject obvious mistakes with a field-level message
/// before a save crosses the ABI.
library;

/// Maximum length of a wrapper command line.
const int launchWrapperCommandMaxLength = 4096;

/// Maximum number of environment entries.
const int launchEnvironmentMaxEntries = 64;

/// Maximum environment key length.
const int launchEnvironmentKeyMaxLength = 128;

/// Maximum environment value length.
const int launchEnvironmentValueMaxLength = 4096;

/// Variables that would change how the JVM or launcher processes are invoked
/// in ways the launch plan cannot reason about.
const Set<String> reservedLaunchEnvironmentKeys = <String>{
  'JAVA_TOOL_OPTIONS',
  '_JAVA_OPTIONS',
  'JDK_JAVA_OPTIONS',
  'CLASSPATH',
};

final RegExp _environmentKeyPattern = RegExp(r'^[A-Za-z_][A-Za-z0-9_]*$');

enum LaunchEnvironmentIssueKind {
  missingSeparator,
  invalidKey,
  duplicateKey,
  reservedKey,
  tooManyEntries,
  invalidValue,
}

/// The first problem found while parsing a `KEY=VALUE` block.
class LaunchEnvironmentIssue {
  const LaunchEnvironmentIssue(this.kind, {this.line, this.key});

  final LaunchEnvironmentIssueKind kind;

  /// 1-based line number, when the problem belongs to one line.
  final int? line;
  final String? key;
}

/// Parsed environment block, or the first issue that stopped parsing.
class LaunchEnvironmentParseResult {
  const LaunchEnvironmentParseResult._(this.values, this.issue);

  factory LaunchEnvironmentParseResult.valid(Map<String, String> values) =>
      LaunchEnvironmentParseResult._(values, null);

  factory LaunchEnvironmentParseResult.invalid(LaunchEnvironmentIssue issue) =>
      LaunchEnvironmentParseResult._(const <String, String>{}, issue);

  final Map<String, String> values;
  final LaunchEnvironmentIssue? issue;

  bool get isValid => issue == null;
}

/// Parse one `KEY=VALUE` per line. Blank lines are ignored; values keep every
/// character after the first `=`, so `A=b=c` stores `b=c`.
LaunchEnvironmentParseResult parseLaunchEnvironment(String text) {
  final values = <String, String>{};
  final lines = text.split('\n');
  for (var index = 0; index < lines.length; index++) {
    var line = lines[index];
    if (line.endsWith('\r')) line = line.substring(0, line.length - 1);
    if (line.trim().isEmpty) continue;

    final lineNumber = index + 1;
    final separator = line.indexOf('=');
    if (separator < 0) {
      return LaunchEnvironmentParseResult.invalid(
        LaunchEnvironmentIssue(
          LaunchEnvironmentIssueKind.missingSeparator,
          line: lineNumber,
        ),
      );
    }

    final key = line.substring(0, separator);
    final value = line.substring(separator + 1);
    if (key.isEmpty ||
        key.length > launchEnvironmentKeyMaxLength ||
        !_environmentKeyPattern.hasMatch(key)) {
      return LaunchEnvironmentParseResult.invalid(
        LaunchEnvironmentIssue(
          LaunchEnvironmentIssueKind.invalidKey,
          line: lineNumber,
          key: key,
        ),
      );
    }
    if (values.containsKey(key)) {
      return LaunchEnvironmentParseResult.invalid(
        LaunchEnvironmentIssue(
          LaunchEnvironmentIssueKind.duplicateKey,
          line: lineNumber,
          key: key,
        ),
      );
    }
    if (reservedLaunchEnvironmentKeys.contains(key)) {
      return LaunchEnvironmentParseResult.invalid(
        LaunchEnvironmentIssue(
          LaunchEnvironmentIssueKind.reservedKey,
          line: lineNumber,
          key: key,
        ),
      );
    }
    if (value.length > launchEnvironmentValueMaxLength ||
        value.contains('\u0000')) {
      return LaunchEnvironmentParseResult.invalid(
        LaunchEnvironmentIssue(
          LaunchEnvironmentIssueKind.invalidValue,
          line: lineNumber,
          key: key,
        ),
      );
    }

    values[key] = value;
    if (values.length > launchEnvironmentMaxEntries) {
      return LaunchEnvironmentParseResult.invalid(
        const LaunchEnvironmentIssue(LaunchEnvironmentIssueKind.tooManyEntries),
      );
    }
  }
  return LaunchEnvironmentParseResult.valid(
    Map<String, String>.unmodifiable(values),
  );
}

/// Render a map back into the multiline field format.
String formatLaunchEnvironment(Map<String, String> environment) => environment
    .entries
    .map((entry) => '${entry.key}=${entry.value}')
    .join('\n');

/// Parse an `environment` value from the wire without trusting its shape.
///
/// Non-string keys or values are skipped; `null`/non-maps mean "not present".
Map<String, String>? parseLaunchEnvironmentWire(Object? value) {
  if (value is! Map) return null;
  final result = <String, String>{};
  for (final entry in value.entries) {
    final key = entry.key;
    final value = entry.value;
    if (key is String && value is String) result[key] = value;
  }
  return Map<String, String>.unmodifiable(result);
}

/// True when [value] can be passed to the process starter as a wrapper line.
bool isValidLaunchWrapperCommand(String value) {
  if (value.length > launchWrapperCommandMaxLength) return false;
  return !value.contains('\u0000') &&
      !value.contains('\r') &&
      !value.contains('\n');
}

/// Validate `host`, `host:port`, `[v6]` and `[v6]:port`.
bool isValidJoinServerAddress(String value) {
  if (value.isEmpty ||
      value.length > 255 ||
      _hasForbiddenAddressCharacter(value)) {
    return false;
  }

  if (value.startsWith('[')) {
    final closing = value.indexOf(']');
    if (closing <= 1) return false;
    final host = value.substring(1, closing);
    if (!_isIpv6Literal(host)) return false;
    final rest = value.substring(closing + 1);
    if (rest.isEmpty) return true;
    if (!rest.startsWith(':')) return false;
    return _isValidPort(rest.substring(1));
  }

  if (value.contains('[') || value.contains(']')) return false;
  final firstColon = value.indexOf(':');
  final lastColon = value.lastIndexOf(':');
  // An unbracketed IPv6 address is ambiguous with host:port.
  if (firstColon != lastColon) return false;

  final hasPort = lastColon >= 0;
  final host = hasPort ? value.substring(0, lastColon) : value;
  if (host.isEmpty || host.contains(':')) return false;
  if (!_isValidHost(host)) return false;
  return !hasPort || _isValidPort(value.substring(lastColon + 1));
}

/// Validate a singleplayer world folder name: 1..128 characters, no path
/// separators, parent-directory segments or control characters.
bool isValidJoinWorldName(String value) {
  if (value.trim().isEmpty || value.length > 128) return false;
  if (value.contains('/') || value.contains('\\') || value.contains('..')) {
    return false;
  }
  for (final unit in value.codeUnits) {
    if (_isControlCode(unit)) return false;
  }
  return true;
}

bool _isValidPort(String value) {
  if (!RegExp(r'^[0-9]{1,5}$').hasMatch(value)) return false;
  final port = int.parse(value);
  return port >= 1 && port <= 65535;
}

bool _isValidHost(String host) {
  if (host.isEmpty) return false;
  for (final unit in host.codeUnits) {
    if (unit <= 0x20 || _isControlCode(unit)) return false;
  }
  return !host.contains('/') && !host.contains('\\');
}

bool _hasForbiddenAddressCharacter(String value) {
  for (final unit in value.codeUnits) {
    if (unit <= 0x20 || _isControlCode(unit)) return true;
  }
  return value.contains('/') || value.contains('\\');
}

bool _isControlCode(int unit) => unit < 0x20 || (unit >= 0x7f && unit <= 0x9f);

bool _isIpv6Literal(String host) {
  if (!host.contains(':')) return false;
  final parts = host.split(':');
  if (parts.length > 9) return false;

  var groups = 0;
  var compressed = false;
  for (var index = 0; index < parts.length; index++) {
    final part = parts[index];
    if (part.isEmpty) {
      compressed = true;
      continue;
    }
    if (index == parts.length - 1 &&
        RegExp(r'^[0-9]{1,3}(\.[0-9]{1,3}){3}$').hasMatch(part)) {
      groups += 2;
      continue;
    }
    if (!RegExp(r'^[0-9A-Fa-f]{1,4}$').hasMatch(part)) return false;
    groups++;
  }
  return compressed ? groups <= 7 : groups == 8;
}
