import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../services/core/core_event.dart';
import '../services/core/system/system_appearance.dart';
import '../theme/tokens.dart';
import 'providers.dart';

/// How the app picks its accent color.
enum AccentMode { system, custom }

/// Vertical control density for lists, inputs and buttons.
enum UiDensity { compact, standard, comfortable }

/// The shell's background treatment.
///
/// `image` keeps the preference visible even before a file is chosen; until a
/// valid path exists [AppearanceSettings.hasImageBackground] reports false and
/// the UI falls back to the solid canvas, matching the reference apps.
enum AppearanceStyle { solid, image }

/// Presentation preferences owned by the UI (not business state).
///
/// Theme mode, accent color, tinting and motion are Flutter concerns, so they
/// live in `shared_preferences` next to the UI language. The C# settings
/// module stays the owner of launcher/instance configuration.
@immutable
class AppearanceSettings {
  const AppearanceSettings({
    this.themeMode = ThemeMode.dark,
    this.accentMode = AccentMode.system,
    this.accentColor = AppTokens.brandAccent,
    this.globalTint = false,
    this.reduceMotion = false,
    this.density = UiDensity.standard,
    this.appearanceStyle = AppearanceStyle.solid,
    this.backgroundImage,
    this.backgroundBlur = 0,
    this.backgroundDim = 0.4,
    this.backgroundScale = 1.2,
  });

  final ThemeMode themeMode;
  final AccentMode accentMode;
  final Color accentColor;
  final bool globalTint;
  final bool reduceMotion;
  final UiDensity density;
  final AppearanceStyle appearanceStyle;

  /// Absolute path to the user's wallpaper, or null when none was selected.
  final String? backgroundImage;

  /// Gaussian blur in logical pixels (0–80).
  final double backgroundBlur;

  /// Black scrim opacity over the wallpaper (0.3–0.9).
  final double backgroundDim;

  /// Wallpaper zoom around the centre (1–2).
  final double backgroundScale;

  /// Whether the image treatment is actually active.
  ///
  /// Selecting the image style without a file is allowed (the picker stays
  /// visible), but the shell keeps the solid canvas until a path exists.
  bool get hasImageBackground =>
      appearanceStyle == AppearanceStyle.image &&
      backgroundImage != null &&
      backgroundImage!.isNotEmpty;

