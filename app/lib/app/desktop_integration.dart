import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:tray_manager/tray_manager.dart' as tray;
import 'package:window_manager/window_manager.dart';

import '../l10n/generated/app_localizations.dart';
import '../stores/preferences.dart';

/// 32x32 PNG tray icon. Windows only accepts PNG (and ICO) for tray images,
/// so this asset must stay a PNG.
const _trayIconAsset = 'assets/branding/tray_icon.png';

/// Mirrors `LocaleController.storageKey`; only read here to pick the language
/// of the tray menu before a widget tree is available.
const _localePreferenceKey = 'ui.locale';

/// Set once the integration has started, so repeated calls are cheap no-ops.
bool _setupStarted = false;

/// True once a tray icon exists and can bring the window back.
bool _trayReady = false;

/// Whether a hidden window can be restored through the tray icon.
///
/// Callers that hide the window on their own (for example the "hide after
/// launch" preference) must check this first, otherwise there would be no way
/// back to the launcher.
bool get isTrayAvailable => _trayReady;

/// Set before tearing the window down so the close event we trigger ourselves
/// is not intercepted again.
bool _quitRequested = false;

// The native wrappers free their handle when collected, so every object the
// tray still points at is kept reachable for the life of the process.
final List<Object> _trayResources = <Object>[];
final List<tray.ListenerId> _trayListenerIds = <tray.ListenerId>[];

final _WindowCloseListener _windowCloseListener = _WindowCloseListener();

/// Wires up the system tray and the "close to tray" window behaviour.
///
/// Safe to call more than once and never throws: a missing tray host (for
/// example GNOME without the AppIndicator extension, or a headless session)
/// leaves the launcher with its default behaviour, where closing the window
/// quits the application.
Future<void> setupDesktopIntegration() async {
  if (_setupStarted) return;
  _setupStarted = true;
  if (kIsWeb) return;

  try {
    await windowManager.ensureInitialized();
  } catch (error, stackTrace) {
    _logFailure('window manager unavailable', error, stackTrace);
    return;
  }

  await _setUpTray();

  // Without a tray icon there would be no way back to a hidden window, so the
  // close button keeps its default behaviour instead of trapping the user.
  if (!_trayReady) return;

  try {
    await windowManager.setPreventClose(true);
    windowManager.addListener(_windowCloseListener);
  } catch (error, stackTrace) {
    _logFailure('window close interception unavailable', error, stackTrace);
  }
}

/// Creates the tray icon with an explicit context menu.
///
/// The menu is always declared: on Windows the tray icon shows no menu at all
/// unless [tray.TrayIcon.setContextMenu] is called, even for the right click.
Future<void> _setUpTray() async {
  try {
    if (!tray.TrayManager.instance.isSupported()) return;

    final trayIcon = tray.TrayIcon.create();
    if (trayIcon == null) return;

    final icon = tray.ImageAsset.fromAsset(_trayIconAsset);
    if (icon == null) {
      trayIcon.dispose();
      return;
    }

    final l10n = await _loadLocalizations();
    final menu = _createTrayMenu(l10n);
    if (menu == null) {
      icon.dispose();
      trayIcon.dispose();
      return;
    }

    trayIcon
      ..icon = icon
      ..setTooltip(l10n.appName)
      // Left click shows the window; only the right click opens the menu.
      ..setContextMenuTrigger(tray.ContextMenuTrigger.rightClicked)
      ..setContextMenu(menu)
      ..setVisible(true);

    _trayResources.addAll(<Object>[icon, menu, trayIcon]);
    _trayListenerIds.add(trayIcon.addListener(_onTrayEvent));
    _trayReady = true;
  } catch (error, stackTrace) {
    _logFailure('system tray unavailable', error, stackTrace);
  }
}

