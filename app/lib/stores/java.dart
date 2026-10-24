import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/java/java_path_store.dart';
import '../services/core/java/java_scan.dart';
import 'providers.dart';

/// Java runtimes discovered by the native scanner.
///
/// The Java settings page and the per-instance Java override field must see
/// the same list. Keeping the scan in one provider means a dialog never runs a
/// second probe just to fill a picker, and both surfaces can refresh it after
/// the user installs a runtime or adds a custom path.
final javaRuntimesProvider =
    AsyncNotifierProvider<JavaRuntimesNotifier, List<JavaRuntime>>(
      JavaRuntimesNotifier.new,
    );

class JavaRuntimesNotifier extends AsyncNotifier<List<JavaRuntime>> {
  static const JavaPathStore _pathStore = JavaPathStore();

  @override
  Future<List<JavaRuntime>> build() => _scan();

  /// Rescans and publishes the result, keeping the previous list on failure.
  Future<List<JavaRuntime>> rescan() async {
    state = const AsyncLoading();
    state = await AsyncValue.guard(_scan);
    return state.value ?? const <JavaRuntime>[];
  }

  Future<List<JavaRuntime>> _scan() async {
    final client = ref.read(coreClientProvider);
    if (client == null) return const <JavaRuntime>[];

    // Custom paths are optional; a preferences failure must not hide the
    // default locations from the picker.
    var roots = const <String>[];
    var executables = const <String>[];
    try {
      roots = await _pathStore.loadRoots();
      executables = await _pathStore.loadExecutables();
    } catch (_) {
      // Fall back to the scanner's built-in locations.
    }

    final result = await client.scanJava(
      roots: roots,
      executables: executables,
      includeDefaults: true,
    );
    return result.items;
  }
}
