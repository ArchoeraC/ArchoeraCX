import 'package:flutter/material.dart';

/// Opens a context menu at a pointer position reported by a secondary click.
///
/// Callers share the item list with their toolbar/overflow button so the two
/// entry points cannot drift apart.
Future<T?> showContextMenuAt<T>(
  BuildContext context, {
  required Offset globalPosition,
  required List<PopupMenuEntry<T>> items,
}) {
  final overlay = Overlay.of(context).context.findRenderObject();
  if (overlay is! RenderBox) return Future<T?>.value();

  final local = overlay.globalToLocal(globalPosition);
  return showMenu<T>(
    context: context,
    position: RelativeRect.fromRect(
      local & Size.zero,
      Offset.zero & overlay.size,
    ),
    items: items,
  );
}
