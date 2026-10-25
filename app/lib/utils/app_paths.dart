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

String slugForInstance(String name) {
  final slug = name
      .trim()
      .toLowerCase()
      .replaceAll(RegExp(r'[^a-z0-9._-]+'), '-')
      .replaceAll(RegExp(r'-+'), '-')
      .replaceAll(RegExp(r'^-|-$'), '');
  // Reject separators: "."/".."/"..." would otherwise make the instance root
  // resolve to the instances directory or its parent.
  if (slug.isEmpty || RegExp(r'^\.+$').hasMatch(slug)) return 'instance';
  return slug;
}
