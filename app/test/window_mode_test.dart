import 'dart:ui';

import 'package:archoera_cx/utils/window_mode.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:screen_retriever/screen_retriever.dart';

void main() {
  group('maximized window size', () {
    test('prefers the usable visible size over the full display size', () {
      final size = maximizedWindowSizeFor(
        const Size(2560, 1440),
        const Size(1920, 1080),
      );

      expect(size, (width: 1920, height: 1080));
    });

    test('falls back to the display size when no visible size is reported', () {
      expect(maximizedWindowSizeFor(const Size(1600, 900), null), (
        width: 1600,
        height: 900,
      ));
      expect(
        maximizedWindowSizeFor(
          const Size(1600, 900),
          const Size(double.infinity, double.infinity),
        ),
        (width: 1600, height: 900),
      );
    });

    test('rounds fractional logical pixels and clamps to the game range', () {
      expect(maximizedWindowSizeFor(const Size(1919.6, 1079.4), null), (
        width: 1920,
        height: 1079,
      ));
      expect(maximizedWindowSizeFor(const Size(320, 200), null), (
        width: 640,
        height: 480,
      ));
      expect(maximizedWindowSizeFor(const Size(9000, 6000), null), (
        width: 7680,
        height: 4320,
      ));
    });

    test('returns null for missing or degenerate sizes', () {
      expect(maximizedWindowSizeFor(null, null), isNull);
      expect(maximizedWindowSizeFor(Size.zero, Size.zero), isNull);
      expect(
        maximizedWindowSizeFor(const Size(-10, -10), const Size(0, 0)),
        isNull,
      );
    });

    test('reads the primary display through the injected loader', () async {
      final size = await resolveMaximizedWindowSize(
        loadDisplay: () async => const Display(
          id: 'primary',
          size: Size(2560, 1440),
          visibleSize: Size(1920, 1080),
        ),
      );

      expect(size, (width: 1920, height: 1080));
    });

    test('a display lookup failure keeps the caller size', () async {
      final size = await resolveMaximizedWindowSize(
        loadDisplay: () async => throw StateError('no display'),
      );

      expect(size, isNull);
    });
  });
}
