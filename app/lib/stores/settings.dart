import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/app_settings.dart';
import '../utils/app_paths.dart';
import 'providers.dart';

/// C#-owned settings, with a development fallback when the native module is
/// unavailable. AsyncNotifier keeps the settings page and create dialog in
/// sync after a directory is picked.
final appSettingsProvider =
    AsyncNotifierProvider<AppSettingsNotifier, AppSettings>(
      AppSettingsNotifier.new,
    );

class AppSettingsNotifier extends AsyncNotifier<AppSettings> {
  @override
  Future<AppSettings> build() async {
    final client = ref.read(coreClientProvider);
    if (client != null) {
      try {
        return await client.getSettings();
      } catch (_) {
        // Fall through to the portable fallback; the settings page still works.
      }
    }

    return _fallback;
  }

  Future<void> setInstancesRoot(String path) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      state = AsyncData(
        AppSettings(
          instancesRoot: path,
          dataRoot: _fallback.dataRoot,
          settingsPath: '',
        ),
      );
      return;
    }

    final updated = await client.setInstancesRoot(path);
    state = AsyncData(updated);
  }

  AppSettings get _fallback {
    final dataRoot = defaultDataRoot();
    return AppSettings(
      instancesRoot: fallbackInstancesRoot(),
      dataRoot: dataRoot,
      settingsPath: '',
    );
  }
}
