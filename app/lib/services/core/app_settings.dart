/// Native settings document returned by `settings.get` / `settings.set`.
class AppSettings {
  const AppSettings({
    required this.instancesRoot,
    required this.dataRoot,
    required this.settingsPath,
    this.instancesRoots = const <String>[],
  });

  /// Primary/legacy root. Always the first entry of [instancesRoots].
  final String instancesRoot;

  /// All configured instance roots; first is the default for new instances.
  final List<String> instancesRoots;

  final String dataRoot;
  final String settingsPath;

  /// Normalized roots with a legacy fallback for older native modules.
  List<String> get roots =>
      instancesRoots.isEmpty ? <String>[instancesRoot] : instancesRoots;

  static AppSettings? tryParse(Object? value) {
    if (value is! Map) return null;
    final instancesRoot = _string(value['instancesRoot']);
    final dataRoot = _string(value['dataRoot']);
    if (instancesRoot == null || dataRoot == null) return null;

    final roots = <String>[];
    final rawRoots = value['instancesRoots'];
    if (rawRoots is List) {
      for (final item in rawRoots) {
        final root = _string(item);
        if (root != null && !roots.contains(root)) roots.add(root);
      }
    }
    if (roots.isEmpty) roots.add(instancesRoot);
    if (!roots.contains(instancesRoot)) roots.insert(0, instancesRoot);

    return AppSettings(
      instancesRoot: instancesRoot,
      instancesRoots: List<String>.unmodifiable(roots),
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
