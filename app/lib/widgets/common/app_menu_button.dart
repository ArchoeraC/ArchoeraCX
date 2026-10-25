import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

/// One action entry in an [AppMenuButton].
///
/// Action menus share rendering and popup placement, while each page only
/// supplies the commands and their labels.
class AppMenuEntry<T> {
  const AppMenuEntry({
    required this.value,
    required this.label,
    this.icon,
    this.enabled = true,
    this.danger = false,
  });

  final T value;
  final String label;
  final IconData? icon;
  final bool enabled;
  final bool danger;
}

/// Shared action menu (overflow/context menu) used by instance, account and
/// download toolbars.
///
/// Uses [PopupMenuButton], the same primitive as [AppDropdown], so placement,
/// keyboard handling and the beveled `popupMenuTheme` stay identical across
/// the shell.
class AppMenuButton<T> extends StatelessWidget {
  const AppMenuButton({
    super.key,
    this.items = const [],
    this.itemBuilder,
    required this.onSelected,
    this.enabled = true,
    this.tooltip,
    this.icon,
    this.child,
    this.initialValue,
    this.constraints,
    this.iconSize,
  }) : assert(
         itemBuilder != null || items.length > 0,
         'Provide items or an itemBuilder.',
       );

  final List<AppMenuEntry<T>> items;

  /// Overrides the default [AppMenuEntry] rendering for menus with richer
  /// rows (for example the Java runtime picker) while keeping the shared
  /// button, placement and popup theme.
  final PopupMenuItemBuilder<T>? itemBuilder;

  final ValueChanged<T> onSelected;
  final bool enabled;
  final String? tooltip;
  final IconData? icon;
  final Widget? child;
  final T? initialValue;
  final BoxConstraints? constraints;
  final double? iconSize;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final hasChild = child != null;
    return PopupMenuButton<T>(
      tooltip: tooltip,
      enabled: enabled,
      padding: hasChild ? EdgeInsets.zero : const EdgeInsets.all(8),
      initialValue: initialValue,
      constraints: constraints,
      clipBehavior: Clip.antiAlias,
      onSelected: onSelected,
      itemBuilder: itemBuilder ?? (context) => appMenuEntries(context, items),
      icon: hasChild || icon == null
          ? null
          : Icon(icon, size: iconSize, color: tokens.textSecondary),
      child: child,
    );
  }
}

/// Builds the popup entries for [AppMenuButton] and for context menus opened
/// with `showContextMenuAt`, so both entry points share one representation.
List<PopupMenuEntry<T>> appMenuEntries<T>(
  BuildContext context,
  List<AppMenuEntry<T>> items,
) {
  final tokens = context.tokens;
  return [
    for (final item in items)
      PopupMenuItem<T>(
        value: item.value,
        enabled: item.enabled,
        child: Row(
          children: [
            if (item.icon != null) ...[
              Icon(
                item.icon,
                size: 20,
                color: item.danger ? tokens.danger : null,
              ),
              const SizedBox(width: AppSpacing.xs),
            ],
            Flexible(
              child: Text(
                item.label,
                overflow: TextOverflow.ellipsis,
                style: item.danger ? TextStyle(color: tokens.danger) : null,
              ),
            ),
          ],
        ),
      ),
  ];
}
