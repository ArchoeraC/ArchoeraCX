import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../models/launch_options.dart';
import '../../theme/tokens.dart';
import 'instance_override_switch.dart';

/// Controllers and validation state for the three per-instance lifecycle
/// command overrides.
///
/// A disabled switch means "inherit the global command" and sends `null`; an
/// enabled switch sends the trimmed field text even when it is empty, which
/// explicitly disables the global command.
class InstanceCommandOverridesController {
  InstanceCommandOverridesController({InstanceOverrides? overrides})
    : _preLoad = TextEditingController(text: overrides?.preLoadCommand ?? ''),
      _preLaunch = TextEditingController(
        text: overrides?.preLaunchCommand ?? '',
      ),
      _postExit = TextEditingController(text: overrides?.postExitCommand ?? ''),
      _preLoadEnabled = overrides?.preLoadCommand != null,
      _preLaunchEnabled = overrides?.preLaunchCommand != null,
      _postExitEnabled = overrides?.postExitCommand != null;

  final TextEditingController _preLoad;
  final TextEditingController _preLaunch;
  final TextEditingController _postExit;

  bool _preLoadEnabled;
  bool _preLaunchEnabled;
  bool _postExitEnabled;

  String? _preLoadError;
  String? _preLaunchError;
  String? _postExitError;

  bool get preLoadEnabled => _preLoadEnabled;
  set preLoadEnabled(bool value) {
    _preLoadEnabled = value;
    _preLoadError = null;
  }

  bool get preLaunchEnabled => _preLaunchEnabled;
  set preLaunchEnabled(bool value) {
    _preLaunchEnabled = value;
    _preLaunchError = null;
  }

  bool get postExitEnabled => _postExitEnabled;
  set postExitEnabled(bool value) {
    _postExitEnabled = value;
    _postExitError = null;
  }

  String? get preLoadError => _preLoadError;
  String? get preLaunchError => _preLaunchError;
  String? get postExitError => _postExitError;

  /// Wire value for `instance.update`: null inherits, `''` runs nothing.
  String? get preLoadCommand => _preLoadEnabled ? _preLoad.text.trim() : null;
  String? get preLaunchCommand =>
      _preLaunchEnabled ? _preLaunch.text.trim() : null;
  String? get postExitCommand =>
      _postExitEnabled ? _postExit.text.trim() : null;

  void clearPreLoadError() => _preLoadError = null;
  void clearPreLaunchError() => _preLaunchError = null;
  void clearPostExitError() => _postExitError = null;

  /// Validates the enabled commands, stores field errors and returns the first
  /// message (or null). Disabled commands are not inspected.
  String? validate(AppLocalizations l10n) {
    String? check(
      bool enabled,
      TextEditingController controller,
      void Function(String?) store,
    ) {
      if (!enabled) {
        store(null);
        return null;
      }
      final message = validateLaunchCommand(controller.text.trim()).issue
          ?.labelFor(l10n);
      store(message);
      return message;
    }

    final preLoad = check(_preLoadEnabled, _preLoad, (value) {
      _preLoadError = value;
    });
    final preLaunch = check(_preLaunchEnabled, _preLaunch, (value) {
      _preLaunchError = value;
    });
    final postExit = check(_postExitEnabled, _postExit, (value) {
      _postExitError = value;
    });
    return preLoad ?? preLaunch ?? postExit;
  }

  void dispose() {
    _preLoad.dispose();
    _preLaunch.dispose();
    _postExit.dispose();
  }
}

/// The three lifecycle command overrides, shown after the game arguments.
class InstanceCommandOverrides extends StatelessWidget {
  const InstanceCommandOverrides({
    super.key,
    required this.controller,
    required this.enabled,
    required this.onChanged,
  });

  final InstanceCommandOverridesController controller;

  /// False while the dialog is saving.
  final bool enabled;

  /// Called after the controller changed so the dialog can rebuild.
  final VoidCallback onChanged;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SizedBox(height: AppSpacing.md),
        InstanceOverrideSwitch(
          key: const ValueKey('instance-pre-load-command-override'),
          title: l10n.instanceSettingsPreLoadOverride,
          value: controller.preLoadEnabled,
          onChanged: enabled
              ? (value) {
                  controller.preLoadEnabled = value;
                  onChanged();
                }
              : null,
        ),
        if (controller.preLoadEnabled)
          _field(
            context,
            key: const ValueKey('instance-pre-load-command'),
            l10n: l10n,
            textController: controller._preLoad,
            label: l10n.instanceSettingsPreLoadCommand,
            error: controller.preLoadError,
            onChanged: () {
              controller.clearPreLoadError();
              onChanged();
            },
          ),
        const SizedBox(height: AppSpacing.sm),
        InstanceOverrideSwitch(
          key: const ValueKey('instance-pre-launch-command-override'),
          title: l10n.instanceSettingsPreLaunchOverride,
          value: controller.preLaunchEnabled,
          onChanged: enabled
              ? (value) {
                  controller.preLaunchEnabled = value;
                  onChanged();
                }
              : null,
        ),
        if (controller.preLaunchEnabled)
          _field(
            context,
            key: const ValueKey('instance-pre-launch-command'),
            l10n: l10n,
            textController: controller._preLaunch,
            label: l10n.instanceSettingsPreLaunchCommand,
            error: controller.preLaunchError,
            onChanged: () {
              controller.clearPreLaunchError();
              onChanged();
            },
          ),
        const SizedBox(height: AppSpacing.sm),
        InstanceOverrideSwitch(
          key: const ValueKey('instance-post-exit-command-override'),
          title: l10n.instanceSettingsPostExitOverride,
          value: controller.postExitEnabled,
          onChanged: enabled
              ? (value) {
                  controller.postExitEnabled = value;
                  onChanged();
                }
              : null,
        ),
        if (controller.postExitEnabled)
          _field(
            context,
            key: const ValueKey('instance-post-exit-command'),
            l10n: l10n,
            textController: controller._postExit,
            label: l10n.instanceSettingsPostExitCommand,
            error: controller.postExitError,
            onChanged: () {
              controller.clearPostExitError();
              onChanged();
            },
          ),
        if (controller.preLoadEnabled ||
            controller.preLaunchEnabled ||
            controller.postExitEnabled)
          const SizedBox(height: AppSpacing.sm),
        Text(
          l10n.instanceSettingsCommandsHelp,
          style: Theme.of(context).textTheme.bodySmall
              ?.copyWith(color: tokens.textTertiary),
        ),
      ],
    );
  }

  Widget _field(
    BuildContext context, {
    required Key key,
    required AppLocalizations l10n,
    required TextEditingController textController,
    required String label,
    required String? error,
    required VoidCallback onChanged,
  }) {
    return Padding(
      padding: const EdgeInsets.only(top: AppSpacing.sm),
      child: TextField(
        key: key,
        controller: textController,
        enabled: enabled,
        onChanged: (_) => onChanged(),
        decoration: InputDecoration(
          labelText: label,
          hintText: l10n.instanceSettingsCommandEmptyHint,
          errorText: error,
          errorMaxLines: 2,
        ),
      ),
    );
  }
}
