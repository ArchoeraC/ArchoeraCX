import 'dart:io';

/// Development fallback used only when the native settings service is not
/// available. The normal path comes from C# `settings.get`.
String defaultDataRoot() {
  String? systemTemp;
  try {
    systemTemp = Directory.systemTemp.path;
  } catch (_) {
    // Fall through to the CWD/relative fallback below.
  }
  return resolveDefaultDataRoot(
    Platform.environment,
    isWindows: Platform.isWindows,
    isMacOs: Platform.isMacOS,
    systemTempPath: systemTemp,
  );
}

/// Pure resolution used by tests and [defaultDataRoot].
///
/// Precedence matches C# `AppDataPaths`: `ARCHOERACX_DATA_DIR`, then the
/// platform directory variable, then the user profile, then
/// `Directory.systemTemp`. No OS path is hard-coded as a fallback.
String resolveDefaultDataRoot(
  Map<String, String> environment, {
  required bool isWindows,
  required bool isMacOs,
  String? systemTempPath,
}) {
  final configured = environment['ARCHOERACX_DATA_DIR'];
  if (configured != null && configured.trim().isNotEmpty) {
    return configured.trim();
  }

  final separator = isWindows ? '\\' : '/';
  if (isWindows) {
    final appData = environment['APPDATA'];
    if (appData != null && appData.trim().isNotEmpty) {
      return joinPath(appData.trim(), 'ArchoeraCX', separator: separator);
    }
  } else if (isMacOs) {
    final home = environment['HOME'];
    if (home != null && home.trim().isNotEmpty) {
      return joinPath(
        joinPath(
          joinPath(home.trim(), 'Library', separator: separator),
          'Application Support',
          separator: separator,
        ),
        'ArchoeraCX',
        separator: separator,
      );
    }
  } else {
    final xdg = environment['XDG_DATA_HOME'];
    // The XDG spec says relative values are invalid; ignore them instead of
    // resolving against the process working directory.
    if (xdg != null && xdg.trim().startsWith('/')) {
      return joinPath(xdg.trim(), 'archoera-cx', separator: separator);
    }
    final home = environment['HOME'];
    if (home != null && home.trim().isNotEmpty) {
      return joinPath(
        joinPath(
          joinPath(home.trim(), '.local', separator: separator),
          'share',
          separator: separator,
        ),
        'archoera-cx',
        separator: separator,
      );
    }
  }

  final temp = systemTempPath?.trim();
  if (temp != null && temp.isNotEmpty) {
    return joinPath(temp, 'archoera-cx', separator: separator);
  }

  try {
    final cwd = Directory.current.path;
    if (cwd.isNotEmpty) {
      return joinPath(
        joinPath(cwd, '.local', separator: separator),
        'archoera-cx',
        separator: separator,
      );
    }
  } catch (_) {
    // The process may have been started from a deleted working directory.
  }

  return 'archoera-cx';
}

String fallbackInstancesRoot() => joinPath(defaultDataRoot(), 'instances');

String joinPath(String parent, String child, {String? separator}) {
  final sep = separator ?? (Platform.isWindows ? '\\' : '/');
  return '${parent.replaceAll(RegExp(r'[\\/]+$'), '')}$sep$child';
}

String instanceRoot({
  String? explicitRoot,
  required String instancesRoot,
  required String id,
}) {
  final root = explicitRoot?.trim();
  return root != null && root.isNotEmpty ? root : joinPath(instancesRoot, id);
}

/// Maximum display-name length, matching C# `ValidateName` and
/// `InstanceFolderName.MaxLength`.
const int instanceNameMaxLength = 80;

/// Characters Windows cannot store in a file name. SJMCL rejects these in the
/// name field; we keep the same set so a name is either usable as-is or
/// reported instead of being silently rewritten.
const String _forbiddenInstanceNameChars = r'\/:*?"<>|';

final RegExp _reservedInstanceName = RegExp(
  r'^(con|prn|aux|nul|com[1-9]|lpt[1-9])(\..*)?$',
  caseSensitive: false,
);

/// Why a manually entered instance name cannot become a folder name.
enum InstanceNameProblem {
  empty,
  tooLong,
  invalidCharacters,
  invalidEdges,
  reservedName,
}

/// Validates a manual instance name before it is sent to C#.
///
/// Mirrors SJMCL's directory-name rule: Unicode and ordinary punctuation are
/// allowed; only separators, Windows-reserved characters/device names and
/// leading/trailing dots are rejected.
InstanceNameProblem? instanceNameProblem(String value) {
  final name = value.trim();
  if (name.isEmpty) return InstanceNameProblem.empty;
  if (name.length > instanceNameMaxLength) {
    return InstanceNameProblem.tooLong;
  }
  if (name.startsWith('.') || name.endsWith('.')) {
    return InstanceNameProblem.invalidEdges;
  }

  for (final rune in name.runes) {
    if (rune < 0x20 || rune == 0x7f) {
      return InstanceNameProblem.invalidCharacters;
    }
    if (rune < 0x80 &&
        _forbiddenInstanceNameChars.contains(String.fromCharCode(rune))) {
      return InstanceNameProblem.invalidCharacters;
    }
  }

  if (_reservedInstanceName.hasMatch(name)) {
    return InstanceNameProblem.reservedName;
  }

  return null;
}

/// Folder name used below `versions/` (or the instances root).
///
/// This is the Dart mirror of C# `InstanceFolderName.FromDisplayName`: keep
/// Unicode and punctuation, turn separators into `-`, trim leading/trailing
/// dots and spaces, and avoid Windows device names. A valid manual name passes
/// through unchanged.
String instanceFolderName(String value) {
  final buffer = StringBuffer();
  for (final rune in value.trim().runes) {
    if (buffer.length >= instanceNameMaxLength) break;
    if (rune < 0x20 || rune == 0x7f) continue;

    final character = String.fromCharCode(rune);
    buffer.write(
      rune < 0x80 && _forbiddenInstanceNameChars.contains(character)
          ? '-'
          : character,
    );
  }

  var folder = buffer.toString();
  folder = folder.replaceFirst(RegExp(r'^[ .]+'), '');
  folder = folder.replaceFirst(RegExp(r'[ .]+$'), '');
  if (folder.isEmpty || RegExp(r'^\.+$').hasMatch(folder)) {
    return 'instance';
  }

  return _reservedInstanceName.hasMatch(folder) ? '_$folder' : folder;
}
