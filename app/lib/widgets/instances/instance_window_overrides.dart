import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';

/// Wire values used by the per-instance window mode dropdown.
const String instanceWindowInherit = 'inherit';
const String instanceWindowWindowed = 'windowed';
const String instanceWindowFullscreen = 'fullscreen';
const String instanceWindowMaximized = 'maximized';

/// Window mode and size overrides for the instance settings dialog.
///
/// The dialog owns the controllers and the display-size lookup; this widget
/// only keeps the layout usable at narrow widths.
class InstanceWindowOverrides extends StatelessWidget {
  const InstanceWindowOverrides({
    super.key,
    required this.value,
    required this.inheritLabel,
    required this.sizeHint,
    required this.widthController,
    required this.heightController,
    required this.enabled,
    required this.onChanged,
  });

  /// One of [instanceWindowInherit], [instanceWindowWindowed],
  /// [instanceWindowFullscreen] or [instanceWindowMaximized].
  final String value;

  /// Label for the inherit option, showing the current global mode.
  final String inheritLabel;

  /// Hint for the width/height fields.
  final String sizeHint;

  final TextEditingController widthController;
  final TextEditingController heightController;
  final bool enabled;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        DropdownButtonFormField<String>(
          key: const ValueKey('instance-window-mode'),
          initialValue: value,
          isExpanded: true,
          decoration: InputDecoration(
            labelText: l10n.instanceSettingsWindow,
            prefixIcon: const Icon(AppIcons.window, size: 18),
          ),
          items: [
            DropdownMenuItem(
              value: instanceWindowInherit,
              child: Text(inheritLabel),
            ),
            DropdownMenuItem(
              value: instanceWindowWindowed,
              child: Text(l10n.instanceSettingsWindowWindowed),
            ),
            DropdownMenuItem(
              value: instanceWindowFullscreen,
              child: Text(l10n.instanceSettingsWindowFullscreen),
            ),
            DropdownMenuItem(
              value: instanceWindowMaximized,
              child: Text(l10n.instanceSettingsWindowMaximized),
            ),
          ],
          onChanged: enabled ? (wire) => onChanged(wire ?? value) : null,
        ),
        const SizedBox(height: AppSpacing.sm),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: _numberField(
                controller: widthController,
                label: l10n.instanceSettingsWindowWidth,
                hint: sizeHint,
              ),
            ),
            const SizedBox(width: AppSpacing.sm),
            Expanded(
              child: _numberField(
                controller: heightController,
                label: l10n.instanceSettingsWindowHeight,
                hint: sizeHint,
              ),
            ),
          ],
        ),
        if (value == instanceWindowMaximized) ...[
          const SizedBox(height: AppSpacing.xs),
          Text(
            l10n.settingsWindowSizeMaximized,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.textTertiary),
          ),
        ],
      ],
    );
  }

  Widget _numberField({
    required TextEditingController controller,
    required String label,
    required String hint,
  }) {
    return TextField(
      controller: controller,
      enabled: enabled,
      keyboardType: TextInputType.number,
      inputFormatters: [FilteringTextInputFormatter.digitsOnly],
      decoration: InputDecoration(labelText: label, hintText: hint),
    );
  }
}
