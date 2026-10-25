import 'package:flutter/material.dart';

import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';

/// Compact dropdown that uses the same surface, border and corner language as
/// the themed text fields.
///
/// The popup is a [PopupMenuButton], the same primitive used by the instance
/// and account action menus, so its placement, keyboard handling and the
/// beveled `popupMenuTheme` are identical to those menus.
class AppDropdown<T> extends StatelessWidget {
  const AppDropdown({
    super.key,
    required this.value,
    required this.items,
    required this.onChanged,
    this.hint,
    this.isExpanded = false,
  });

  final T? value;
  final List<DropdownMenuItem<T>> items;
  final ValueChanged<T?>? onChanged;
  final Widget? hint;
  final bool isExpanded;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final enabled = onChanged != null;
    final selected = _selectedItem(value, items);
    final textTheme = Theme.of(context).textTheme;

    return PopupMenuButton<T>(
      enabled: enabled,
      padding: EdgeInsets.zero,
      initialValue: value,
      clipBehavior: Clip.antiAlias,
      onSelected: enabled ? (value) => onChanged!(value) : null,
      itemBuilder: (context) => _menuItems(context, tokens, items, value),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: BorderRadius.circular(tokens.radiusM),
          child: Container(
            padding: const EdgeInsetsDirectional.only(
              start: 10,
              end: 6,
              top: 5,
              bottom: 5,
            ),
            decoration: BoxDecoration(
              color: tokens.surface2,
              borderRadius: BorderRadius.circular(tokens.radiusM),
              border: Border.all(color: tokens.border),
            ),
            child: Row(
              mainAxisSize: isExpanded ? MainAxisSize.max : MainAxisSize.min,
              children: [
                Flexible(
                  child: DefaultTextStyle.merge(
                    style: textTheme.bodyMedium?.copyWith(
                      color: enabled ? tokens.textPrimary : tokens.textTertiary,
                    ),
                    child: selected?.child ?? hint ?? const SizedBox.shrink(),
                  ),
                ),
                const SizedBox(width: AppSpacing.xxs),
                Icon(
                  AppIcons.chevronDown,
                  size: 16,
                  color: enabled ? tokens.textSecondary : tokens.textTertiary,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Form-field variant of [AppDropdown] for dialogs.
///
/// Keeps the caller's label/hint decoration but uses the same popup primitive
/// and theme as the compact control.
class AppDropdownFormField<T> extends StatelessWidget {
  const AppDropdownFormField({
    super.key,
    this.initialValue,
    required this.items,
    required this.onChanged,
    this.decoration = const InputDecoration(),
    this.isExpanded = true,
    this.hint,
  });

  final T? initialValue;
  final List<DropdownMenuItem<T>> items;
  final ValueChanged<T?>? onChanged;
  final InputDecoration decoration;
  final bool isExpanded;
  final Widget? hint;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final enabled = onChanged != null;
    final selected = _selectedItem(initialValue, items);
    final textTheme = Theme.of(context).textTheme;

    return PopupMenuButton<T>(
      enabled: enabled,
      padding: EdgeInsets.zero,
      initialValue: initialValue,
      clipBehavior: Clip.antiAlias,
      onSelected: enabled ? (value) => onChanged!(value) : null,
      itemBuilder: (context) =>
          _menuItems(context, tokens, items, initialValue),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: BorderRadius.circular(tokens.radiusM),
          child: InputDecorator(
            decoration: decoration.copyWith(enabled: enabled),
            isEmpty: selected == null,
            child: Row(
              mainAxisSize: isExpanded ? MainAxisSize.max : MainAxisSize.min,
              children: [
                Flexible(
                  child: DefaultTextStyle.merge(
                    style: textTheme.bodyMedium?.copyWith(
                      color: enabled ? tokens.textPrimary : tokens.textTertiary,
                    ),
                    child: selected?.child ?? hint ?? const SizedBox.shrink(),
                  ),
                ),
                const SizedBox(width: AppSpacing.xxs),
                Icon(
                  AppIcons.chevronDown,
                  size: 18,
                  color: enabled ? tokens.textSecondary : tokens.textTertiary,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

List<PopupMenuEntry<T>> _menuItems<T>(
  BuildContext context,
  AppTokens tokens,
  List<DropdownMenuItem<T>> items,
  T? value,
) {
  final textTheme = Theme.of(context).textTheme;
  return [
    for (final item in items)
      PopupMenuItem<T>(
        value: item.value,
        enabled: item.enabled,
        child: Row(
          children: [
            if (item.value == value)
              Icon(AppIcons.check, size: 16, color: tokens.accent)
            else
              const SizedBox(width: 16),
            const SizedBox(width: AppSpacing.xs),
            Flexible(
              child: DefaultTextStyle.merge(
                style: textTheme.bodyMedium,
                child: item.child,
              ),
            ),
          ],
        ),
      ),
  ];
}

DropdownMenuItem<T>? _selectedItem<T>(
  T? value,
  List<DropdownMenuItem<T>> items,
) {
  for (final item in items) {
    if (item.value == value) return item;
  }
  return null;
}
