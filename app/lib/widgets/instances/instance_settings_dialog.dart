import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../models/launch_options.dart';
import '../../services/core/launch/launch_memory.dart';
import '../../stores/instances.dart';
import '../../stores/platform.dart';
import '../../stores/providers.dart';
import '../../stores/settings.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../../utils/absolute_path.dart';
import '../../utils/window_mode.dart';
import '../common/confirm.dart';
import '../common/app_dropdown.dart';
import '../common/toast.dart';
import 'instance_command_overrides.dart';
import 'instance_java_runtime_picker.dart';
import 'instance_override_switch.dart';
import 'instance_performance_overrides.dart';
import 'instance_window_overrides.dart';

/// Per-instance overrides of the global launch settings.
///
/// Plain fields treat an empty input as "follow the global setting" and send
/// an explicit `null` in the merge patch. The wrapper, environment and
/// auto-join sections have their own switches because an enabled empty wrapper
/// or environment is meaningful (no wrapper, clear the environment).
Future<void> showInstanceSettingsDialog(
  BuildContext context,
  GameInstance instance,
) {
  return showDialog<void>(
    context: context,
    barrierDismissible: false,
    builder: (_) => InstanceSettingsDialog(instance: instance),
  );
}

class InstanceSettingsDialog extends ConsumerStatefulWidget {
  const InstanceSettingsDialog({super.key, required this.instance});

  final GameInstance instance;

  @override
  ConsumerState<InstanceSettingsDialog> createState() =>
      _InstanceSettingsDialogState();
}

