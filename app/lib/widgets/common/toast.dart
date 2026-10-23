import 'dart:async';

import 'package:flutter/material.dart';

import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import 'app_panel.dart';

enum ToastType { info, success, warning, error }

class ToastItem {
  ToastItem({
    required this.id,
    required this.type,
    required this.message,
    required this.duration,
  });

  final int id;
  final ToastType type;
  final String message;
  final Duration duration;
  bool leaving = false;
}

/// Global toast queue. Unlike SnackBars, toasts live above the navigator and
/// never steal focus or block the underlying page.
class ToastController extends ChangeNotifier {
  static const int maxToasts = 5;

  final List<ToastItem> _items = [];
  int _nextId = 0;

  /// Applied when a caller does not pass an explicit duration. Settings lets
  /// the user make toasts linger longer (or get out of the way sooner).
  Duration defaultDuration = const Duration(milliseconds: 3000);

  List<ToastItem> get items => List.unmodifiable(_items);

  void show(
    String message, {
    ToastType type = ToastType.info,
    Duration? duration,
  }) {
    final resolved = duration ?? defaultDuration;
    while (_items.length >= maxToasts) {
      _items.removeAt(0);
    }
    final item = ToastItem(
      id: _nextId++,
      type: type,
      message: message,
      duration: resolved,
    );
    _items.add(item);
    notifyListeners();
    Timer(item.duration, () => dismiss(item.id));
  }

  void dismiss(int id) {
    final index = _items.indexWhere((item) => item.id == id);
    if (index == -1 || _items[index].leaving) return;
    _items[index].leaving = true;
    notifyListeners();
  }

  void remove(int id) {
    final before = _items.length;
    _items.removeWhere((item) => item.id == id);
    if (_items.length != before) notifyListeners();
  }
}

final ToastController toastController = ToastController();

/// Convenience entry point: `toast('Saved', type: ToastType.success)`.
void toast(
  String message, {
  ToastType type = ToastType.info,
  Duration? duration,
}) {
  toastController.show(message, type: type, duration: duration);
}

/// Mounted under MaterialApp.builder, above the Navigator.
class ToastOverlay extends StatelessWidget {
  const ToastOverlay({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Stack(
      fit: StackFit.expand,
      children: [
        child,
        Positioned(
          top:
              MediaQuery.paddingOf(context).top + AppLayout.titleBarHeight + 12,
          left: 0,
          right: 0,
          child: IgnorePointer(
            child: ListenableBuilder(
              listenable: toastController,
              builder: (context, _) => Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (final item in toastController.items)
                    _ToastItemView(key: ValueKey(item.id), item: item),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _ToastItemView extends StatefulWidget {
  const _ToastItemView({super.key, required this.item});

  final ToastItem item;

  @override
  State<_ToastItemView> createState() => _ToastItemViewState();
}

class _ToastItemViewState extends State<_ToastItemView>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 180),
  );

  late final CurvedAnimation _in = CurvedAnimation(
    parent: _controller,
    curve: Curves.easeOutCubic,
  );
  late final CurvedAnimation _out = CurvedAnimation(
    parent: _controller,
    curve: Curves.easeInCubic,
    reverseCurve: Curves.easeInCubic,
  );

  bool _noAnimations = false;
  bool _removing = false;

  @override
  void initState() {
    super.initState();
    _controller.forward();
    _controller.addStatusListener(_onStatusChanged);
    toastController.addListener(_onControllerChanged);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _noAnimations = MediaQuery.maybeDisableAnimationsOf(context) ?? false;
    if (_noAnimations && _controller.value < 1) _controller.value = 1;
  }

  @override
  void dispose() {
    toastController.removeListener(_onControllerChanged);
    _controller.removeStatusListener(_onStatusChanged);
    _controller.dispose();
    super.dispose();
  }

  void _onStatusChanged(AnimationStatus status) {
    if (status == AnimationStatus.completed) _maybeLeave();
  }

  void _onControllerChanged() {
    if (widget.item.leaving) _maybeLeave();
  }

  void _maybeLeave() {
    if (!widget.item.leaving || _removing) return;
    if (_noAnimations) {
      _removing = true;
      toastController.remove(widget.item.id);
      return;
    }
    // The enter animation may still be running when a short-lived toast is
    // dismissed; wait for it to finish instead of silently ignoring the leave.
    if (_controller.isAnimating || _controller.value < 1) return;
    _removing = true;
    _controller.reverse().then((_) {
      if (mounted) toastController.remove(widget.item.id);
    });
  }

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final color = switch (widget.item.type) {
      ToastType.success => tokens.success,
      ToastType.warning => tokens.warning,
      ToastType.error => tokens.danger,
      ToastType.info => tokens.accent,
    };
    final icon = switch (widget.item.type) {
      ToastType.success => AppIcons.completed,
      ToastType.warning => AppIcons.warning,
      ToastType.error => AppIcons.error,
      ToastType.info => AppIcons.info,
    };
    final shape = AppShapes.panel(
      tokens,
      side: BorderSide(color: color.withValues(alpha: 0.6)),
    );

    return AnimatedBuilder(
      animation: _controller,
      builder: (context, child) {
        final value = widget.item.leaving ? _out.value : _in.value;
        final offset = widget.item.leaving
            ? -10 * (1 - value)
            : 10 * (1 - value);
        return Opacity(
          opacity: value,
          child: Transform.translate(offset: Offset(0, offset), child: child),
        );
      },
      child: Padding(
        padding: const EdgeInsets.only(bottom: AppSpacing.xs),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 460),
          child: Material(
            color: Colors.transparent,
            child: DecoratedBox(
              decoration: ShapeDecoration(
                color: tokens.surface1,
                shape: shape,
                shadows: [
                  BoxShadow(
                    color: tokens.overlay.withValues(alpha: 0.18),
                    blurRadius: 16,
                    offset: const Offset(0, 6),
                  ),
                ],
              ),
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: AppSpacing.md,
                  vertical: AppSpacing.sm,
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(icon, size: 18, color: color),
                    const SizedBox(width: AppSpacing.xs),
                    Flexible(
                      child: Text(
                        widget.item.message,
                        maxLines: 3,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.bodySmall?.copyWith(
                          color: tokens.textPrimary,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
