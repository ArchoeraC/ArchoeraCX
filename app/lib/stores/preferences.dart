import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../widgets/common/toast.dart';

/// Routes the user may choose as the shell's startup page.
const startPageOptions = <String>{
  '/',
  '/instances',
  '/download',
  '/accounts',
  '/wiki',
  '/settings',
};

/// Persistence key shared by the controller and [readStartPagePreference].
const _startPageKey = 'ui.startPage';

/// Reads the persisted startup page before the router is built.
///
/// Returns null when nothing is stored, the value is not one of
/// [startPageOptions], or the preference store is unavailable.
Future<String?> readStartPagePreference() async {
  try {
    final prefs = await SharedPreferences.getInstance();
    final value = prefs.getString(_startPageKey);
    return value != null && startPageOptions.contains(value) ? value : null;
  } catch (_) {
    return null;
  }
}

/// Reads the persisted "close to tray" flag outside of Riverpod.
///
/// The window close handler runs before the widget tree is consulted, so it
/// reads the same key the controller writes. Returns false when nothing is
/// stored or the preference store is unavailable.
Future<bool> readCloseToTrayPreference() async {
  try {
    final prefs = await SharedPreferences.getInstance();
    return prefs.getBool(PreferencesController.closeToTrayKey) ?? false;
  } catch (_) {
    return false;
  }
}

/// Presentation and behaviour preferences owned by the UI layer.
///
/// Launcher/instance configuration that the native modules act on lives in the
/// C# settings document (`AppSettings`). Everything here only affects the
/// Flutter shell: task list length, log tail size, toast timing, desktop
/// notifications and the startup page.
@immutable
class UiPreferences {
  const UiPreferences({
    this.taskHistoryLimit = 12,
    this.logLineLimit = 400,
    this.toastSeconds = 3,
    this.desktopNotifications = true,
    this.downloadNotifications = true,
    this.closeToTray = false,
    this.startPage = '/',
  });

  /// Completed tasks kept in the task center (running tasks are never dropped).
  final int taskHistoryLimit;

  /// Lines requested when the log viewer opens.
  final int logLineLimit;

  /// How long an info/success toast stays on screen.
  final int toastSeconds;

  /// Desktop notifications for account sign-in and similar events.
  final bool desktopNotifications;

  /// Desktop notifications when a background download finishes.
  final bool downloadNotifications;

  /// Hide the main window in the tray instead of quitting on window close.
  final bool closeToTray;

  /// Route opened on launch; one of [startPageOptions].
  final String startPage;

  UiPreferences copyWith({
    int? taskHistoryLimit,
    int? logLineLimit,
    int? toastSeconds,
    bool? desktopNotifications,
    bool? downloadNotifications,
    bool? closeToTray,
    String? startPage,
  }) {
    return UiPreferences(
      taskHistoryLimit: taskHistoryLimit ?? this.taskHistoryLimit,
      logLineLimit: logLineLimit ?? this.logLineLimit,
      toastSeconds: toastSeconds ?? this.toastSeconds,
      desktopNotifications: desktopNotifications ?? this.desktopNotifications,
      downloadNotifications:
          downloadNotifications ?? this.downloadNotifications,
      closeToTray: closeToTray ?? this.closeToTray,
      startPage: startPage ?? this.startPage,
    );
  }
}

final preferencesProvider =
    NotifierProvider<PreferencesController, UiPreferences>(
      PreferencesController.new,
    );

final class PreferencesController extends Notifier<UiPreferences> {
  /// Persistence key read by [readCloseToTrayPreference] when the window closes.
  static const closeToTrayKey = 'ui.closeToTray';

  static const _taskHistoryKey = 'ui.taskHistory';
  static const _logLinesKey = 'ui.logLines';
  static const _toastSecondsKey = 'ui.toastSeconds';
  static const _desktopNotificationsKey = 'ui.notifyDesktop';
  static const _downloadNotificationsKey = 'ui.notifyDownloads';

  @override
  UiPreferences build() {
    unawaited(_restore());
    return const UiPreferences();
  }

  Future<void> _restore() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      if (!ref.mounted) return;
      final restored = UiPreferences(
        taskHistoryLimit: (prefs.getInt(_taskHistoryKey) ?? 12).clamp(4, 60),
        logLineLimit: (prefs.getInt(_logLinesKey) ?? 400).clamp(100, 5000),
        toastSeconds: (prefs.getInt(_toastSecondsKey) ?? 3).clamp(1, 15),
        desktopNotifications: prefs.getBool(_desktopNotificationsKey) ?? true,
        downloadNotifications: prefs.getBool(_downloadNotificationsKey) ?? true,
        closeToTray: prefs.getBool(closeToTrayKey) ?? false,
        startPage: _sanitizeStartPage(prefs.getString(_startPageKey)),
      );
      state = restored;
      _applyToastDuration(restored);
    } catch (_) {
      // A missing preference store must not block startup.
    }
  }

  Future<void> setTaskHistoryLimit(int value) async {
    final next = state.copyWith(taskHistoryLimit: value.clamp(4, 60));
    state = next;
    await _writeInt(_taskHistoryKey, next.taskHistoryLimit);
  }

  Future<void> setLogLineLimit(int value) async {
    final next = state.copyWith(logLineLimit: value.clamp(100, 5000));
    state = next;
    await _writeInt(_logLinesKey, next.logLineLimit);
  }

  Future<void> setToastSeconds(int value) async {
    final next = state.copyWith(toastSeconds: value.clamp(1, 15));
    state = next;
    _applyToastDuration(next);
    await _writeInt(_toastSecondsKey, next.toastSeconds);
  }

  Future<void> setDesktopNotifications(bool enabled) async {
    state = state.copyWith(desktopNotifications: enabled);
    await _writeBool(_desktopNotificationsKey, enabled);
  }

  Future<void> setDownloadNotifications(bool enabled) async {
    state = state.copyWith(downloadNotifications: enabled);
    await _writeBool(_downloadNotificationsKey, enabled);
  }

  /// Persists whether closing the window hides it in the tray.
  ///
  /// The desktop integration reads the same key when the close button is used.
  Future<void> setCloseToTray(bool enabled) async {
    state = state.copyWith(closeToTray: enabled);
    await _writeBool(closeToTrayKey, enabled);
  }

  /// Persists the startup page; unknown routes are ignored.
  Future<void> setStartPage(String value) async {
    if (!startPageOptions.contains(value)) return;
    state = state.copyWith(startPage: value);
    await _writeString(_startPageKey, value);
  }

  String _sanitizeStartPage(String? value) =>
      value != null && startPageOptions.contains(value) ? value : '/';

  void _applyToastDuration(UiPreferences prefs) {
    toastController.defaultDuration = Duration(seconds: prefs.toastSeconds);
  }

  Future<void> _writeInt(String key, int value) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setInt(key, value);
    } catch (_) {
      // The in-memory value still applies for this session.
    }
  }

  Future<void> _writeBool(String key, bool value) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setBool(key, value);
    } catch (_) {
      // The in-memory value still applies for this session.
    }
  }

  Future<void> _writeString(String key, String value) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(key, value);
    } catch (_) {
      // The in-memory value still applies for this session.
    }
  }
}
