import 'dart:io';

/// Development fallback used only when the native settings service is not
/// available. The normal path comes from C# `settings.get`.
String defaultDataRoot() {
  final configured = Platform.environment['ARCHOERACX_DATA_DIR'];
  if (configured != null && configured.trim().isNotEmpty) {
    return configured.trim();
  }

  if (Platform.isWindows) {
    final appData = Platform.environment['APPDATA'];
    if (appData != null && appData.trim().isNotEmpty) {
      return '${appData.trim()}\\ArchoeraCX';
    }
  } else if (Platform.isMacOS) {
    final home = Platform.environment['HOME'];
    if (home != null && home.trim().isNotEmpty) {
      return '${home.trim()}/Library/Application Support/ArchoeraCX';
    }
  } else {
    final xdg = Platform.environment['XDG_DATA_HOME'];
    if (xdg != null && xdg.trim().isNotEmpty) {
      return '${xdg.trim()}/archoera-cx';
    }
    final home = Platform.environment['HOME'];
    if (home != null && home.trim().isNotEmpty) {
      return '${home.trim()}/.local/share/archoera-cx';
    }
  }

  return '${Directory.current.path}/.local/archoera-cx';
}

String fallbackInstancesRoot() => '${defaultDataRoot()}/instances';

String joinPath(String parent, String child) {
  final separator = Platform.isWindows ? '\\' : '/';
  return '${parent.replaceAll(RegExp(r'[\\/]+$'), '')}$separator$child';
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
  return slug.isEmpty ? 'instance' : slug;
}
