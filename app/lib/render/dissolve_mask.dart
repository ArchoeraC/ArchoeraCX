import 'dart:ui' as ui;

import 'package:flutter/material.dart';

/// Dissolves [child] away as [progress] runs from 0 to 1.
///
/// The outgoing page/section is captured as a GPU texture (`toImageSync`) and
/// masked with the shared `transition.frag` program. Only alpha is animated,
/// so the mask adds no extra content layer; the shader also avoids creating a
/// gradient or blur object on every frame.
///
/// A plain opacity fade is the fallback when no program is available, which is
/// the pre-shader behaviour and is also what reduced-motion sessions use.
class AppDissolveMask extends StatefulWidget {
  const AppDissolveMask({
    super.key,
    required this.progress,
    required this.program,
    required this.child,
    this.softness = 0.35,
  });

  /// 0 = child fully visible, 1 = child fully removed.
  final Animation<double> progress;

  /// Shared dissolve program, or null to keep the opacity fallback.
  final ui.FragmentProgram? program;

  final Widget child;

  /// Half-width of the dissolve edge in normalized units.
  final double softness;

  @override
  State<AppDissolveMask> createState() => _AppDissolveMaskState();
}

class _AppDissolveMaskState extends State<AppDissolveMask> {
  ui.FragmentProgram? _program;
  ui.FragmentShader? _shader;

  @override
  void initState() {
    super.initState();
    _bind();
  }

  @override
  void didUpdateWidget(covariant AppDissolveMask oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.program, widget.program)) _bind();
  }

  @override
  void dispose() {
    _shader?.dispose();
    super.dispose();
  }

  void _bind() {
    _shader?.dispose();
    _shader = null;
    _program = widget.program;
    if (_program != null) _shader = _program!.fragmentShader();
  }

  @override
  Widget build(BuildContext context) {
    final shader = _shader;
    final program = _program;
    if (shader == null || program == null) {
      return FadeTransition(
        opacity: ReverseAnimation(widget.progress),
        child: widget.child,
      );
    }

    // Rebuilding just this mask per frame is cheap; the child texture is
    // handed to AnimatedBuilder so it is never rebuilt.
    return AnimatedBuilder(
      animation: widget.progress,
      builder: (context, child) => ShaderMask(
        blendMode: BlendMode.dstIn,
        shaderCallback: (rect) {
          shader
            ..setFloat(0, rect.width)
            ..setFloat(1, rect.height)
            ..setFloat(2, widget.progress.value)
            ..setFloat(3, widget.softness);
          return shader;
        },
        child: child,
      ),
      child: widget.child,
    );
  }
}
