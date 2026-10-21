/// Native settings document returned by `settings.get` / `settings.set`.
class AppSettings {
  const AppSettings({
    required this.instancesRoot,
    required this.dataRoot,
    required this.settingsPath,
  });

  final String instancesRoot;
  final String dataRoot;
  final String settingsPath;

  static AppSettings? tryParse(Object? value) {
    if (value is! Map) return null;
    final instancesRoot = _string(value['instancesRoot']);
    final dataRoot = _string(value['dataRoot']);
    if (instancesRoot == null || dataRoot == null) return null;

    return AppSettings(
      instancesRoot: instancesRoot,
      dataRoot: dataRoot,
      settingsPath: _string(value['settingsPath']) ?? '',
    );
  }

  static String? _string(Object? value) {
    if (value == null) return null;
    final text = value.toString().trim();
    return text.isEmpty ? null : text;
  }
}
