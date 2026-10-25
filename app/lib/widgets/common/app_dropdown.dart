import 'package:flutter/material.dart';

import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';

/// Compact dropdown that uses the same surface, border and corner language as
/// the themed text fields.
///
/// Flutter's [DropdownButton] does not expose the popup's shape or border, so
/// this builds the control and its menu with [MenuAnchor]. The menu therefore
/// follows the same beveled panel style as the rest of the shell instead of
/// the default rounded Material menu.
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

    return MenuAnchor(
      style: _menuStyle(tokens),
      consumeOutsideTap: true,
      menuChildren: _menuChildren(context, tokens, value, enabled),
      builder: (context, controller, _) {
        return Material(
          color: Colors.transparent,
          child: InkWell(
            borderRadius: BorderRadius.circular(tokens.radiusM),
            onTap: enabled
                ? () =>
                      controller.isOpen ? controller.close() : controller.open()
                : null,
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
                        color: enabled
                            ? tokens.textPrimary
                            : tokens.textTertiary,
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
        );
      },
    );
  }

  List<Widget> _menuChildren(
    BuildContext context,
    AppTokens tokens,
    T? value,
    bool enabled,
  ) {
    final textTheme = Theme.of(context).textTheme;
    return [
      for (final item in items)
        MenuItemButton(
          onPressed: enabled && item.enabled
              ? () => onChanged!(item.value)
              : null,
          leadingIcon: item.value == value
              ? Icon(AppIcons.check, size: 16, color: tokens.accent)
              : null,
          style: ButtonStyle(
            foregroundColor: WidgetStatePropertyAll(tokens.textPrimary),
            backgroundColor: WidgetStateProperty.resolveWith((states) {
              if (states.contains(WidgetState.hovered) ||
                  states.contains(WidgetState.focused)) {
                return tokens.surface2;
              }
              if (item.value == value) return tokens.accentSoft;
              return Colors.transparent;
            }),
            padding: const WidgetStatePropertyAll(
              EdgeInsets.symmetric(
                horizontal: AppSpacing.sm,
                vertical: AppSpacing.xs,
              ),
            ),
            textStyle: WidgetStatePropertyAll(textTheme.bodyMedium),
          ),
          child: DefaultTextStyle.merge(
            style: textTheme.bodyMedium,
            child: item.child,
          ),
        ),
    ];
  }
}

/// Form-field variant of [AppDropdown] for dialogs.
///
/// Keeps the caller's label/hint decoration but shares the beveled menu,
/// surface color and semantic chevron with the compact control.
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

    return MenuAnchor(
      style: _menuStyle(tokens),
      consumeOutsideTap: true,
      menuChildren: [
        for (final item in items)
          MenuItemButton(
            onPressed: enabled && item.enabled
                ? () => onChanged!(item.value)
                : null,
            leadingIcon: item.value == initialValue
                ? Icon(AppIcons.check, size: 16, color: tokens.accent)
                : null,
            style: ButtonStyle(
              foregroundColor: WidgetStatePropertyAll(tokens.textPrimary),
              backgroundColor: WidgetStateProperty.resolveWith((states) {
                if (states.contains(WidgetState.hovered) ||
                    states.contains(WidgetState.focused)) {
                  return tokens.surface2;
                }
                if (item.value == initialValue) return tokens.accentSoft;
                return Colors.transparent;
              }),
              padding: const WidgetStatePropertyAll(
                EdgeInsets.symmetric(
                  horizontal: AppSpacing.sm,
                  vertical: AppSpacing.xs,
                ),
              ),
              textStyle: WidgetStatePropertyAll(textTheme.bodyMedium),
            ),
            child: DefaultTextStyle.merge(
              style: textTheme.bodyMedium,
              child: item.child,
            ),
          ),
      ],
      builder: (context, controller, _) {
        return Material(
          color: Colors.transparent,
          child: InkWell(
            borderRadius: BorderRadius.circular(tokens.radiusM),
            onTap: enabled
                ? () =>
                      controller.isOpen ? controller.close() : controller.open()
                : null,
            child: InputDecorator(
              decoration: decoration.copyWith(enabled: enabled),
              isEmpty: selected == null,
              child: Row(
                mainAxisSize: isExpanded ? MainAxisSize.max : MainAxisSize.min,
                children: [
                  Flexible(
                    child: DefaultTextStyle.merge(
                      style: textTheme.bodyMedium?.copyWith(
                        color: enabled
                            ? tokens.textPrimary
                            : tokens.textTertiary,
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
        );
      },
    );
  }
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

MenuStyle _menuStyle(AppTokens tokens) {
  return MenuStyle(
    alignment: const AlignmentDirectional(-1, 1),
    backgroundColor: WidgetStatePropertyAll(tokens.surface1),
    surfaceTintColor: const WidgetStatePropertyAll(Colors.transparent),
    shadowColor: WidgetStatePropertyAll(Colors.black.withValues(alpha: 0.35)),
    elevation: const WidgetStatePropertyAll(8),
    padding: const WidgetStatePropertyAll(
      EdgeInsets.symmetric(vertical: AppSpacing.xs),
    ),
    minimumSize: const WidgetStatePropertyAll(Size(200, 0)),
    maximumSize: const WidgetStatePropertyAll(Size(420, 480)),
    side: WidgetStatePropertyAll(BorderSide(color: tokens.border)),
    shape: WidgetStatePropertyAll(
      BeveledRectangleBorder(
        borderRadius: BorderRadius.only(
          topRight: Radius.circular(tokens.bevelL),
          bottomLeft: Radius.circular(tokens.bevelS),
        ),
      ),
    ),
  );
}
