import 'dart:io';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../utils/window_mode.dart';

/// Whether Linux-only launch options (GameMode, MangoHud, Zink and the native
/// library overrides) should be shown.
///
/// A provider instead of a direct `Platform.isLinux` check lets tests render
/// both the Linux and the portable settings surface.
final linuxPlatformProvider = Provider<bool>((ref) => Platform.isLinux);

/// Whether the shell runs on a desktop platform with a real window.
///
/// Guards window-level launch behaviour such as hiding the launcher after a
/// successful game start. A provider keeps that surface testable.
final desktopPlatformProvider = Provider<bool>(
  (ref) => Platform.isLinux || Platform.isWindows || Platform.isMacOS,
);

/// Resolves the primary display's usable size for the maximized window mode.
///
/// Overridable so widget tests do not need the `screen_retriever` platform
/// channel; production uses [resolveMaximizedWindowSize].
typedef MaximizedWindowSizeResolver =
    Future<({int width, int height})?> Function();

final maximizedWindowSizeProvider = Provider<MaximizedWindowSizeResolver>(
  (ref) => resolveMaximizedWindowSize,
);
