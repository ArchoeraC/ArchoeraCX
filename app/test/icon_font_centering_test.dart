import 'dart:ui' as ui;

import 'package:archoera_cx/theme/icons/app_icon_data.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

/// Guards the icon font against the upstream left-edge bias.
///
/// Tabler's webfont draws every glyph flush against the left edge of its em
/// box, and the build pipeline re-centers each outline on its advance
/// (`tool/build_icons.py`). CSS icon usage hides the bias by centering the
/// text box itself, but Skia paints the outline at the pen position, so a
/// regression would push every icon's ink visibly off center inside badges
/// (the accent-swatch check was the first place it showed). This test
/// rasterizes a few glyphs and requires the ink to sit on the box center.
void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    final loader = FontLoader(AppIconData.fontFamily)
      ..addFont(rootBundle.load('assets/fonts/AppIcons.ttf'));
    await loader.load();
  });

  test('glyph ink is centered inside its box', () async {
    const size = 64.0;
    const canvasSize = 256.0;
    const samples = {
      'check': AppIconData.check,
      'plus': AppIconData.plus,
      'color-picker': AppIconData.colorPicker,
      'alert-triangle': AppIconData.alertTriangle,
      'user': AppIconData.user,
      'player-play': AppIconData.playerPlay,
    };
    for (final entry in samples.entries) {
      final recorder = ui.PictureRecorder();
      final canvas = Canvas(recorder);
      canvas.drawRect(
        const Rect.fromLTWH(0, 0, canvasSize, canvasSize),
        Paint()..color = const Color(0xFF000000),
      );
      final painter = TextPainter(
        text: TextSpan(
          text: String.fromCharCode(entry.value.codePoint),
          style: const TextStyle(
            fontFamily: AppIconData.fontFamily,
            fontSize: size,
            color: Color(0xFFFFFFFF),
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      final boxLeft = (canvasSize - painter.width) / 2;
      final boxTop = (canvasSize - painter.height) / 2;
      painter.paint(canvas, Offset(boxLeft, boxTop));
      final image = await recorder
          .endRecording()
          .toImage(canvasSize.toInt(), canvasSize.toInt());
      final data = await image.toByteData(format: ui.ImageByteFormat.rawRgba);
      final bytes = data!.buffer.asUint32List();
      var minX = canvasSize.toInt();
      var maxX = -1;
      var minY = canvasSize.toInt();
      var maxY = -1;
      for (var y = 0; y < canvasSize.toInt(); y++) {
        for (var x = 0; x < canvasSize.toInt(); x++) {
          // rawRgba on a little-endian host: the low byte is the red channel,
          // which separates white ink from the black backdrop.
          if (bytes[y * canvasSize.toInt() + x] & 0xFF > 64) {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
          }
        }
      }
      expect(maxX, greaterThanOrEqualTo(0), reason: '${entry.key} drew no ink');
      final inkCx = (minX + maxX) / 2 - boxLeft;
      final inkCy = (minY + maxY) / 2 - boxTop;
      // 1.5px of slack absorbs the inclusive pixel-scan half-step; the
      // pre-fix bias was ~9.5px horizontally. Vertical ink placement follows
      // the font baseline, which the upstream design does not normalize per
      // glyph (the tall alert-triangle sits ~3px high at this reference
      // size) — that is under 0.8px at the sizes the UI actually draws, so
      // only a regression of the horizontal recentering fails here.
      expect(
        (inkCx - painter.width / 2).abs(),
        lessThan(1.5),
        reason: '${entry.key} ink is not horizontally centered',
      );
      expect(
        (inkCy - painter.height / 2).abs(),
        lessThan(3.5),
        reason: '${entry.key} ink is not vertically centered',
      );
    }
  }, timeout: const Timeout(Duration(minutes: 5)));
}
