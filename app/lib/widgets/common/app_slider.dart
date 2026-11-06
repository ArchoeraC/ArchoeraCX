import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme/tokens.dart';

/// Flat accent slider used by the appearance rows.
///
/// [Slider] is not used here on purpose: the Material 3 slider paints tick
/// dots for [divisions], a value bubble and a wide overlay halo, which reads as
/// a different visual language inside the settings panels. This slider follows
/// the reference player's look instead: a 4 px rounded track, a 16 px
/// surface-coloured thumb with an accent ring (so it stays readable on top of
/// the filled part), and optional range labels under the track. Dragging snaps
/// to [divisions] so the value grid is explicit.
class AppSlider extends StatefulWidget {
  const AppSlider({
    super.key,
    required this.value,
    required this.min,
    required this.max,
    required this.onChanged,
    this.divisions,
    this.valueLabel,
    this.valueFormatter,
    this.semanticLabel,
    this.marks,
  });

  /// Current value; clamped into `[min, max]` before painting.
  final double value;

  final double min;
  final double max;

  /// Called for every step while the pointer is down.
  final ValueChanged<double> onChanged;

  /// Number of equal steps between [min] and [max], or null for continuous.
  final int? divisions;

  /// Text shown to accessibility services, typically the formatted value.
  final String? valueLabel;

  /// Formats any value on the slider's grid, used for announcements.
  final String Function(double value)? valueFormatter;

  /// Optional label announcing what this slider controls.
  final String? semanticLabel;

  /// Labels under the track, keyed by the value they mark.
  ///
  /// The row is informational: taps still land on the track, never on a label.
  final Map<double, String>? marks;

  @override
  State<AppSlider> createState() => _AppSliderState();
}

class _AppSliderState extends State<AppSlider> {
  static const double _trackHeight = 4;
  static const double _thumbSize = 16;
  static const double _rowHeight = 28;
  static const double _marksHeight = 20;

  final FocusNode _focusNode = FocusNode();

  bool _hovering = false;
  bool _dragging = false;
  bool _focused = false;

  /// Width of the track from the last layout; pointer callbacks need it to
  /// convert the local x position into a value.
  double _width = 0;

  @override
  void dispose() {
    _focusNode.dispose();
    super.dispose();
  }

  double get _span => (widget.max - widget.min).abs() < 1e-9
      ? 0
      : widget.max - widget.min;

  double get _value => widget.value.clamp(widget.min, widget.max);

  double get _ratio => _span == 0 ? 0 : (_value - widget.min) / _span;

  /// Snaps [raw] onto the division grid (when present) and clamps it.
  double _snap(double raw) {
    final divisions = widget.divisions;
    var next = raw.clamp(widget.min, widget.max);
    if (divisions != null && divisions > 0) {
      final step = _span / divisions;
      if (step > 0) {
        // Round to the nearest division instead of truncating.
        final index = ((next - widget.min) / step).round();
        next = widget.min + index * step;
      }
    }
    return next.clamp(widget.min, widget.max);
  }

  void _emit(Offset localPosition) {
    if (_width <= 0) return;
    final ratio = (localPosition.dx / _width).clamp(0.0, 1.0);
    final next = _snap(widget.min + ratio * _span);
    if ((next - _value).abs() < 1e-9) return;
    widget.onChanged(next);
  }

  /// One step of the slider grid.
  double _stepValue(int direction) {
    final divisions = widget.divisions ?? 100;
    final step = divisions > 0 ? _span / divisions : _span;
    return _snap(_value + step * direction);
  }

  void _nudge(int direction) => widget.onChanged(_stepValue(direction));

  KeyEventResult _onKey(FocusNode node, KeyEvent event) {
    if (event is! KeyDownEvent && event is! KeyRepeatEvent) {
      return KeyEventResult.ignored;
    }
    final key = event.logicalKey;
    if (key == LogicalKeyboardKey.arrowLeft ||
        key == LogicalKeyboardKey.arrowDown) {
      _nudge(-1);
      return KeyEventResult.handled;
    }
    if (key == LogicalKeyboardKey.arrowRight ||
        key == LogicalKeyboardKey.arrowUp) {
      _nudge(1);
      return KeyEventResult.handled;
    }
    if (key == LogicalKeyboardKey.home) {
      widget.onChanged(widget.min);
      return KeyEventResult.handled;
    }
    if (key == LogicalKeyboardKey.end) {
      widget.onChanged(widget.max);
      return KeyEventResult.handled;
    }
    return KeyEventResult.ignored;
  }

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final active = _hovering || _dragging || _focused;
    final track = tokens.textPrimary.withValues(alpha: 0.18);
    final thumbGlow = tokens.accent.withValues(alpha: _dragging ? 0.32 : 0.20);
    final formatter = widget.valueFormatter;
    final marks = widget.marks;

