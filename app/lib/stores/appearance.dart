import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../services/core/core_event.dart';
import '../services/core/system_appearance.dart';
import '../theme/tokens.dart';
import 'providers.dart';

/// How the app picks its accent color.
enum AccentMode { system, custom }

/// Vertical control density for lists, inputs and buttons.
enum UiDensity { compact, standard, comfortable }

/// Presentation preferences owned by the UI (not business state).
///
/// Theme mode, accent color, tinting and motion are Flutter concerns, so they
/// live in `shared_preferences` next to the UI language. The C# settings
/// module stays the owner of launcher/instance configuration.
@immutable
class AppearanceSettings {
  const AppearanceSettings({
    this.themeMode = ThemeMode.system,
    this.accentMode = AccentMode.system,
    this.accentColor = AppTokens.brandAccent,
    this.globalTint = false,
    this.reduceMotion = false,
    this.density = UiDensity.standard,
  });

  final ThemeMode themeMode;
  final AccentMode accentMode;
  final Color accentColor;
  final bool globalTint;
  final bool reduceMotion;
  final UiDensity density;

  AppearanceSettings copyWith({
    ThemeMode? themeMode,
    AccentMode? accentMode,
    Color? accentColor,
    bool? globalTint,
    bool? reduceMotion,
    UiDensity? density,
  }) {
    return AppearanceSettings(
      themeMode: themeMode ?? this.themeMode,
      accentMode: accentMode ?? this.accentMode,
      accentColor: accentColor ?? this.accentColor,
      globalTint: globalTint ?? this.globalTint,
      reduceMotion: reduceMotion ?? this.reduceMotion,
      density: density ?? this.density,
    );
  }
}

final appearanceProvider =
    NotifierProvider<AppearanceController, AppearanceSettings>(
      AppearanceController.new,
    );

final class AppearanceController extends Notifier<AppearanceSettings> {
  static const _themeModeKey = 'ui.themeMode';
  static const _accentModeKey = 'ui.accentMode';
  static const _accentColorKey = 'ui.accentColor';
  static const _globalTintKey = 'ui.globalTint';
  static const _reduceMotionKey = 'ui.reduceMotion';
  static const _densityKey = 'ui.density';

  @override
  AppearanceSettings build() {
    unawaited(_restore());
    return const AppearanceSettings();
  }

  Future<void> _restore() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final mode = switch (prefs.getString(_themeModeKey)) {
        'light' => ThemeMode.light,
        'dark' => ThemeMode.dark,
        _ => ThemeMode.system,
      };
      final accentMode = prefs.getString(_accentModeKey) == 'custom'
          ? AccentMode.custom
          : AccentMode.system;
      final colorValue = prefs.getInt(_accentColorKey);
      final color = colorValue == null
          ? AppTokens.brandAccent
          : Color(colorValue);
      if (!ref.mounted) return;
      state = AppearanceSettings(
        themeMode: mode,
        accentMode: accentMode,
        accentColor: color,
        globalTint: prefs.getBool(_globalTintKey) ?? false,
        reduceMotion: prefs.getBool(_reduceMotionKey) ?? false,
        density: switch (prefs.getString(_densityKey)) {
          'compact' => UiDensity.compact,
          'comfortable' => UiDensity.comfortable,
          _ => UiDensity.standard,
        },
      );
    } catch (_) {
      // A missing preference store must not block startup.
    }
  }

  Future<void> setThemeMode(ThemeMode mode) async {
    state = state.copyWith(themeMode: mode);
    await _write(_themeModeKey, switch (mode) {
      ThemeMode.light => 'light',
      ThemeMode.dark => 'dark',
      ThemeMode.system => 'system',
    });
  }

  Future<void> setAccentMode(AccentMode mode) async {
    state = state.copyWith(accentMode: mode);
    await _write(
      _accentModeKey,
      mode == AccentMode.custom ? 'custom' : 'system',
    );
  }

  Future<void> setAccentColor(Color color) async {
    state = state.copyWith(accentColor: color, accentMode: AccentMode.custom);
    await _write(_accentColorKey, color.toARGB32());
    await _write(_accentModeKey, 'custom');
  }

  Future<void> setGlobalTint(bool enabled) async {
    state = state.copyWith(globalTint: enabled);
    await _write(_globalTintKey, enabled);
  }

  Future<void> setReduceMotion(bool enabled) async {
    state = state.copyWith(reduceMotion: enabled);
    await _write(_reduceMotionKey, enabled);
  }

  Future<void> setDensity(UiDensity density) async {
    state = state.copyWith(density: density);
    await _write(_densityKey, switch (density) {
      UiDensity.compact => 'compact',
      UiDensity.standard => 'standard',
      UiDensity.comfortable => 'comfortable',
    });
  }

  Future<void> reset() async {
    state = const AppearanceSettings();
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.remove(_themeModeKey);
      await prefs.remove(_accentModeKey);
      await prefs.remove(_accentColorKey);
      await prefs.remove(_globalTintKey);
      await prefs.remove(_reduceMotionKey);
      await prefs.remove(_densityKey);
    } catch (_) {
      // The in-memory defaults still apply for this session.
    }
  }

  Future<void> _write(String key, Object value) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      if (value is String) {
        await prefs.setString(key, value);
      } else if (value is int) {
        await prefs.setInt(key, value);
      } else if (value is bool) {
        await prefs.setBool(key, value);
      }
    } catch (_) {
      // The in-memory selection still applies for this session.
    }
  }
}

/// Latest core-reported system appearance, kept live by `system.appearance`
/// domain events. Null while the core is unavailable or before the first read.
final systemAppearanceProvider = StreamProvider<SystemAppearance?>((ref) {
  final client = ref.watch(coreClientProvider);
  if (client == null) {
    return Stream<SystemAppearance?>.value(null);
  }

  final controller = StreamController<SystemAppearance?>();
  var closed = false;

  Future<void> load() async {
    try {
      final appearance = await client.getSystemAppearance();
      if (!closed && appearance != null) controller.add(appearance);
    } catch (_) {
      // Older cores do not implement system.appearance; Flutter's platform
      // brightness remains the fallback.
    }
  }

  final subscription = client.events
      .where(
        (event) =>
            event.type == CoreEventType.domain &&
            event.payload['kind'] == 'system.appearance',
      )
      .listen((event) {
        final appearance = SystemAppearance.tryParse(event.payload);
        if (!closed && appearance != null) controller.add(appearance);
      });

  unawaited(load());

  ref.onDispose(() {
    closed = true;
    unawaited(subscription.cancel());
    unawaited(controller.close());
  });

  return controller.stream;
});
