import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/game_instance.dart';
import '../../services/core/launch_memory.dart';
import '../../stores/instances.dart';
import '../../stores/providers.dart';
import '../../stores/settings.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/toast.dart';

/// Per-instance overrides of the global launch settings.
///
/// Every field is optional: an empty input means "follow the global setting"
/// and is sent as an explicit `null` in the merge patch.
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
  static const _windowInherit = 'inherit';
  static const _windowFullscreen = 'fullscreen';
  static const _windowWindowed = 'windowed';

  static const _memoryMinMb = 128;
  static const _memoryMaxMb = 65536;
  static const _windowMinWidth = 640;
  static const _windowMaxWidth = 7680;
  static const _windowMinHeight = 480;
  static const _windowMaxHeight = 4320;

  final _minController = TextEditingController();
  final _maxController = TextEditingController();
  final _jvmController = TextEditingController();
  final _gameArgsController = TextEditingController();
  final _javaController = TextEditingController();
  final _widthController = TextEditingController();
  final _heightController = TextEditingController();

  String? _gcMode;
  String _windowMode = _windowInherit;
  String? _error;
  bool _saving = false;
  LaunchMemoryPlan? _memoryPlan;

  @override
  void initState() {
    super.initState();
    final overrides = widget.instance.overrides;
    if (overrides != null) {
      _minController.text = overrides.memoryMinMb?.toString() ?? '';
      _maxController.text = overrides.memoryMaxMb?.toString() ?? '';
      _jvmController.text = overrides.jvmArgs ?? '';
      _gameArgsController.text = overrides.gameArgs ?? '';
      _javaController.text = overrides.javaPath ?? '';
      _widthController.text = overrides.windowWidth?.toString() ?? '';
      _heightController.text = overrides.windowHeight?.toString() ?? '';
      _gcMode = overrides.gcMode;
      _windowMode = switch (overrides.fullscreen) {
        true => _windowFullscreen,
        false => _windowWindowed,
        null => _windowInherit,
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
    _windowFullscreen => true,
    _windowWindowed => false,
    _ => null,
  };

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
        javaPath: _text(_javaController),
        windowWidth: _parseNumber(_widthController),
        windowHeight: _parseNumber(_heightController),
        fullscreen: _fullscreen,
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
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(l10n.instanceSettingsReset),
        content: Text(l10n.instanceSettingsResetBody),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(l10n.commonCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(l10n.instanceSettingsReset),
          ),
        ],
      ),
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
  String? _validate(AppLocalizations l10n) {
    String? error;
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
    if (java != null && !_isAbsolutePath(java)) {
      return l10n.instanceSettingsInvalidPath;
    }

    final gcMode = _gcMode;
    if (gcMode != null && !InstanceOverrides.gcModes.contains(gcMode)) {
      return l10n.instanceSettingsInvalidGc;
    }
    return null;
  }

  static bool _isAbsolutePath(String path) {
    if (path.startsWith('/')) return true;
    if (path.startsWith(r'\\')) return true;
    return RegExp(r'^[A-Za-z]:[\\/]').hasMatch(path);
  }

  static int? _parseNumber(TextEditingController controller) =>
      int.tryParse(controller.text.trim());

  void _fail(String message) {
    setState(() => _error = message);
    toast(message, type: ToastType.error);
  }

  List<(String, String)> _gcOptions(AppLocalizations l10n) => [
    ('auto', l10n.settingsGcAuto),
    ('g1gc', l10n.settingsGcG1),
    ('zgc', l10n.settingsGcZgc),
    ('parallel', l10n.settingsGcParallel),
    ('serial', l10n.settingsGcSerial),
    ('shenandoah', l10n.settingsGcShenandoah),
    ('epsilon', l10n.settingsGcEpsilon),
  ];

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
        (typedMax < 1024 || typedMax * 10 < plan.recommendedMb * 6);
    final gcHint = global == null
        ? l10n.instanceSettingsFollowGlobal
        : l10n.instanceSettingsFollowGlobalWith(_gcLabel(l10n, global.gcMode));
    final windowHint = global == null
        ? l10n.instanceSettingsWindowInherit
        : l10n.instanceSettingsFollowGlobalWith(
            global.fullscreen
                ? l10n.instanceSettingsWindowFullscreen
                : '${global.windowWidth} × ${global.windowHeight}',
          );
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
                    DropdownButtonFormField<String>(
                      key: const ValueKey('instance-gc-mode'),
                      initialValue: _gcMode ?? '',
                      decoration: InputDecoration(
                        labelText: l10n.instanceSettingsGc,
                        prefixIcon: const Icon(AppIcons.settings, size: 18),
                      ),
                      items: [
                        DropdownMenuItem(value: '', child: Text(gcHint)),
                        for (final option in _gcOptions(l10n))
                          DropdownMenuItem(
                            value: option.$1,
                            child: Text(option.$2),
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
                    const SizedBox(height: AppSpacing.md),
                    TextField(
                      controller: _javaController,
                      enabled: !_saving,
                      decoration: InputDecoration(
                        labelText: l10n.instanceSettingsJava,
                        hintText: javaHint,
                        helperText: l10n.instanceSettingsJavaHint,
                        helperMaxLines: 2,
                        prefixIcon: const Icon(AppIcons.java, size: 18),
                      ),
                    ),
                    const SizedBox(height: AppSpacing.md),
                    DropdownButtonFormField<String>(
                      key: const ValueKey('instance-window-mode'),
                      initialValue: _windowMode,
                      decoration: InputDecoration(
                        labelText: l10n.instanceSettingsWindow,
                        prefixIcon: const Icon(AppIcons.window, size: 18),
                      ),
                      items: [
                        DropdownMenuItem(
                          value: _windowInherit,
                          child: Text(windowHint),
                        ),
                        DropdownMenuItem(
                          value: _windowFullscreen,
                          child: Text(l10n.instanceSettingsWindowFullscreen),
                        ),
                        DropdownMenuItem(
                          value: _windowWindowed,
                          child: Text(l10n.instanceSettingsWindowWindowed),
                        ),
                      ],
                      onChanged: _saving
                          ? null
                          : (value) => setState(
                              () => _windowMode = value ?? _windowInherit,
                            ),
                    ),
                    const SizedBox(height: AppSpacing.sm),
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          child: _numberField(
                            controller: _widthController,
                            label: l10n.instanceSettingsWindowWidth,
                            hint: windowHint,
                          ),
                        ),
                        const SizedBox(width: AppSpacing.sm),
                        Expanded(
                          child: _numberField(
                            controller: _heightController,
                            label: l10n.instanceSettingsWindowHeight,
                            hint: windowHint,
                          ),
                        ),
                      ],
                    ),
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
