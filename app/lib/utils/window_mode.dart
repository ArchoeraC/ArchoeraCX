import 'dart:ui' show Size;

import 'package:screen_retriever/screen_retriever.dart';

/// How the game window should open.
///
/// Minecraft has no `--maximized` launch argument, so [maximized] is an
/// approximation: the launcher resolves the primary display's usable area into
/// the regular window width/height arguments.
enum GameWindowMode { windowed, fullscreen, maximized }

/// Maps the persisted global flags to the single UI mode.
///
/// The native owner keeps the flags mutually exclusive, but a hand-edited or
/// older document may still set both; fullscreen then wins because it is the
/// explicit vanilla launch argument.
GameWindowMode gameWindowModeFrom({
  required bool fullscreen,
  required bool maximized,
}) {
  if (maximized && !fullscreen) return GameWindowMode.maximized;
  return fullscreen ? GameWindowMode.fullscreen : GameWindowMode.windowed;
}

/// Maps a per-instance override pair to the mode shown in the dialog. `null`
/// on both flags means "follow global".
GameWindowMode? instanceWindowModeFrom({
  required bool? fullscreen,
  required bool? maximized,
}) {
  if (maximized == true && fullscreen != true) return GameWindowMode.maximized;
  return switch (fullscreen) {
    true => GameWindowMode.fullscreen,
    false => GameWindowMode.windowed,
    null => null,
  };
}

/// Upper/lower bounds shared with the settings parser and the instance dialog.
const int gameWindowMinWidth = 640;
const int gameWindowMaxWidth = 7680;
const int gameWindowMinHeight = 480;
const int gameWindowMaxHeight = 4320;

/// Resolve the window size used to approximate a maximized game window.
///
/// [visibleSize] is the usable display area (panels and taskbars excluded) and
/// is preferred over the full [size]. Returns null when neither is usable, so
/// callers can keep whatever size is already configured instead of writing a
/// zero-sized window.
({int width, int height})? maximizedWindowSizeFor(
  Size? size,
  Size? visibleSize,
) {
  final usable = _usable(visibleSize) ?? _usable(size);
  if (usable == null) return null;
  return (
    width: usable.width.round().clamp(gameWindowMinWidth, gameWindowMaxWidth),
    height: usable.height.round().clamp(
      gameWindowMinHeight,
      gameWindowMaxHeight,
    ),
  );
}

Size? _usable(Size? size) {
  if (size == null) return null;
  if (!size.width.isFinite || !size.height.isFinite) return null;
  if (size.width <= 0 || size.height <= 0) return null;
  return size;
}

/// Loads the primary display through `screen_retriever`.
///
/// Kept injectable so widget tests and unusual Linux sessions can substitute a
/// fake without starting the platform channel.
typedef PrimaryDisplayLoader = Future<Display> Function();

/// Resolves the maximized window size; any platform-channel failure returns
/// null instead of breaking the settings page.
Future<({int width, int height})?> resolveMaximizedWindowSize({
  PrimaryDisplayLoader? loadDisplay,
}) async {
  try {
    final display =
        await (loadDisplay ?? ScreenRetriever.instance.getPrimaryDisplay)();
    return maximizedWindowSizeFor(display.size, display.visibleSize);
  } catch (_) {
    return null;
  }
}
