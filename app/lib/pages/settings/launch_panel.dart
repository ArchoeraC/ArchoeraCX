part of '../settings_page.dart';

/// Game window size, fullscreen, extra game arguments, wrapper command and
/// child environment.
///
/// These are C#-owned settings because the launch plan builder turns them into
/// `--width`/`--height` substitutions, `--fullscreen`, a wrapper process and
/// the game process environment.
class _LaunchSettingsPanel extends ConsumerStatefulWidget {
  const _LaunchSettingsPanel({this.settings});

  final AppSettings? settings;

  @override
  ConsumerState<_LaunchSettingsPanel> createState() =>
      _LaunchSettingsPanelState();
}

class _LaunchSettingsPanelState extends ConsumerState<_LaunchSettingsPanel> {
  final _game = TextEditingController();
  final _wrapper = TextEditingController();
  final _environment = TextEditingController();
  final _preLoad = TextEditingController();
  final _preLaunch = TextEditingController();
  final _postExit = TextEditingController();
  final _gameFocus = FocusNode();
  final _wrapperFocus = FocusNode();
  final _environmentFocus = FocusNode();
  final _preLoadFocus = FocusNode();
  final _preLaunchFocus = FocusNode();
  final _postExitFocus = FocusNode();
  Timer? _debounce;
  bool _saving = false;
  String? _error;
  String? _wrapperError;
  String? _environmentError;
  String? _preLoadError;
  String? _preLaunchError;
  String? _postExitError;

  @override
  void initState() {
    super.initState();
    _sync();
    _gameFocus.addListener(() {
      if (!_gameFocus.hasFocus) _saveNow();
    });
    _wrapperFocus.addListener(() {
      if (!_wrapperFocus.hasFocus) _saveNow();
    });
    _environmentFocus.addListener(() {
      if (!_environmentFocus.hasFocus) _saveNow();
    });
    for (final focus in [_preLoadFocus, _preLaunchFocus, _postExitFocus]) {
      focus.addListener(() {
        if (!focus.hasFocus) _saveNow();
      });
    }
  }