    return Semantics(
      slider: true,
      label: widget.semanticLabel,
      value: widget.valueLabel ?? formatter?.call(_value),
      increasedValue: formatter?.call(_stepValue(1)),
      decreasedValue: formatter?.call(_stepValue(-1)),
      onIncrease: formatter == null ? null : () => _nudge(1),
      onDecrease: formatter == null ? null : () => _nudge(-1),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Focus(
            focusNode: _focusNode,
            onFocusChange: (value) => setState(() => _focused = value),
            onKeyEvent: _onKey,
            child: MouseRegion(
              cursor: SystemMouseCursors.click,
              onEnter: (_) => setState(() => _hovering = true),
              onExit: (_) => setState(() => _hovering = false),
              child: GestureDetector(
                behavior: HitTestBehavior.opaque,
                onTapDown: (details) {
                  _focusNode.requestFocus();
                  setState(() => _dragging = true);
                  _emit(details.localPosition);
                },
                onTapUp: (_) => setState(() => _dragging = false),
                onTapCancel: () => setState(() => _dragging = false),
                onHorizontalDragStart: (details) {
                  _focusNode.requestFocus();
                  setState(() => _dragging = true);
                  _emit(details.localPosition);
                },
                onHorizontalDragUpdate: (details) =>
                    _emit(details.localPosition),
                onHorizontalDragEnd: (_) => setState(() => _dragging = false),
                onHorizontalDragCancel: () => setState(() => _dragging = false),
                child: SizedBox(
                  height: _rowHeight,
                  child: LayoutBuilder(
                    builder: (context, constraints) {
                      final width = constraints.maxWidth;
                      _width = width;
                      final position = _ratio * width;
                      return Stack(
                        clipBehavior: Clip.none,
                        alignment: Alignment.centerLeft,
                        children: [
                          // Track.
                          Positioned(
                            left: 0,
                            right: 0,
                            top: (_rowHeight - _trackHeight) / 2,
                            child: DecoratedBox(
                              decoration: BoxDecoration(
                                color: track,
                                borderRadius: BorderRadius.circular(
                                  _trackHeight,
                                ),
                              ),
                              child: const SizedBox(height: _trackHeight),
                            ),
                          ),
                          // Filled part.
                          Positioned(
                            left: 0,
                            top: (_rowHeight - _trackHeight) / 2,
                            width: position,
                            child: DecoratedBox(
                              decoration: BoxDecoration(
                                color: tokens.accent,
                                borderRadius: BorderRadius.circular(
                                  _trackHeight,
                                ),
                              ),
                              child: const SizedBox(height: _trackHeight),
                            ),
                          ),
                          // Thumb. The surface-coloured core keeps the dot
                          // readable where it overlaps the accent fill.
                          Positioned(
                            left: (position - _thumbSize / 2).clamp(
                              0.0,
                              (width - _thumbSize).clamp(0.0, double.infinity),
                            ),
                            top: (_rowHeight - _thumbSize) / 2,
                            child: AnimatedScale(
                              scale: _dragging
                                  ? 1.12
                                  : (_hovering || _focused ? 1.06 : 1),
                              duration: tokens.fast,
                              curve: tokens.curve,
                              child: Container(
                                key: const ValueKey('app-slider-thumb'),
                                width: _thumbSize,
                                height: _thumbSize,
                                decoration: BoxDecoration(
                                  color: tokens.surface1,
                                  shape: BoxShape.circle,
                                  border: Border.all(
                                    color: tokens.accent,
                                    width: 3,
                                  ),
                                  boxShadow: [
                                    BoxShadow(
                                      color: thumbGlow,
                                      blurRadius: active ? 10 : 4,
                                      spreadRadius: active ? 2 : 0,
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ),
                        ],
                      );
                    },
                  ),
                ),
              ),
            ),
          ),
          if (marks != null && marks.isNotEmpty)
            SizedBox(
              height: _marksHeight,
              child: LayoutBuilder(
                builder: (context, constraints) {
                  final width = constraints.maxWidth;
                  return Stack(
                    clipBehavior: Clip.none,
                    children: [
                      for (final mark in marks.entries)
                        _Mark(
                          ratio: _span == 0
                              ? 0
                              : ((mark.key - widget.min) / _span).clamp(0.0, 1.0),
                          label: mark.value,
                          width: width,
                        ),
                    ],
                  );
                },
              ),
            ),
        ],
      ),
    );
  }
}

/// One range label under the track, clamped to the slider's own width.
class _Mark extends StatelessWidget {
  const _Mark({required this.ratio, required this.label, required this.width});

  final double ratio;
  final String label;
  final double width;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Positioned(
      left: ratio * width,
      top: 4,
      child: FractionalTranslation(
        translation: Offset(ratio <= 0 ? 0 : (ratio >= 1 ? -1 : -0.5), 0),
        child: Text(
          label,
          maxLines: 1,
          softWrap: false,
          style: Theme.of(context).textTheme.labelSmall?.copyWith(
            color: tokens.textTertiary,
            fontSize: 11,
            height: 1,
          ),
        ),
      ),
    );
  }
}
