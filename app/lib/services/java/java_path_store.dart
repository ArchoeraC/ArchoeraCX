import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

/// Persists user-supplied Java scan paths.
///
/// Values are stored as JSON string arrays under [rootsKey] and
/// [executablesKey]. The store contains no business logic: it only normalizes
/// empty/duplicate entries so a manual path is sent to `java.scan` once.
final class JavaPathStore {
  const JavaPathStore();

  static const String rootsKey = 'java.roots';
  static const String executablesKey = 'java.executables';

  /// Directories chosen by the user, scanned recursively by the module.
  Future<List<String>> loadRoots() => _load(rootsKey);

  /// Persist [roots], dropping blanks and duplicates.
  Future<void> saveRoots(List<String> roots) => _save(rootsKey, roots);

  /// Java executables chosen by the user.
  Future<List<String>> loadExecutables() => _load(executablesKey);

  /// Persist [executables], dropping blanks and duplicates.
  Future<void> saveExecutables(List<String> executables) =>
      _save(executablesKey, executables);

  Future<List<String>> _load(String key) async {
    final preferences = await SharedPreferences.getInstance();
    final raw = preferences.getString(key);
    if (raw == null || raw.trim().isEmpty) return const <String>[];

    Object? decoded;
    try {
      decoded = jsonDecode(raw);
    } on FormatException {
      return const <String>[];
    }

    if (decoded is! List) return const <String>[];
    return _normalize(decoded.whereType<String>());
  }

  Future<void> _save(String key, Iterable<String> values) async {
    final preferences = await SharedPreferences.getInstance();
    await preferences.setString(key, jsonEncode(_normalize(values)));
  }

  static List<String> _normalize(Iterable<String> values) {
    final seen = <String>{};
    final result = <String>[];

    for (final value in values) {
      final path = value.trim();
      if (path.isEmpty || !seen.add(path)) continue;
      result.add(path);
    }

    return List<String>.unmodifiable(result);
  }
}
