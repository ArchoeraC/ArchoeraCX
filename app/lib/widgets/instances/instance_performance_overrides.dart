import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/game_instance.dart';
import '../../theme/tokens.dart';

/// Mutable tri-state values for the Linux-only per-instance performance
/// overrides. `null` means "follow the global switch".
class InstancePerformanceOverridesController {
  InstancePerformanceOverridesController({InstanceOverrides? overrides})
    : enableGameMode = overrides?.enableGameMode,
      enableMangoHud = overrides?.enableMangoHud,
      useZink = overrides?.useZink;

  bool? enableGameMode;
  bool? enableMangoHud;
  bool? useZink;
}

/// Three inherit/on/off rows used by the instance settings dialog.
///
/// A switch cannot express the third state, so each option uses a compact
/// dropdown that also fits the narrow dialog layout.
class InstancePerformanceOverrides extends StatelessWidget {
  const InstancePerformanceOverrides({
    super.key,
    required this.controller,
    required this.enabled,
    required this.onChanged,
  });

  final InstancePerformanceOverridesController controller;
  final bool enabled;
  final VoidCallback onChanged;

  static const inherit = 'inherit';
  static const on = 'on';
  static const off = 'off';

  static String _wire(bool? value) => switch (value) {
    true => on,
    false => off,
    null => inherit,
  };

  static bool? _value(String? wire) => switch (wire) {
    on => true,
    off => false,
    _ => null,
  };

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(l10n.instanceSettingsPerformance, style: textTheme.labelLarge),
        const SizedBox(height: 2),
        Text(
          l10n.instanceSettingsPerformanceHint,
          style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(height: AppSpacing.xs),
        _row(
          context: context,
          key: const ValueKey('instance-gamemode-override'),
          l10n: l10n,
          label: l10n.instanceSettingsGameMode,
          value: controller.enableGameMode,
          onChanged: (value) {
            controller.enableGameMode = value;
            onChanged();
          },
        ),
        _row(
          context: context,
          key: const ValueKey('instance-mangohud-override'),
          l10n: l10n,
          label: l10n.instanceSettingsMangoHud,
          value: controller.enableMangoHud,
          onChanged: (value) {
            controller.enableMangoHud = value;
            onChanged();
          },
        ),
        _row(
          context: context,
          key: const ValueKey('instance-zink-override'),
          l10n: l10n,
          label: l10n.instanceSettingsZink,
          value: controller.useZink,
          onChanged: (value) {
            controller.useZink = value;
            onChanged();
          },
        ),
      ],
    );
  }

  Widget _row({
    required BuildContext context,
    required Key key,
    required AppLocalizations l10n,
    required String label,
    required bool? value,
    required ValueChanged<bool?> onChanged,
  }) {
    return Row(
      children: [
        Expanded(
          child: Text(label, style: Theme.of(context).textTheme.bodyMedium),
        ),
        const SizedBox(width: AppSpacing.sm),
        DropdownButton<String>(
          key: key,
          value: _wire(value),
          isDense: true,
          underline: const SizedBox.shrink(),
          borderRadius: BorderRadius.circular(context.tokens.radiusM),
          items: [
            DropdownMenuItem(
              value: inherit,
              child: Text(l10n.instanceSettingsOverrideInherit),
            ),
            DropdownMenuItem(
              value: on,
              child: Text(l10n.instanceSettingsOverrideOn),
            ),
            DropdownMenuItem(
              value: off,
              child: Text(l10n.instanceSettingsOverrideOff),
            ),
          ],
          onChanged: enabled ? (wire) => onChanged(_value(wire)) : null,
        ),
      ],
    );
  }
}
