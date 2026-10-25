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
    required this.items,
    required this.onSelected,
    this.tooltip,
    this.icon,
    this.child,
    this.initialValue,
  });

  final List<AppMenuEntry<T>> items;
  final ValueChanged<T> onSelected;
  final String? tooltip;
  final IconData? icon;
  final Widget? child;
  final T? initialValue;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final hasChild = child != null;
    return PopupMenuButton<T>(
      tooltip: tooltip,
      padding: hasChild ? EdgeInsets.zero : const EdgeInsets.all(8),
      initialValue: initialValue,
      onSelected: onSelected,
      itemBuilder: (context) => appMenuEntries(context, items),
      icon: hasChild || icon == null
          ? null
          : Icon(icon, color: tokens.textSecondary),
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
