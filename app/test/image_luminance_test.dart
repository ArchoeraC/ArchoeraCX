import 'dart:io';
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:archoera_cx/render/image_luminance.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// Writes a solid 32×32 PNG so the sampler sees a deterministic wallpaper.
Future<Uint8List> _solidPng(int argb) async {
  final recorder = ui.PictureRecorder();
  Canvas(
    recorder,
  ).drawRect(const Rect.fromLTWH(0, 0, 32, 32), Paint()..color = Color(argb));
  final picture = recorder.endRecording();
  final image = await picture.toImage(32, 32);
  try {
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    return data!.buffer.asUint8List();
  } finally {
    picture.dispose();
    image.dispose();
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  group('wallpaperDimFloor', () {
    test('dark and mid wallpapers keep the default floor', () {
      expect(wallpaperDimFloor(null), minWallpaperDim);
      expect(wallpaperDimFloor(0.02), minWallpaperDim);
      // 18 % grey already meets the contrast budget without a strong scrim.
      expect(wallpaperDimFloor(0.2), minWallpaperDim);
    });

    test('a bright wallpaper raises the floor for readable text', () {
      // White needs roughly 55 % black before #F2F5FA reaches 4.5:1.
      final white = wallpaperDimFloor(1);
      expect(white, closeTo(0.55, 0.01));
      expect(white, greaterThan(wallpaperDimFloor(0.6)));
    });

    test('the floor never swallows the wallpaper', () {
      expect(wallpaperDimFloor(1), lessThanOrEqualTo(0.75));
      expect(wallpaperDimFloor(1), lessThan(maxWallpaperDim));
    });
  });

  group('sampleWallpaperLuminance', () {
    testWidgets('reads opaque pixels and ignores transparency', (tester) async {
      final directory = Directory.systemTemp.createTempSync(
        'archoera-luminance',
      );
      addTearDown(() => directory.deleteSync(recursive: true));

      await tester.runAsync(() async {
        final white = File('${directory.path}/white.png')
          ..writeAsBytesSync(await _solidPng(0xFFFFFFFF));
        final black = File('${directory.path}/black.png')
          ..writeAsBytesSync(await _solidPng(0xFF000000));
        final clear = File('${directory.path}/clear.png')
          ..writeAsBytesSync(await _solidPng(0x00000000));

        expect(await sampleWallpaperLuminance(white.path), closeTo(1, 0.02));
        expect(await sampleWallpaperLuminance(black.path), closeTo(0, 0.02));
        // Fully transparent images (or missing files) carry no information;
        // the caller keeps the user's own scrim.
        expect(await sampleWallpaperLuminance(clear.path), isNull);
        expect(
          await sampleWallpaperLuminance('${directory.path}/missing.png'),
          isNull,
        );
      });
    });
  });
}
