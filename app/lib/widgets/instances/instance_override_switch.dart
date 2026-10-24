import 'package:flutter/material.dart';

import '../../theme/tokens.dart';

/// A compact switch row used by the instance settings dialog for optional
/// sections whose "on" state has its own meaning (no wrapper, empty map).
class InstanceOverrideSwitch extends StatelessWidget {
  const InstanceOverrideSwitch({
    super.key,
    required this.title,
    this.subtitle,
    required this.value,
    required this.onChanged,
  });

  final String title;
  final String? subtitle;
  final bool value;
  final ValueChanged<bool>? onChanged;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return SwitchListTile(
      contentPadding: EdgeInsets.zero,
      dense: true,
      title: Text(title),
      subtitle: subtitle == null
          ? null
          : Text(
              subtitle!,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textTertiary),
            ),
      value: value,
      onChanged: onChanged,
    );
  }
}
