import 'dart:async';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:tray_manager/tray_manager.dart' as tray;
import 'package:window_manager/window_manager.dart';

import '../l10n/generated/app_localizations.dart';
import '../stores/locale.dart';
import '../stores/preferences.dart';

/// 32x32 PNG tray icon. Windows only accepts PNG (and ICO) for tray images,
/// so this asset must stay a PNG.
const _trayIconAsset = 'assets/branding/tray_icon.png';

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

/// Set before tearing the window down so the close request we trigger ourselves
/// is not intercepted again.
bool _quitRequested = false;

/// The launcher's own window, resolved on first use and then reused.
///
/// nativeapi finds the window the platform considers the app's current one;
/// before the runner has shown it the lookup answers null and the next caller
/// retries.
Window? _mainWindow;

/// Whether the native close-request subscription is installed.
bool _closeInterceptionAttached = false;

// The native wrappers free their handle when collected, so every object the
// tray still points at is kept reachable for the life of the process.
final List<Object> _trayResources = <Object>[];
final List<tray.ListenerId> _trayListenerIds = <tray.ListenerId>[];

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

  await _setUpTray();

  // Without a tray icon there would be no way back to a hidden window, so the
  // close button keeps its default behaviour instead of trapping the user.
  if (!_trayReady) return;

  // Resolving attaches the close-request handler, which is what turns the
  // close button into "hide to tray"; without a window the platform default
  // (quit) stays in place.
  try {
    if (_resolveMainWindow() == null) {
      _logFailure('window unavailable', 'no native window', StackTrace.current);
    }
  } catch (error, stackTrace) {
    _logFailure('window manager unavailable', error, stackTrace);
  }
}

/// Resolves the launcher window, or null outside a desktop app.
///
/// Resolving also subscribes the close-request handler, so a late first frame
/// still gets "close to tray": the next window action retries the lookup.
/// Widget tests run without the native library, where the FFI lookup throws
/// [ArgumentError]; every window action then degrades to a no-op.
Window? _resolveMainWindow() {
  final cached = _mainWindow;
  if (cached != null) return cached;
  try {
    final window = WindowManager.instance.getCurrent();
    if (window == null) return null;
    _mainWindow = window;
    _attachCloseListener(window);
    return window;
  } on ArgumentError {
    return null;
  }
}

/// Subscribes the close handler once, when a tray icon can restore the window.
///
/// A close request reaches this window's own listeners and is approved unless
/// the listener cancels it, which is what "close to tray" does.
void _attachCloseListener(Window window) {
  if (!_trayReady || _closeInterceptionAttached) return;
  try {
    window.addListener(_onWindowEvent);
    _closeInterceptionAttached = true;
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
    if (event is tray.MenuItemClickedEvent) unawaited(activateMainWindow());
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
Future<AppLocalizations> _loadLocalizations() async =>
    AppLocalizations.delegate.load(await resolveStartupLocale());

void _onTrayEvent(tray.TrayIconEvent event) {
  // Linux panels own tray clicks and only open the menu, so this covers
  // Windows and macOS. The right click also shows the window; the menu itself
  // is opened by the native side through `contextMenuTrigger`.
  if (event is tray.TrayIconClickedEvent ||
      event is tray.TrayIconDoubleClickedEvent ||
      event is tray.TrayIconRightClickedEvent) {
    unawaited(activateMainWindow());
  }
}

/// Brings the existing window forward, restoring it from minimized or hidden
/// state. Shared by the tray and the single-instance activation event.
Future<void> activateMainWindow() async {
  if (_quitRequested) return;
  try {
    final window = _resolveMainWindow();
    if (window == null) return;
    if (window.isMinimized) window.restore();
    window.show();
    window.focus();
  } catch (error, stackTrace) {
    _logFailure('cannot show the main window', error, stackTrace);
  }
}

/// Hides the launcher window, for example while a launched game runs.
///
/// Callers must check [isTrayAvailable] first: without a tray icon a hidden
/// window could not be brought back.
Future<void> hideMainWindow() async {
  try {
    _resolveMainWindow()?.hide();
  } catch (error, stackTrace) {
    _logFailure('cannot hide the main window', error, stackTrace);
  }
}

/// Hides and exits a secondary process before it builds a widget tree.
///
/// The primary has already received the activation message. Exiting here
/// prevents a second visible window, tray icon and widget tree from being
/// created.
Future<void> exitSecondaryInstance() async {
  try {
    _resolveMainWindow()?.hide();
  } catch (_) {
    // The process must exit even when the window plugin is unavailable.
  }
  exit(0);
}

/// Answers a close request from the title bar or the platform.
///
/// Runs on the close request's own callback, so cancelling [event] there is
/// what keeps the window alive. A request started by [quitApplication] is
/// approved instead of being intercepted again.
Future<void> _onWindowEvent(WindowEvent event) async {
  if (event is! WindowCloseRequestedEvent) return;
  if (_quitRequested) return;

  var closeToTray = false;
  try {
    closeToTray = await readCloseToTrayPreference();
  } catch (_) {
    // A missing preference store keeps the default (quit).
  }

  if (closeToTray && _trayReady) {
    // Hiding keeps the engine, the task pump and every download alive.
    event.request.cancel();
    await hideMainWindow();
    return;
  }

  await quitApplication();
}

/// Sends the platform close request through the window's normal close path.
///
/// `Window.close()` continues the host's own handling (WM_CLOSE, GTK
/// delete-event, `performClose`), which is also the path that tears the
/// Windows runner down in the right order. Approving the request is what ends
/// the process, because every desktop runner quits with its last window.
@visibleForTesting
Future<void> closeMainWindowForQuit({
  required bool Function() closeWindow,
  required void Function(int exitCode) quitProcess,
}) async {
  if (closeWindow()) return;
  // Some hosts cannot close a window; end the process the way the platform
  // quit path would instead of leaving a window that refuses to disappear.
  quitProcess(0);
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
    final window = _resolveMainWindow();
    await closeMainWindowForQuit(
      closeWindow: () => window?.close() ?? false,
      quitProcess: exit,
    );
  } catch (error, stackTrace) {
    _quitRequested = false;
    _logFailure('cannot close the application', error, stackTrace);
  }
}

void _logFailure(String message, Object error, StackTrace stackTrace) {
  if (!kDebugMode) return;
  debugPrint('desktop integration: $message: $error\n$stackTrace');
}
