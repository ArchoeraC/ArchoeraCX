part of '../settings_page.dart';

/// Game window size, fullscreen and extra game arguments.
///
/// These are C#-owned settings because the launch plan builder turns them into
/// `--width`/`--height` substitutions and `--fullscreen`.
class _LaunchSettingsPanel extends ConsumerStatefulWidget {
  const _LaunchSettingsPanel({this.settings});

  final AppSettings? settings;

  @override
  ConsumerState<_LaunchSettingsPanel> createState() =>
      _LaunchSettingsPanelState();
}

class _LaunchSettingsPanelState extends ConsumerState<_LaunchSettingsPanel> {
  static const _presets = <(int, int)>[
    (854, 480),
    (1280, 720),
    (1600, 900),
    (1920, 1080),
  ];

  final _game = TextEditingController();
  final _gameFocus = FocusNode();
  Timer? _debounce;
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _sync();
    _gameFocus.addListener(() {
      if (!_gameFocus.hasFocus) _saveNow();
    });
  }

  @override
  void didUpdateWidget(covariant _LaunchSettingsPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.settings != widget.settings) _sync();
  }

  void _sync() {
    if (!_gameFocus.hasFocus) _game.text = widget.settings?.gameArgs ?? '';
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _game.dispose();
    _gameFocus.dispose();
    super.dispose();
  }

  void _scheduleSave() {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 500), _saveNow);
  }

  Future<void> _saveNow() async {
    if (!mounted) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(gameArgs: _game.text.trim());
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _applyWindow(int width, int height) async {
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(windowWidth: width, windowHeight: height);
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  Future<void> _setFullscreen(bool enabled) async {
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(fullscreen: enabled);
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  Future<void> _setShareLoaderFiles(bool enabled) async {
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(shareLoaderFiles: enabled);
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  Future<void> _setGameOptimization(bool enabled) async {
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(gameOptimization: enabled);
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final settings = widget.settings;
    final width = settings?.windowWidth ?? 854;
    final height = settings?.windowHeight ?? 480;
    final fullscreen = settings?.fullscreen ?? false;
    final shareLoaderFiles = settings?.shareLoaderFiles ?? true;
    final gameOptimization = settings?.gameOptimization ?? true;
    final current = '${width}x$height';
    final knownPreset = _presets.any((p) => '${p.$1}x${p.$2}' == current);

    final windowGroup = _SettingsGroup(
      title: l10n.settingsSectionWindow,
      note: l10n.settingsWindowNote,
      children: [
        _AppearanceTile(
          icon: AppIcons.window,
          title: l10n.settingsFullscreen,
          subtitle: l10n.settingsFullscreenDesc,
          trailing: Switch(value: fullscreen, onChanged: _setFullscreen),
        ),
        _AppearanceTile(
          icon: AppIcons.density,
          title: l10n.settingsWindowSize,
          subtitle: fullscreen
              ? l10n.settingsWindowSizeFullscreen
              : l10n.settingsWindowSizeDesc,
          trailing: DropdownButton<String>(
            value: current,
            isDense: true,
            underline: const SizedBox.shrink(),
            borderRadius: BorderRadius.circular(tokens.radiusM),
            items: [
              for (final preset in _presets)
                DropdownMenuItem(
                  value: '${preset.$1}x${preset.$2}',
                  child: Text('${preset.$1} × ${preset.$2}'),
                ),
              if (!knownPreset)
                DropdownMenuItem(
                  value: current,
                  child: Text('$width × $height'),
                ),
            ],
            onChanged: (value) {
              if (value == null) return;
              final parts = value.split('x');
              final w = int.tryParse(parts.first);
              final h = int.tryParse(parts.last);
              if (w == null || h == null) return;
              _applyWindow(w, h);
            },
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.modpack,
          title: l10n.settingsShareLoaderFiles,
          subtitle: l10n.settingsShareLoaderFilesDesc,
          trailing: Switch(
            key: const ValueKey('settings-share-loader-files'),
            value: shareLoaderFiles,
            onChanged: _setShareLoaderFiles,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.running,
          title: l10n.settingsGameOptimization,
          subtitle: l10n.settingsGameOptimizationDesc,
          trailing: Switch(
            key: const ValueKey('settings-game-optimization'),
            value: gameOptimization,
            onChanged: _setGameOptimization,
          ),
        ),
      ],
    );

    final argsGroup = _SettingsGroup(
      title: l10n.settingsSectionGameArgs,
      note: l10n.settingsGameArgsNote,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.sm,
            AppSpacing.xs,
            AppSpacing.sm,
            AppSpacing.sm,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              TextField(
                controller: _game,
                focusNode: _gameFocus,
                onChanged: (_) => _scheduleSave(),
                onSubmitted: (_) => _saveNow(),
                decoration: InputDecoration(
                  labelText: l10n.settingsGameArgs,
                  hintText: l10n.settingsGameArgsHint,
                ),
              ),
              if (_error != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  _error!,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.danger),
                ),
              ],
              if (_saving)
                const Padding(
                  padding: EdgeInsets.only(top: AppSpacing.xs),
                  child: Align(
                    alignment: Alignment.centerRight,
                    child: SizedBox(
                      width: 14,
                      height: 14,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ],
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        windowGroup,
        const SizedBox(height: AppSpacing.lg),
        argsGroup,
      ],
    );
  }
}
