import 'package:flutter/material.dart';

import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';

/// Compact dropdown that uses the same surface, border and corner language as
/// the themed text fields.
///
/// Flutter has no `DropdownButton` theme, so the raw widget in a settings row
/// otherwise falls back to a transparent, underline-based control. The key is
/// forwarded to the inner [DropdownButton] so existing tests and callers keep
/// finding the actual control.
class AppDropdown<T> extends StatelessWidget {
  const AppDropdown({
    Key? key,
    required this.value,
    required this.items,
    required this.onChanged,
    this.hint,
    this.isExpanded = false,
  }) : _dropdownKey = key,
       super(key: null);

  final T? value;
  final List<DropdownMenuItem<T>> items;
  final ValueChanged<T?>? onChanged;
  final Widget? hint;
  final bool isExpanded;

  final Key? _dropdownKey;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final enabled = onChanged != null;
    final textTheme = Theme.of(context).textTheme;

    return Container(
      padding: const EdgeInsetsDirectional.only(start: 10, end: 6),
      decoration: BoxDecoration(
        color: tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusM),
        border: Border.all(color: tokens.border),
      ),
      child: DropdownButtonHideUnderline(
        child: DropdownButton<T>(
          key: _dropdownKey,
          value: value,
          hint: hint,
          isDense: true,
          isExpanded: isExpanded,
          borderRadius: BorderRadius.circular(tokens.radiusM),
          dropdownColor: tokens.surface1,
          icon: Icon(
            AppIcons.chevronDown,
            size: 16,
            color: enabled ? tokens.textSecondary : tokens.textTertiary,
          ),
          style: textTheme.bodyMedium?.copyWith(
            color: enabled ? tokens.textPrimary : tokens.textTertiary,
          ),
          items: items,
          onChanged: onChanged,
        ),
      ),
    );
  }
}

/// Form-field variant of [AppDropdown] for dialogs.
///
/// Keeps the caller's label/hint decoration but shares the popup color,
/// corner radius and semantic chevron with the compact control.
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
    return DropdownButtonFormField<T>(
      initialValue: initialValue,
      isExpanded: isExpanded,
      hint: hint,
      borderRadius: BorderRadius.circular(tokens.radiusM),
      dropdownColor: tokens.surface1,
      style: Theme.of(context).textTheme.bodyMedium,
      icon: Icon(
        AppIcons.chevronDown,
        size: 18,
        color: onChanged == null ? tokens.textTertiary : tokens.textSecondary,
      ),
      decoration: decoration,
      items: items,
      onChanged: onChanged,
    );
  }
}
