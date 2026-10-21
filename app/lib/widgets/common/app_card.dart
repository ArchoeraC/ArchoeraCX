import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

class AppCard extends StatefulWidget {
  const AppCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(20),
    this.onTap,
    this.selected = false,
    this.compact = false,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;
  final bool selected;
  final bool compact;

  @override
  State<AppCard> createState() => _AppCardState();
}

class _AppCardState extends State<AppCard> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final interactive = widget.onTap != null;
    final background = widget.selected
        ? tokens.accentSoft
        : (_hovered && interactive ? tokens.surface2 : tokens.surface1);
    final border = widget.selected || (_hovered && interactive)
        ? tokens.borderStrong
        : tokens.border;

    final content = Padding(padding: widget.padding, child: widget.child);

    return MouseRegion(
      cursor: interactive ? SystemMouseCursors.click : MouseCursor.defer,
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: AnimatedContainer(
        duration: tokens.fast,
        curve: tokens.curve,
        decoration: BoxDecoration(
          color: background,
          borderRadius: BorderRadius.circular(
            widget.compact ? tokens.radiusM : tokens.radiusL,
          ),
          border: Border.all(color: border),
        ),
        child: Material(
          color: Colors.transparent,
          child: interactive
              ? InkWell(
                  onTap: widget.onTap,
                  borderRadius: BorderRadius.circular(
                    widget.compact ? tokens.radiusM : tokens.radiusL,
                  ),
                  child: content,
                )
              : content,
        ),
      ),
    );
  }
}
