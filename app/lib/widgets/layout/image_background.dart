import 'dart:io' show File, Platform;
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

/// Full-window wallpaper layer for the image appearance style.
///
/// The image is decoded once per file, fitted with [BoxFit.cover], scaled,
/// blurred and darkened, then **baked into one static texture** on the raster
/// thread. Subsequent frames only draw a [RawImage]; real-time
/// `ImageFiltered` would re-run the gaussian blur on every frame under
/// Impeller (see the same trade-off in ArchoeraMusic's baked background).
///
/// Widget tests cannot rely on `Picture.toImageSync` in the fake-async
/// environment, so they fall back to the live filter path; the visual result is
/// the same.
class ImageBackground extends StatefulWidget {
  const ImageBackground({
    super.key,
    required this.path,
    this.blurSigma = 0,
    this.scale = 1.2,
    this.dim = 0.4,
  });

  /// Absolute path chosen by the file picker.
  final String path;

  /// Gaussian blur radius in logical pixels (0–80).
  final double blurSigma;

  /// Zoom around the centre (1–2).
  final double scale;

  /// Black scrim opacity (0.3–0.9). Keeps light text readable.
  final double dim;

  @override
  State<ImageBackground> createState() => _ImageBackgroundState();
}

class _ImageBackgroundState extends State<ImageBackground> {
  static bool get _canBake => !Platform.environment.containsKey('FLUTTER_TEST');

  ImageStream? _stream;
  ImageStreamListener? _listener;
  ImageProvider? _provider;

  /// Decoded source owned by the image cache; never disposed here.
  ui.Image? _source;

  /// Baked texture; non-null after the first post-frame bake.
  ui.Image? _baked;
  Object? _bakedKey;
  bool _failed = false;
  String? _resolvedPath;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // MediaQuery is only available from here; it supplies the decode budget.
    if (_resolvedPath != widget.path) {
      _resolveImage();
    }
  }

  @override
  void didUpdateWidget(ImageBackground oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.path != widget.path) {
      _resolveImage();
      return;
    }
    if (oldWidget.blurSigma != widget.blurSigma ||
        oldWidget.scale != widget.scale ||
        oldWidget.dim != widget.dim) {
      _bakedKey = null;
    }
  }

  @override
  void dispose() {
    _detach();
    _baked?.dispose();
    super.dispose();
  }

  void _detach() {
    final stream = _stream;
    final listener = _listener;
    if (stream != null && listener != null) {
      stream.removeListener(listener);
    }
    _stream = null;
    _listener = null;
  }

  void _resolveImage() {
    _detach();
    _source = null;
    _bakedKey = null;
    _failed = false;
    _resolvedPath = widget.path;

    final media = MediaQuery.maybeOf(context);
    final dpr = media?.devicePixelRatio ?? 1;
    final size = media?.size ?? const Size(1920, 1080);
    final decodeWidth = (size.width * dpr).round().clamp(1, 2560);
    final decodeHeight = (size.height * dpr).round().clamp(1, 2560);
    final provider = ResizeImage.resizeIfNeeded(
      decodeWidth,
      decodeHeight,
      FileImage(File(widget.path)),
    );
    _provider = provider;

    final listener = ImageStreamListener(
      (info, _) {
        if (!mounted || _resolvedPath != widget.path) return;
        _source = info.image;
        _bakedKey = null;
        setState(() {});
      },
      onError: (_, _) {
        if (!mounted) return;
        setState(() => _failed = true);
      },
    );
    _listener = listener;
    _stream = provider.resolve(ImageConfiguration.empty)..addListener(listener);
  }

  /// Re-bakes when the image, viewport, DPR or appearance parameters change.
  void _scheduleBake(ui.Image source, Size size, double dpr) {
    final key = Object.hash(
      identityHashCode(source),
      size.width,
      size.height,
      dpr,
      widget.blurSigma,
      widget.scale,
      widget.dim,
    );
    if (key == _bakedKey) return;
    _bakedKey = key;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || key != _bakedKey) return;
      final baked = _bake(source, size, dpr);
      if (baked == null) return;
      final old = _baked;
      setState(() => _baked = baked);
      if (old != null) {
        WidgetsBinding.instance.addPostFrameCallback((_) => old.dispose());
      }
    });
  }

  ui.Image? _bake(ui.Image source, Size size, double dpr) {
    final width = (size.width * dpr).round().clamp(1, 8192);
    final height = (size.height * dpr).round().clamp(1, 8192);
    final output = Size(width.toDouble(), height.toDouble());
    final recorder = ui.PictureRecorder();
    final canvas = Canvas(recorder, Offset.zero & output);

    final paint = Paint()..filterQuality = FilterQuality.low;
    if (widget.blurSigma > 0) {
      paint.imageFilter = ui.ImageFilter.blur(
        sigmaX: widget.blurSigma * dpr,
        sigmaY: widget.blurSigma * dpr,
        tileMode: TileMode.clamp,
      );
    }

    final sourceSize = Size(source.width.toDouble(), source.height.toDouble());
    final fitted = applyBoxFit(BoxFit.cover, sourceSize, output);
    final sourceRect = Alignment.center.inscribe(
      fitted.source,
      Offset.zero & sourceSize,
    );
    final destinationRect = Alignment.center.inscribe(
      fitted.destination,
      Offset.zero & output,
    );

    canvas.save();
    if (widget.scale != 1) {
      canvas
        ..translate(output.width / 2, output.height / 2)
        ..scale(widget.scale)
        ..translate(-output.width / 2, -output.height / 2);
    }
    canvas.drawImageRect(source, sourceRect, destinationRect, paint);
    canvas.restore();

    if (widget.dim > 0) {
      canvas.drawRect(
        Offset.zero & output,
        Paint()..color = Colors.black.withValues(alpha: widget.dim),
      );
    }

    final picture = recorder.endRecording();
    try {
      return picture.toImageSync(width, height);
    } catch (_) {
      return null;
    } finally {
      picture.dispose();
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_failed || _provider == null) return const SizedBox.shrink();
    return LayoutBuilder(
      builder: (context, constraints) {
        final size = Size(
          constraints.maxWidth.isFinite ? constraints.maxWidth : 1920,
          constraints.maxHeight.isFinite ? constraints.maxHeight : 1080,
        );
        final source = _source;
        if (_canBake && source != null) {
          _scheduleBake(source, size, MediaQuery.devicePixelRatioOf(context));
          final baked = _baked;
          if (baked != null) {
            return ClipRect(
              child: RawImage(
                image: baked,
                width: size.width,
                height: size.height,
                fit: BoxFit.fill,
                filterQuality: FilterQuality.low,
              ),
            );
          }
        }
        return _live(size);
      },
    );
  }

  /// Live fallback used before the first bake and under widget tests.
  Widget _live(Size size) {
    Widget image = Image(
      image: _provider!,
      width: size.width,
      height: size.height,
      fit: BoxFit.cover,
      gaplessPlayback: true,
      errorBuilder: (_, _, _) => const SizedBox.shrink(),
    );
    if (widget.blurSigma > 0) {
      image = ImageFiltered(
        imageFilter: ui.ImageFilter.blur(
          sigmaX: widget.blurSigma,
          sigmaY: widget.blurSigma,
          tileMode: TileMode.clamp,
        ),
        child: image,
      );
    }
    if (widget.scale != 1) {
      image = Transform.scale(scale: widget.scale, child: image);
    }
    return ClipRect(
      child: Stack(
        fit: StackFit.expand,
        children: [
          image,
          ColoredBox(color: Colors.black.withValues(alpha: widget.dim)),
        ],
      ),
    );
  }
}