  AppearanceSettings copyWith({
    ThemeMode? themeMode,
    AccentMode? accentMode,
    Color? accentColor,
    bool? globalTint,
    bool? reduceMotion,
    UiDensity? density,
    AppearanceStyle? appearanceStyle,
    String? backgroundImage,
    bool clearBackgroundImage = false,
    double? backgroundBlur,
    double? backgroundDim,
    double? backgroundScale,
  }) {
    return AppearanceSettings(
      themeMode: themeMode ?? this.themeMode,
      accentMode: accentMode ?? this.accentMode,
      accentColor: accentColor ?? this.accentColor,
      globalTint: globalTint ?? this.globalTint,
      reduceMotion: reduceMotion ?? this.reduceMotion,
      density: density ?? this.density,
      appearanceStyle: appearanceStyle ?? this.appearanceStyle,
      backgroundImage: clearBackgroundImage
          ? null
          : (backgroundImage ?? this.backgroundImage),
      backgroundBlur: backgroundBlur ?? this.backgroundBlur,
      backgroundDim: backgroundDim ?? this.backgroundDim,
      backgroundScale: backgroundScale ?? this.backgroundScale,
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
  static const _appearanceStyleKey = 'ui.appearanceStyle';
  static const _backgroundImageKey = 'ui.backgroundImage';
  static const _backgroundBlurKey = 'ui.backgroundBlur';
  static const _backgroundDimKey = 'ui.backgroundDim';
  static const _backgroundScaleKey = 'ui.backgroundScale';

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
        'system' => ThemeMode.system,
        // Dark is the default: a white launcher flashing up in a dark room
        // reads as a flashbang, and users who never chose a mode get the
        // calmer surface.
        _ => ThemeMode.dark,
      };
      final accentMode = prefs.getString(_accentModeKey) == 'custom'
          ? AccentMode.custom
          : AccentMode.system;
      final colorValue = prefs.getInt(_accentColorKey);
      final color = colorValue == null
          ? AppTokens.brandAccent
          : Color(colorValue);
      final imagePath = prefs.getString(_backgroundImageKey);
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
        appearanceStyle: prefs.getString(_appearanceStyleKey) == 'image'
            ? AppearanceStyle.image
            : AppearanceStyle.solid,
        backgroundImage:
            imagePath != null &&
                imagePath.trim().isNotEmpty &&
                _isAbsolutePath(imagePath)
            ? imagePath
            : null,
        backgroundBlur: ((prefs.getDouble(_backgroundBlurKey) ?? 0).clamp(
          0,
          80,
        )).toDouble(),
        backgroundDim: ((prefs.getDouble(_backgroundDimKey) ?? 0.4).clamp(
          0.3,
          0.9,
        )).toDouble(),
        backgroundScale: ((prefs.getDouble(_backgroundScaleKey) ?? 1.2).clamp(
          1,
          2,
        )).toDouble(),
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

  Future<void> setAppearanceStyle(AppearanceStyle style) async {
    state = state.copyWith(appearanceStyle: style);
    await _write(
      _appearanceStyleKey,
      style == AppearanceStyle.image ? 'image' : 'solid',
    );
  }

  Future<void> setBackgroundImage(String? path) async {
    final candidate = path?.trim();
    final image = candidate != null && _isAbsolutePath(candidate)
        ? candidate
        : null;
    state = state.copyWith(
      backgroundImage: image,
      clearBackgroundImage: image == null,
    );
    try {
      final prefs = await SharedPreferences.getInstance();
      if (image == null) {
        await prefs.remove(_backgroundImageKey);
      } else {
        await prefs.setString(_backgroundImageKey, image);
      }
    } catch (_) {
      // The in-memory selection still applies for this session.
    }
  }

  /// Keeps a manipulated preference file from pointing the image loader at a
  /// relative path. The picker itself always returns an absolute path.
  static bool _isAbsolutePath(String path) {
    if (path.startsWith('/')) return true;
    if (path.startsWith(r'\\')) return true;
    return RegExp(r'^[A-Za-z]:[\\/]').hasMatch(path);
  }

  Future<void> setBackgroundBlur(double value) async {
    final blur = value.clamp(0, 80).toDouble();
    state = state.copyWith(backgroundBlur: blur);
    await _write(_backgroundBlurKey, blur);
  }

  Future<void> setBackgroundDim(double value) async {
    final dim = value.clamp(0.3, 0.9).toDouble();
    state = state.copyWith(backgroundDim: dim);
    await _write(_backgroundDimKey, dim);
  }

  Future<void> setBackgroundScale(double value) async {
    final scale = value.clamp(1, 2).toDouble();
    state = state.copyWith(backgroundScale: scale);
    await _write(_backgroundScaleKey, scale);
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
      await prefs.remove(_appearanceStyleKey);
      await prefs.remove(_backgroundImageKey);
      await prefs.remove(_backgroundBlurKey);
      await prefs.remove(_backgroundDimKey);
      await prefs.remove(_backgroundScaleKey);
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
      } else if (value is double) {
        await prefs.setDouble(key, value);
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
  SystemAppearance? latest;

  void emit(SystemAppearance appearance) {
    latest = appearance.merge(latest);
    if (!closed) controller.add(latest);
  }

  Future<void> load() async {
    try {
      final appearance = await client.getSystemAppearance();
      if (appearance != null) emit(appearance);
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
        if (appearance != null) emit(appearance);
      });

  unawaited(load());

  ref.onDispose(() {
    closed = true;
    unawaited(subscription.cancel());
    unawaited(controller.close());
  });

  return controller.stream;
});
