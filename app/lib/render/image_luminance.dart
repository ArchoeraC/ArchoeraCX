import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';
import 'dart:ui' as ui;

/// Smallest scrim the settings row allows.
const double minWallpaperDim = 0.3;

/// Largest scrim the settings row allows.
const double maxWallpaperDim = 0.9;

/// A floor is only raised for wallpapers that actually need it; above this the
/// photo would be almost gone, so the cap wins over a stricter contrast target.
const double _maxAutoFloor = 0.75;

/// sRGB channel value that keeps `#F2F5FA` at the 4.5:1 body-text target once
/// the scrim has been blended in. Blending happens in sRGB space on the GPU,
/// so the constraint is `channel * (1 - dim) <= budget` (inverse gamma below).
const double _textChannelBudget = 0.45;

/// Samples how bright a wallpaper is, once per selection.
///
/// Decodes a 32×32 thumbnail through the engine's image codec (the decode runs
/// off the UI isolate) and returns the 90th-percentile relative luminance of
/// its opaque pixels, or null when the file cannot be read/decoded or has no
/// opaque pixels. A percentile rather than the mean keeps a bright sky or lamp
/// from being averaged away by the rest of a dark photo.
Future<double?> sampleWallpaperLuminance(String path) async {
  Uint8List bytes;
  try {
    bytes = await File(path).readAsBytes();
  } catch (_) {
    return null;
  }
  ui.Codec? codec;
  ui.Image? image;
  try {
    codec = await ui.instantiateImageCodec(
      bytes,
      targetWidth: 32,
      targetHeight: 32,
    );
    final frame = await codec.getNextFrame();
    image = frame.image;
    final data = await image.toByteData(format: ui.ImageByteFormat.rawRgba);
    if (data == null) return null;
    return _percentileLuminance(data.buffer.asUint8List(), 0.9);
  } catch (_) {
    return null;
  } finally {
    image?.dispose();
    codec?.dispose();
  }
}

/// Scrim floor that keeps the dark palette's body text readable on a wallpaper
/// whose brightest tenth has [luminance].
///
/// The launcher forces the dark palette in image mode, so the text is always
/// `#F2F5FA`. Rather than flipping text colour per section (which makes it
/// flicker while scrolling over a photo), the scrim is raised just enough for
/// the brightest tenth of the image to stay readable; darker wallpapers keep
/// the user's own setting.
double wallpaperDimFloor(double? luminance) {
  if (luminance == null || luminance <= 0) return minWallpaperDim;
  final channel = _encode(luminance.clamp(0.0, 1.0));
  if (channel <= 0) return minWallpaperDim;
  final floor = 1 - _textChannelBudget / channel;
  return floor.clamp(minWallpaperDim, _maxAutoFloor).toDouble();
}

double? _percentileLuminance(Uint8List rgba, double percentile) {
  final values = <double>[];
  for (var i = 0; i + 3 < rgba.length; i += 4) {
    if (rgba[i + 3] < 16) continue;
    values.add(_relativeLuminance(rgba[i], rgba[i + 1], rgba[i + 2]));
  }
  if (values.isEmpty) return null;
  values.sort();
  final index = ((values.length - 1) * percentile).round().clamp(
    0,
    values.length - 1,
  );
  return values[index];
}

double _relativeLuminance(int red, int green, int blue) =>
    0.2126 * _linear(red) + 0.7152 * _linear(green) + 0.0722 * _linear(blue);

double _linear(int channel) {
  final value = channel / 255;
  return value <= 0.04045
      ? value / 12.92
      : math.pow((value + 0.055) / 1.055, 2.4).toDouble();
}

double _encode(double linear) {
  if (linear <= 0.0031308) return linear * 12.92;
  return (1.055 * math.pow(linear, 1 / 2.4) - 0.055).toDouble();
}