/// Builds "Show window / --- / Quit" for the tray.
///
/// Built once per launch with the language selected at startup; changing the
/// UI language later applies on the next launch, like the tray itself.
tray.Menu? _createTrayMenu(AppLocalizations l10n) {
  final menu = tray.Menu.create();
  if (menu == null) return null;

  final showItem = tray.MenuItem.createWithLabelAndType(
    l10n.trayShow,
    tray.MenuItemType.normal,
  );
  if (showItem == null) {
    menu.dispose();
    return null;
  }
  showItem.addListener((event) {
    if (event is tray.MenuItemClickedEvent) unawaited(_showMainWindow());
  });
  menu.addItem(showItem);

  menu.addSeparator();

  final quitItem = tray.MenuItem.createWithLabelAndType(
    l10n.trayQuit,
    tray.MenuItemType.normal,
  );
  if (quitItem == null) {
    menu.dispose();
    showItem.dispose();
    return null;
  }
  quitItem.addListener((event) {
    if (event is tray.MenuItemClickedEvent) unawaited(quitApplication());
  });
  menu.addItem(quitItem);

  _trayResources.addAll(<Object>[showItem, quitItem]);
  return menu;
}

/// Resolves the tray menu language without a [BuildContext].
Future<AppLocalizations> _loadLocalizations() async {
  var locale = WidgetsBinding.instance.platformDispatcher.locale;
  try {
    final prefs = await SharedPreferences.getInstance();
    final code = prefs.getString(_localePreferenceKey);
    if (code != null && (code == 'en' || code == 'zh')) {
      locale = Locale(code);
    }
  } catch (_) {
    // Fall back to the platform locale.
  }

  final supported = AppLocalizations.supportedLocales.any(
    (candidate) => candidate.languageCode == locale.languageCode,
  );
  if (!supported) locale = const Locale('en');
  return AppLocalizations.delegate.load(locale);
}

void _onTrayEvent(tray.TrayIconEvent event) {
  // Linux panels own tray clicks and only open the menu, so this covers
  // Windows and macOS. The right click also shows the window; the menu itself
  // is opened by the native side through `contextMenuTrigger`.
  if (event is tray.TrayIconClickedEvent ||
      event is tray.TrayIconDoubleClickedEvent ||
      event is tray.TrayIconRightClickedEvent) {
    unawaited(_showMainWindow());
  }
}

Future<void> _showMainWindow() async {
  try {
    await windowManager.show();
    await windowManager.focus();
  } catch (error, stackTrace) {
    _logFailure('cannot show the main window', error, stackTrace);
  }
}

/// Handles the window close button while [windowManager] intercepts it.
Future<void> _handleWindowClose() async {
  if (_quitRequested) return;

  var closeToTray = false;
  try {
    closeToTray = await readCloseToTrayPreference();
  } catch (_) {
    // A missing preference store keeps the default (quit).
  }

  if (closeToTray && _trayReady) {
    // Hiding keeps the engine, the task pump and every download alive.
    try {
      await windowManager.hide();
    } catch (error, stackTrace) {
      _logFailure('cannot hide the main window', error, stackTrace);
    }
    return;
  }

  await quitApplication();
}

/// Closes the launcher through the same path the tray uses.
///
/// Shared by the tray menu, the window close button and the
/// "quit after the game exits" monitor so every clean exit tears the window
/// down exactly once. Never throws; a failure leaves the launcher running.
Future<void> quitApplication() async {
  if (_quitRequested) return;
  _quitRequested = true;
  try {
    // `destroy` also drops the close interception, so the window really goes
    // away instead of asking us again.
    await windowManager.destroy();
  } catch (error, stackTrace) {
    _quitRequested = false;
    _logFailure('cannot close the application', error, stackTrace);
  }
}

void _logFailure(String message, Object error, StackTrace stackTrace) {
  if (!kDebugMode) return;
  debugPrint('desktop integration: $message: $error\n$stackTrace');
}

final class _WindowCloseListener extends WindowListener {
  @override
  void onWindowClose() {
    unawaited(_handleWindowClose());
  }
}
