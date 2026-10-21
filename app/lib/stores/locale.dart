import 'dart:ui';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// UI language preference. `null` means "follow the operating system".
///
/// This is a presentation preference, not business state, so it is stored in
/// `shared_preferences` instead of the C# settings owner.
final localeProvider = NotifierProvider<LocaleController, Locale?>(
  LocaleController.new,
);

final class LocaleController extends Notifier<Locale?> {
  static const storageKey = 'ui.locale';

  @override
  Locale? build() {
    _restore();
    return null;
  }

  Future<void> _restore() async {
    try {
      final preferences = await SharedPreferences.getInstance();
      final code = preferences.getString(storageKey);
      final locale = _fromCode(code);
      if (locale != null && ref.mounted) state = locale;
    } catch (_) {
      // A missing preference store must not block startup.
    }
  }

  /// Persists the selected language. Passing null restores system selection.
  Future<void> setLocale(Locale? locale) async {
    state = locale;
    try {
      final preferences = await SharedPreferences.getInstance();
      if (locale == null) {
        await preferences.remove(storageKey);
      } else {
        await preferences.setString(storageKey, locale.languageCode);
      }
    } catch (_) {
      // The in-memory selection still applies for this session.
    }
  }

  static Locale? _fromCode(String? code) => switch (code) {
    'en' => const Locale('en'),
    'zh' => const Locale('zh'),
    _ => null,
  };
}