  @override
  void didUpdateWidget(covariant _LaunchSettingsPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.settings != widget.settings) _sync();
  }

  void _sync() {
    if (!_gameFocus.hasFocus) _game.text = widget.settings?.gameArgs ?? '';
    if (!_wrapperFocus.hasFocus) {
      _wrapper.text = widget.settings?.wrapperCommand ?? '';
    }
    if (!_environmentFocus.hasFocus) {
      _environment.text = formatLaunchEnvironment(
        widget.settings?.environment ?? const <String, String>{},
      );
    }
    if (!_preLoadFocus.hasFocus) {
      _preLoad.text = widget.settings?.preLoadCommand ?? '';
    }
    if (!_preLaunchFocus.hasFocus) {
      _preLaunch.text = widget.settings?.preLaunchCommand ?? '';
    }
    if (!_postExitFocus.hasFocus) {
      _postExit.text = widget.settings?.postExitCommand ?? '';
    }
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _game.dispose();
    _wrapper.dispose();
    _environment.dispose();
    _preLoad.dispose();
    _preLaunch.dispose();
    _postExit.dispose();
    _gameFocus.dispose();
    _wrapperFocus.dispose();
    _environmentFocus.dispose();
    _preLoadFocus.dispose();
    _preLaunchFocus.dispose();
    _postExitFocus.dispose();
    super.dispose();
  }

  void _scheduleSave() {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 500), _saveNow);
  }

  Future<void> _saveNow() async {
    if (!mounted) return;
    _debounce?.cancel();

    final l10n = AppLocalizations.of(context);
    final wrapper = _wrapper.text.trim();
    final wrapperError = isValidLaunchWrapperCommand(wrapper)
        ? null
        : l10n.launchWrapperCommandTooLong;
    final environment = parseLaunchEnvironment(_environment.text);
    final environmentError = environment.issue?.labelFor(l10n);
    final preLoad = _preLoad.text.trim();
    final preLaunch = _preLaunch.text.trim();
    final postExit = _postExit.text.trim();
    final preLoadError = validateLaunchCommand(preLoad).issue?.labelFor(l10n);
    final preLaunchError = validateLaunchCommand(preLaunch).issue
        ?.labelFor(l10n);
    final postExitError = validateLaunchCommand(postExit).issue?.labelFor(l10n);

    setState(() {
      _saving = true;
      _error = null;
      _wrapperError = wrapperError;
      _environmentError = environmentError;
      _preLoadError = preLoadError;
      _preLaunchError = preLaunchError;
      _postExitError = postExitError;
    });

    if (wrapperError != null ||
        environmentError != null ||
        preLoadError != null ||
        preLaunchError != null ||
        postExitError != null) {
      setState(() => _saving = false);
      return;
    }

    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(
            gameArgs: _game.text.trim(),
            wrapperCommand: wrapper.isEmpty ? null : wrapper,
            preLoadCommand: preLoad.isEmpty ? null : preLoad,
            preLaunchCommand: preLaunch.isEmpty ? null : preLaunch,
            postExitCommand: postExit.isEmpty ? null : postExit,
            environment: environment.values,
          );
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
    } finally {
      if (mounted) setState(() => _saving = false);
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
    final shareLoaderFiles = settings?.shareLoaderFiles ?? true;
    final gameOptimization = settings?.gameOptimization ?? true;
    final desktop = ref.watch(desktopPlatformProvider);
    final prefs = ref.watch(preferencesProvider);
    final prefsNotifier = ref.read(preferencesProvider.notifier);

    final behaviorGroup = _SettingsGroup(
      title: l10n.settingsSectionBehavior,
      note: l10n.settingsBehaviorNote,
      children: [
        _AppearanceTile(
          icon: AppIcons.window,
          title: l10n.settingsHideAfterLaunch,
          subtitle: l10n.settingsHideAfterLaunchDesc,
          trailing: Switch(
            key: const ValueKey('settings-hide-after-launch'),
            value: prefs.hideAfterLaunch,
            onChanged: prefsNotifier.setHideAfterLaunch,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.close,
          title: l10n.settingsQuitAfterGameExit,
          subtitle: l10n.settingsQuitAfterGameExitDesc,
          trailing: Switch(
            key: const ValueKey('settings-quit-after-game-exit'),
            value: prefs.quitAfterGameExit,
            onChanged: prefsNotifier.setQuitAfterGameExit,
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

    final commandsGroup = _SettingsGroup(
      title: l10n.settingsSectionCommands,
      note: l10n.settingsCommandsNote,
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
              _commandField(
                key: const ValueKey('settings-pre-load-command'),
                controller: _preLoad,
                focusNode: _preLoadFocus,
                label: l10n.settingsPreLoadCommand,
                hint: l10n.settingsCommandEmptyHint,
                helper: l10n.settingsPreLoadCommandDesc,
                error: _preLoadError,
              ),
              const SizedBox(height: AppSpacing.md),
              _commandField(
                key: const ValueKey('settings-pre-launch-command'),
                controller: _preLaunch,
                focusNode: _preLaunchFocus,
                label: l10n.settingsPreLaunchCommand,
                hint: l10n.settingsCommandEmptyHint,
                helper: l10n.settingsPreLaunchCommandDesc,
                error: _preLaunchError,
              ),
              const SizedBox(height: AppSpacing.md),
              _commandField(
                key: const ValueKey('settings-post-exit-command'),
                controller: _postExit,
                focusNode: _postExitFocus,
                label: l10n.settingsPostExitCommand,
                hint: l10n.settingsCommandEmptyHint,
                helper: l10n.settingsPostExitCommandDesc,
                error: _postExitError,
              ),
              const SizedBox(height: AppSpacing.sm),
              Text(
                l10n.settingsCommandsHelp,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
          ),
        ),
      ],
    );

    final processGroup = _SettingsGroup(
      title: l10n.settingsSectionProcess,
      note: l10n.settingsProcessNote,
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
                key: const ValueKey('settings-wrapper-command'),
                controller: _wrapper,
                focusNode: _wrapperFocus,
                onChanged: (_) => _scheduleSave(),
                onSubmitted: (_) => _saveNow(),
                decoration: InputDecoration(
                  labelText: l10n.settingsWrapperCommand,
                  hintText: l10n.settingsWrapperCommandHint,
                  helperText: l10n.settingsWrapperCommandHelp,
                  helperMaxLines: 2,
                  errorText: _wrapperError,
                  errorMaxLines: 2,
                ),
              ),
              const SizedBox(height: AppSpacing.md),
              TextField(
                key: const ValueKey('settings-environment'),
                controller: _environment,
                focusNode: _environmentFocus,
                keyboardType: TextInputType.multiline,
                minLines: 4,
                maxLines: 8,
                onChanged: (_) => _scheduleSave(),
                decoration: InputDecoration(
                  labelText: l10n.settingsEnvironment,
                  hintText: l10n.settingsEnvironmentHint,
                  helperText: l10n.settingsEnvironmentHelp,
                  helperMaxLines: 2,
                  errorText: _environmentError,
                  errorMaxLines: 3,
                  alignLabelWithHint: true,
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
        if (desktop) ...[behaviorGroup, const SizedBox(height: AppSpacing.lg)],
        argsGroup,
        const SizedBox(height: AppSpacing.lg),
        commandsGroup,
        const SizedBox(height: AppSpacing.lg),
        processGroup,
      ],
    );
  }

  Widget _commandField({
    required Key key,
    required TextEditingController controller,
    required FocusNode focusNode,
    required String label,
    required String hint,
    required String helper,
    required String? error,
  }) {
    return TextField(
      key: key,
      controller: controller,
      focusNode: focusNode,
      onChanged: (_) => _scheduleSave(),
      onSubmitted: (_) => _saveNow(),
      decoration: InputDecoration(
        labelText: label,
        hintText: hint,
        helperText: helper,
        helperMaxLines: 2,
        errorText: error,
        errorMaxLines: 2,
      ),
    );
  }
}