class _InstanceSettingsDialogState
    extends ConsumerState<InstanceSettingsDialog> {
  static const _memoryMinMb = 128;
  static const _memoryMaxMb = 65536;
  static const _windowMinWidth = gameWindowMinWidth;
  static const _windowMaxWidth = gameWindowMaxWidth;
  static const _windowMinHeight = gameWindowMinHeight;
  static const _windowMaxHeight = gameWindowMaxHeight;

  final _minController = TextEditingController();
  final _maxController = TextEditingController();
  final _jvmController = TextEditingController();
  final _gameArgsController = TextEditingController();
  final _wrapperController = TextEditingController();
  final _environmentController = TextEditingController();
  late final InstanceCommandOverridesController _commands;
  final _joinServerController = TextEditingController();
  final _joinWorldController = TextEditingController();
  final _javaController = TextEditingController();
  final _widthController = TextEditingController();
  final _heightController = TextEditingController();
  late final InstancePerformanceOverridesController _performance;

  String? _gcMode;
  String _windowMode = instanceWindowInherit;
  bool _wrapperOverride = false;
  bool _environmentOverride = false;
  bool _autoJoin = false;
  String? _wrapperError;
  String? _environmentError;
  String? _serverError;
  String? _worldError;
  String? _error;
  bool _saving = false;
  LaunchMemoryPlan? _memoryPlan;

  @override
  void initState() {
    super.initState();
    _commands = InstanceCommandOverridesController(
      overrides: widget.instance.overrides,
    );
    final overrides = widget.instance.overrides;
    _performance = InstancePerformanceOverridesController(overrides: overrides);
    if (overrides != null) {
      _minController.text = overrides.memoryMinMb?.toString() ?? '';
      _maxController.text = overrides.memoryMaxMb?.toString() ?? '';
      _jvmController.text = overrides.jvmArgs ?? '';
      _gameArgsController.text = overrides.gameArgs ?? '';
      _wrapperOverride = overrides.wrapperCommand != null;
      _wrapperController.text = overrides.wrapperCommand ?? '';
      _environmentOverride = overrides.environment != null;
      _environmentController.text = formatLaunchEnvironment(
        overrides.environment ?? const <String, String>{},
      );
      _autoJoin = overrides.joinServerOnLaunch == true;
      _joinServerController.text = overrides.joinServerAddress ?? '';
      _joinWorldController.text = overrides.joinWorldName ?? '';
      _javaController.text = overrides.javaPath ?? '';
      _widthController.text = overrides.windowWidth?.toString() ?? '';
      _heightController.text = overrides.windowHeight?.toString() ?? '';
      _gcMode = overrides.gcMode;
      _windowMode = switch (instanceWindowModeFrom(
        fullscreen: overrides.fullscreen,
        maximized: overrides.maximized,
      )) {
        GameWindowMode.fullscreen => instanceWindowFullscreen,
        GameWindowMode.windowed => instanceWindowWindowed,
        GameWindowMode.maximized => instanceWindowMaximized,
        null => instanceWindowInherit,
      };
    }
    _loadMemoryPlan();
  }

  /// The recommendation is advisory: it tells the user what the launcher would
  /// pick so an override can be judged against it.
  Future<void> _loadMemoryPlan() async {
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    try {
      final plan = await client.getLaunchMemory(widget.instance.id);
      if (mounted) setState(() => _memoryPlan = plan);
    } catch (_) {
      // The dialog still works without the preview.
    }
  }

  @override
  void dispose() {
    _minController.dispose();
    _maxController.dispose();
    _jvmController.dispose();
    _gameArgsController.dispose();
    _wrapperController.dispose();
    _environmentController.dispose();
    _commands.dispose();
    _joinServerController.dispose();
    _joinWorldController.dispose();
    _javaController.dispose();
    _widthController.dispose();
    _heightController.dispose();
    super.dispose();
  }

  String? _text(TextEditingController controller) {
    final value = controller.text.trim();
    return value.isEmpty ? null : value;
  }

  bool? get _fullscreen => switch (_windowMode) {
    instanceWindowFullscreen => true,
    instanceWindowWindowed || instanceWindowMaximized => false,
    _ => null,
  };

  bool? get _maximized => switch (_windowMode) {
    instanceWindowMaximized => true,
    instanceWindowWindowed || instanceWindowFullscreen => false,
    _ => null,
  };

  /// Selecting maximized resolves the primary display once and stores the
  /// usable size in the width/height fields, because the game only accepts a
  /// window size. A failed lookup keeps whatever size is already typed.
  Future<void> _setWindowMode(String mode) async {
    setState(() => _windowMode = mode);
    if (mode != instanceWindowMaximized) return;
    final size = await ref.read(maximizedWindowSizeProvider)();
    if (!mounted || size == null || _windowMode != instanceWindowMaximized) {
      return;
    }
    setState(() {
      _widthController.text = '${size.width}';
      _heightController.text = '${size.height}';
    });
  }

  Future<void> _save(AppLocalizations l10n) async {
    if (_saving) return;
    final validation = _validate(l10n);
    if (validation != null) {
      _fail(validation);
      return;
    }
    final client = ref.read(coreClientProvider);
    if (client == null) {
      _fail(l10n.commonCoreNotConnected);
      return;
    }

    setState(() {
      _saving = true;
      _error = null;
    });

    try {
      await client.updateInstanceOverrides(
        instanceId: widget.instance.id,
        memoryMinMb: _parseNumber(_minController),
        memoryMaxMb: _parseNumber(_maxController),
        jvmArgs: _text(_jvmController),
        gcMode: _gcMode,
        gameArgs: _text(_gameArgsController),
        wrapperCommand: _wrapperOverride
            ? _wrapperController.text.trim()
            : null,
        preLoadCommand: _commands.preLoadCommand,
        preLaunchCommand: _commands.preLaunchCommand,
        postExitCommand: _commands.postExitCommand,
        environment: _environmentOverride
            ? parseLaunchEnvironment(_environmentController.text).values
            : null,
        joinServerOnLaunch: _autoJoin,
        joinServerAddress: _autoJoin ? _text(_joinServerController) : null,
        joinWorldName: _autoJoin ? _text(_joinWorldController) : null,
        javaPath: _text(_javaController),
        windowWidth: _parseNumber(_widthController),
        windowHeight: _parseNumber(_heightController),
        fullscreen: _fullscreen,
        maximized: _maximized,
        enableGameMode: _performance.enableGameMode,
        enableMangoHud: _performance.enableMangoHud,
        useZink: _performance.useZink,
      );
      await ref.read(instancesProvider.notifier).reload();
      if (!mounted) return;
      Navigator.of(context).pop();
      toast(l10n.instanceSettingsSaved, type: ToastType.success);
    } catch (error) {
      if (!mounted) return;
      _fail(l10n.instanceSettingsSaveFailed('$error'));
    }
  }

  /// Human label for a GC mode, used in the "follow global" hint.
  String _gcLabel(AppLocalizations l10n, String mode) => switch (mode) {
    'g1gc' => l10n.settingsGcG1,
    'zgc' => l10n.settingsGcZgc,
    'parallel' => l10n.settingsGcParallel,
    'serial' => l10n.settingsGcSerial,
    'shenandoah' => l10n.settingsGcShenandoah,
    'epsilon' => l10n.settingsGcEpsilon,
    _ => l10n.settingsGcAuto,
  };

  /// Clears every override so the instance follows the global settings again.
  Future<void> _resetToGlobal(AppLocalizations l10n) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      _fail(l10n.commonCoreNotConnected);
      return;
    }
    final confirmed = await confirmDestructive(
      context,
      l10n,
      title: l10n.instanceSettingsReset,
      body: l10n.instanceSettingsResetBody,
      confirmLabel: l10n.instanceSettingsReset,
    );
    if (confirmed != true || !mounted) return;

    setState(() => _saving = true);
    try {
      await client.updateInstanceOverrides(instanceId: widget.instance.id);
      await ref.read(instancesProvider.notifier).reload();
      if (!mounted) return;
      Navigator.of(context).pop();
      toast(l10n.instanceSettingsResetDone, type: ToastType.success);
    } catch (error) {
      if (!mounted) return;
      setState(() => _saving = false);
      _fail(l10n.instanceSettingsSaveFailed('$error'));
    }
  }

  /// Returns the first validation problem, or null when the form is valid.
  ///
  /// Field-level issues are also stored in the matching `*Error` members so
  /// they render directly under the offending input.
  String? _validate(AppLocalizations l10n) {
    String? error;
    _wrapperError = null;
    _environmentError = null;
    _serverError = null;
    _worldError = null;

    int? parse(TextEditingController controller, int min, int max) {
      final raw = controller.text.trim();
      if (raw.isEmpty) return null;
      final value = int.tryParse(raw);
      if (value == null) {
        error ??= l10n.instanceSettingsInvalidNumbers;
        return null;
      }
      if (value < min || value > max) {
        error ??= l10n.instanceSettingsInvalidRange;
        return null;
      }
      return value;
    }

    final minMb = parse(_minController, _memoryMinMb, _memoryMaxMb);
    final maxMb = parse(_maxController, _memoryMinMb, _memoryMaxMb);
    parse(_widthController, _windowMinWidth, _windowMaxWidth);
    parse(_heightController, _windowMinHeight, _windowMaxHeight);
    if (error != null) return error;

    if (minMb != null && maxMb != null && minMb > maxMb) {
      return l10n.instanceSettingsInvalidRange;
    }

    final java = _text(_javaController);
    if (java != null && !isAbsoluteFilePath(java)) {
      return l10n.instanceSettingsInvalidPath;
    }

    final gcMode = _gcMode;
    if (gcMode != null && !InstanceOverrides.gcModes.contains(gcMode)) {
      return l10n.instanceSettingsInvalidGc;
    }

    if (_wrapperOverride &&
        !isValidLaunchWrapperCommand(_wrapperController.text.trim())) {
      _wrapperError = l10n.instanceSettingsInvalidWrapper;
      error ??= _wrapperError;
    }

    if (_environmentOverride) {
      final environment = parseLaunchEnvironment(_environmentController.text);
      if (!environment.isValid) {
        _environmentError = environment.issue?.labelFor(l10n);
        error ??= _environmentError;
      }
    }

    error ??= _commands.validate(l10n);

    if (_autoJoin) {
      final serverInput = _joinServerController.text;
      final worldInput = _joinWorldController.text;
      final server = serverInput.trim();
      final world = worldInput.trim();
      if (server.isEmpty && world.isEmpty) {
        _serverError = l10n.instanceSettingsAutoJoinNeedsTarget;
        error ??= _serverError;
      } else if (server.isNotEmpty && world.isNotEmpty) {
        final conflict = l10n.instanceSettingsAutoJoinConflict;
        _serverError = conflict;
        _worldError = conflict;
        error ??= conflict;
      } else {
        if (server.isNotEmpty && !isValidJoinServerAddress(serverInput)) {
          _serverError = l10n.instanceSettingsInvalidServerAddress;
          error ??= _serverError;
        }
        if (world.isNotEmpty && !isValidJoinWorldName(worldInput)) {
          _worldError = l10n.instanceSettingsInvalidWorldName;
          error ??= _worldError;
        }
      }
    }
    return error;
  }

  static int? _parseNumber(TextEditingController controller) =>
      int.tryParse(controller.text.trim());

  void _fail(String message) {
    setState(() => _error = message);
    toast(message, type: ToastType.error);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final global = ref.watch(appSettingsProvider).value;
    final memoryHint = global == null
        ? l10n.instanceSettingsFollowGlobal
        : global.memoryAuto
        ? _memoryPlan == null
              ? l10n.settingsMemoryAuto
              : l10n.instanceSettingsMemoryAuto(_memoryPlan!.recommendedMb)
        : l10n.instanceSettingsFollowGlobalWith(
            '${global.memoryMinMb}–${global.memoryMaxMb} MB',
          );
    // A typed override far below the recommendation is worth flagging before
    // the game crashes on it; the value is still saved as-is.
    final typedMax = int.tryParse(_maxController.text.trim());
    final plan = _memoryPlan;
    final lowMemory =
        plan != null &&
        typedMax != null &&
        (typedMax < 1024 || typedMax * 5 < plan.recommendedMb * 4);
    final gcHint = global == null
        ? l10n.instanceSettingsFollowGlobal
        : l10n.instanceSettingsFollowGlobalWith(_gcLabel(l10n, global.gcMode));
    final windowHint = global == null
        ? l10n.instanceSettingsWindowInherit
        : l10n.instanceSettingsFollowGlobalWith(switch (gameWindowModeFrom(
            fullscreen: global.fullscreen,
            maximized: global.maximized,
          )) {
            GameWindowMode.fullscreen => l10n.instanceSettingsWindowFullscreen,
            GameWindowMode.maximized => l10n.instanceSettingsWindowMaximized,
            GameWindowMode.windowed =>
              '${global.windowWidth} × ${global.windowHeight}',
          });
    final linux = ref.watch(linuxPlatformProvider);
    final sizeHint = _windowMode == instanceWindowMaximized
        ? l10n.settingsWindowSizeMaximized
        : windowHint;
    final javaHint = switch (global?.javaPath) {
      null => l10n.instanceSettingsFollowGlobal,
      final path => l10n.instanceSettingsFollowGlobalWith(path),
    };
    final hasOverrides =
        widget.instance.overrides != null &&
        !widget.instance.overrides!.isEmpty;

    return Dialog(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxWidth: 620,
          maxHeight: MediaQuery.sizeOf(context).height * 0.82,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.lg,
                AppSpacing.lg,
                AppSpacing.md,
                AppSpacing.sm,
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          l10n.instanceSettingsTitle,
                          style: textTheme.titleLarge,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          '${widget.instance.name} · '
                          '${l10n.instanceSettingsDesc}',
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: textTheme.bodySmall?.copyWith(
                            color: tokens.textTertiary,
                          ),
                        ),
                      ],
                    ),
                  ),
                  IconButton(
                    onPressed: _saving
                        ? null
                        : () => Navigator.of(context).pop(),
                    tooltip: l10n.commonClose,
                    icon: const Icon(AppIcons.close),
                  ),
                ],
              ),
            ),
            Divider(height: 1, color: tokens.border),
            Flexible(
              child: SingleChildScrollView(
                padding: const EdgeInsets.fromLTRB(
                  AppSpacing.lg,
                  AppSpacing.md,
                  AppSpacing.lg,
                  AppSpacing.md,
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          child: _numberField(
                            controller: _minController,
                            label: l10n.instanceSettingsMemoryMin,
                            hint: memoryHint,
                          ),
                        ),
                        const SizedBox(width: AppSpacing.sm),
                        Expanded(
                          child: _numberField(
                            controller: _maxController,
                            label: l10n.instanceSettingsMemoryMax,
                            hint: memoryHint,
                            // Rebuild so the low-memory hint follows the input.
                            onChanged: (_) => setState(() {}),
                          ),
                        ),
                      ],
                    ),
                    if (lowMemory) ...[
                      const SizedBox(height: AppSpacing.xs),
                      Text(
                        l10n.instanceSettingsMemoryLow(
                          _memoryPlan!.recommendedMb,
                        ),
                        style: textTheme.bodySmall?.copyWith(
                          color: tokens.warning,
                        ),
                      ),
                    ],
                    const SizedBox(height: AppSpacing.md),
                    AppDropdownFormField<String>(
                      key: const ValueKey('instance-gc-mode'),
                      initialValue: _gcMode ?? '',
                      decoration: InputDecoration(
                        labelText: l10n.instanceSettingsGc,
                        prefixIcon: const Icon(AppIcons.settings, size: 18),
                      ),
                      items: [
                        DropdownMenuItem(value: '', child: Text(gcHint)),
                        for (final mode in InstanceOverrides.gcModes)
                          DropdownMenuItem(
                            value: mode,
                            child: Text(_gcLabel(l10n, mode)),
                          ),
                      ],
                      onChanged: _saving
                          ? null
                          : (value) => setState(
                              () => _gcMode = value == null || value.isEmpty
                                  ? null
                                  : value,
                            ),
                    ),
                    const SizedBox(height: AppSpacing.md),
                    TextField(
                      controller: _jvmController,
                      enabled: !_saving,
                      minLines: 2,
                      maxLines: 2,
                      decoration: InputDecoration(
                        labelText: l10n.instanceSettingsJvmArgs,
                        hintText: l10n.instanceSettingsFollowGlobal,
                        alignLabelWithHint: true,
                      ),
                    ),
                    const SizedBox(height: AppSpacing.md),
                    TextField(
                      controller: _gameArgsController,
                      enabled: !_saving,
                      minLines: 2,
                      maxLines: 2,
                      decoration: InputDecoration(
                        labelText: l10n.instanceSettingsGameArgs,
                        hintText: l10n.instanceSettingsFollowGlobal,
                        alignLabelWithHint: true,
                      ),
                    ),
                    InstanceCommandOverrides(
                      controller: _commands,
                      enabled: !_saving,
                      onChanged: () => setState(() {}),
                    ),
                    const SizedBox(height: AppSpacing.md),
                    InstanceOverrideSwitch(
                      key: const ValueKey('instance-wrapper-override'),
                      title: l10n.instanceSettingsWrapperOverride,
                      value: _wrapperOverride,
                      onChanged: (value) => setState(() {
                        _wrapperOverride = value;
                        _wrapperError = null;
                      }),
                    ),
                    if (_wrapperOverride) ...[
                      const SizedBox(height: AppSpacing.sm),
                      TextField(
                        controller: _wrapperController,
                        enabled: !_saving,
                        onChanged: (_) => setState(() => _wrapperError = null),
                        decoration: InputDecoration(
                          labelText: l10n.instanceSettingsWrapperCommand,
                          hintText: l10n.instanceSettingsWrapperCommandHint,
                          hintMaxLines: 2,
                          errorText: _wrapperError,
                          errorMaxLines: 2,
                        ),
                      ),
                    ],
                    const SizedBox(height: AppSpacing.sm),
                    InstanceOverrideSwitch(
                      key: const ValueKey('instance-environment-override'),
                      title: l10n.instanceSettingsEnvironmentOverride,
                      value: _environmentOverride,
                      onChanged: (value) => setState(() {
                        _environmentOverride = value;
                        _environmentError = null;
                      }),
                    ),
                    if (_environmentOverride) ...[
                      const SizedBox(height: AppSpacing.sm),
                      TextField(
                        controller: _environmentController,
                        enabled: !_saving,
                        keyboardType: TextInputType.multiline,
                        minLines: 3,
                        maxLines: 6,
                        onChanged: (_) =>
                            setState(() => _environmentError = null),
                        decoration: InputDecoration(
                          labelText: l10n.instanceSettingsEnvironment,
                          hintText: l10n.instanceSettingsEnvironmentHint,
                          helperText: l10n.instanceSettingsEnvironmentHelp,
                          helperMaxLines: 2,
                          errorText: _environmentError,
                          errorMaxLines: 3,
                          alignLabelWithHint: true,
                        ),
                      ),
                    ],
                    const SizedBox(height: AppSpacing.sm),
                    InstanceOverrideSwitch(
                      key: const ValueKey('instance-auto-join'),
                      title: l10n.instanceSettingsAutoJoin,
                      subtitle: l10n.instanceSettingsAutoJoinDesc,
                      value: _autoJoin,
                      onChanged: (value) => setState(() {
                        _autoJoin = value;
                        _serverError = null;
                        _worldError = null;
                      }),
                    ),
                    if (_autoJoin) ...[
                      const SizedBox(height: AppSpacing.sm),
                      TextField(
                        controller: _joinServerController,
                        enabled: !_saving,
                        onChanged: (_) => setState(() => _serverError = null),
                        decoration: InputDecoration(
                          labelText: l10n.instanceSettingsJoinServer,
                          hintText: l10n.instanceSettingsJoinServerHint,
                          errorText: _serverError,
                          errorMaxLines: 2,
                        ),
                      ),
                      const SizedBox(height: AppSpacing.sm),
                      TextField(
                        controller: _joinWorldController,
                        enabled: !_saving,
                        onChanged: (_) => setState(() => _worldError = null),
                        decoration: InputDecoration(
                          labelText: l10n.instanceSettingsJoinWorld,
                          hintText: l10n.instanceSettingsJoinWorldHint,
                          errorText: _worldError,
                          errorMaxLines: 2,
                        ),
                      ),
                    ],
                    const SizedBox(height: AppSpacing.md),
                    TextField(
                      controller: _javaController,
                      enabled: !_saving,
                      onChanged: (_) => setState(() {}),
                      decoration: InputDecoration(
                        labelText: l10n.instanceSettingsJava,
                        hintText: javaHint,
                        helperText: l10n.instanceSettingsJavaHint,
                        helperMaxLines: 2,
                        prefixIcon: const Icon(AppIcons.java, size: 18),
                        suffixIcon: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            if (_javaController.text.trim().isNotEmpty)
                              IconButton(
                                tooltip: l10n.instanceSettingsJavaFollowGlobal,
                                onPressed: _saving
                                    ? null
                                    : () => setState(_javaController.clear),
                                icon: const Icon(AppIcons.reset, size: 18),
                              ),
                            InstanceJavaRuntimePicker(
                              l10n: l10n,
                              selectedPath: _javaController.text.trim(),
                              enabled: !_saving,
                              onSelected: (path) => setState(() {
                                _javaController.text = path;
                                _error = null;
                              }),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: AppSpacing.md),
                    InstanceWindowOverrides(
                      value: _windowMode,
                      inheritLabel: windowHint,
                      sizeHint: sizeHint,
                      widthController: _widthController,
                      heightController: _heightController,
                      enabled: !_saving,
                      onChanged: _setWindowMode,
                    ),
                    if (linux) ...[
                      const SizedBox(height: AppSpacing.md),
                      InstancePerformanceOverrides(
                        controller: _performance,
                        enabled: !_saving,
                        onChanged: () => setState(() {}),
                      ),
                    ],
                    if (_error != null) ...[
                      const SizedBox(height: AppSpacing.md),
                      Text(
                        _error!,
                        style: textTheme.bodySmall?.copyWith(
                          color: tokens.danger,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
            Divider(height: 1, color: tokens.border),
            Padding(
              padding: const EdgeInsets.all(AppSpacing.md),
              child: Row(
                children: [
                  if (hasOverrides)
                    TextButton.icon(
                      onPressed: _saving ? null : () => _resetToGlobal(l10n),
                      icon: const Icon(AppIcons.reset, size: 18),
                      label: Text(l10n.instanceSettingsReset),
                    ),
                  const Spacer(),
                  TextButton(
                    onPressed: _saving
                        ? null
                        : () => Navigator.of(context).pop(),
                    child: Text(l10n.commonCancel),
                  ),
                  const SizedBox(width: AppSpacing.xs),
                  FilledButton.icon(
                    onPressed: _saving ? null : () => _save(l10n),
                    icon: _saving
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(AppIcons.check, size: 18),
                    label: Text(l10n.instanceSettingsSave),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _numberField({
    required TextEditingController controller,
    required String label,
    required String hint,
    ValueChanged<String>? onChanged,
  }) {
    return TextField(
      controller: controller,
      enabled: !_saving,
      keyboardType: TextInputType.number,
      inputFormatters: [FilteringTextInputFormatter.digitsOnly],
      onChanged: onChanged,
      decoration: InputDecoration(labelText: label, hintText: hint),
    );
  }
}
