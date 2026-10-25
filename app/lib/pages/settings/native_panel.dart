part of '../settings_page.dart';

enum _NativeLibraryField { glfw, openAL, sdl }

/// Linux-only developer overrides for the GLFW/OpenAL/SDL libraries loaded by
/// the local game process.
///
/// The paths are normal settings (not secrets), but they are only applied while
/// the developer switch is on. Turning the switch off keeps the stored values
/// parked instead of deleting them.
class _NativeLibrariesPanel extends ConsumerStatefulWidget {
  const _NativeLibrariesPanel({this.settings});

  final AppSettings? settings;

  @override
  ConsumerState<_NativeLibrariesPanel> createState() =>
      _NativeLibrariesPanelState();
}

class _NativeLibrariesPanelState extends ConsumerState<_NativeLibrariesPanel> {
  final _glfw = TextEditingController();
  final _openAL = TextEditingController();
  final _sdl = TextEditingController();
  final _glfwFocus = FocusNode();
  final _openALFocus = FocusNode();
  final _sdlFocus = FocusNode();
  Timer? _debounce;
  _NativeLibraryField? _pending;
  String? _glfwError;
  String? _openALError;
  String? _sdlError;

  @override
  void initState() {
    super.initState();
    _sync();
    for (final entry in <(_NativeLibraryField, FocusNode)>[
      (_NativeLibraryField.glfw, _glfwFocus),
      (_NativeLibraryField.openAL, _openALFocus),
      (_NativeLibraryField.sdl, _sdlFocus),
    ]) {
      entry.$2.addListener(() {
        if (!entry.$2.hasFocus) _saveField(entry.$1);
      });
    }
  }

  @override
  void didUpdateWidget(covariant _NativeLibrariesPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.settings != widget.settings) _sync();
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _glfw.dispose();
    _openAL.dispose();
    _sdl.dispose();
    _glfwFocus.dispose();
    _openALFocus.dispose();
    _sdlFocus.dispose();
    super.dispose();
  }

  void _sync() {
    final settings = widget.settings;
    if (!_glfwFocus.hasFocus) _glfw.text = settings?.nativeGlfwPath ?? '';
    if (!_openALFocus.hasFocus) {
      _openAL.text = settings?.nativeOpenALPath ?? '';
    }
    if (!_sdlFocus.hasFocus) _sdl.text = settings?.nativeSdlPath ?? '';
  }

  TextEditingController _controller(_NativeLibraryField field) =>
      switch (field) {
        _NativeLibraryField.glfw => _glfw,
        _NativeLibraryField.openAL => _openAL,
        _NativeLibraryField.sdl => _sdl,
      };

  String? _errorFor(_NativeLibraryField field) => switch (field) {
    _NativeLibraryField.glfw => _glfwError,
    _NativeLibraryField.openAL => _openALError,
    _NativeLibraryField.sdl => _sdlError,
  };

  void _setError(_NativeLibraryField field, String? error) {
    setState(() {
      switch (field) {
        case _NativeLibraryField.glfw:
          _glfwError = error;
        case _NativeLibraryField.openAL:
          _openALError = error;
        case _NativeLibraryField.sdl:
          _sdlError = error;
      }
    });
  }

  void _schedule(_NativeLibraryField field) {
    _pending = field;
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 500), _savePending);
  }

  void _savePending() {
    final field = _pending;
    _pending = null;
    if (field != null) _saveField(field);
  }

  /// Persists one path. Empty clears it; a relative/invalid value stays local
  /// with an inline error and is never sent to the native owner.
  Future<void> _saveField(_NativeLibraryField field) async {
    if (!mounted) return;
    _debounce?.cancel();
    _pending = null;
    final l10n = AppLocalizations.of(context);
    final text = _controller(field).text.trim();
    if (text.isNotEmpty && !isAbsoluteFilePath(text)) {
      _setError(field, l10n.settingsNativePathInvalid);
      return;
    }
    _setError(field, null);
    final value = text.isEmpty ? null : text;
    try {
      final notifier = ref.read(appSettingsProvider.notifier);
      switch (field) {
        case _NativeLibraryField.glfw:
          await notifier.setRuntimeOptions(nativeGlfwPath: value);
        case _NativeLibraryField.openAL:
          await notifier.setRuntimeOptions(nativeOpenALPath: value);
        case _NativeLibraryField.sdl:
          await notifier.setRuntimeOptions(nativeSdlPath: value);
      }
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  Future<void> _pickFile(_NativeLibraryField field) async {
    final l10n = AppLocalizations.of(context);
    try {
      final file = await openFile(
        acceptedTypeGroups: <XTypeGroup>[
          XTypeGroup(label: l10n.settingsNativePickerLabel),
        ],
      );
      if (file == null || !mounted) return;
      _controller(field).text = file.path;
      _setError(field, null);
      await _saveField(field);
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  Future<void> _setDeveloperMode(bool enabled) async {
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(developerMode: enabled);
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final developerMode = widget.settings?.developerMode ?? false;

    return _SettingsGroup(
      title: l10n.settingsSectionAdvancedNative,
      children: [
        _AppearanceTile(
          icon: AppIcons.security,
          title: l10n.settingsDeveloperMode,
          subtitle: l10n.settingsDeveloperModeDesc,
          trailing: Switch(
            key: const ValueKey('settings-developer-mode'),
            value: developerMode,
            onChanged: _setDeveloperMode,
          ),
        ),
        if (developerMode)
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
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Icon(AppIcons.warning, size: 16, color: tokens.warning),
                    const SizedBox(width: AppSpacing.xs),
                    Expanded(
                      child: Text(
                        l10n.settingsNativeWarning,
                        style: textTheme.bodySmall?.copyWith(
                          color: tokens.warning,
                        ),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: AppSpacing.md),
                _pathField(
                  key: const ValueKey('settings-native-glfw'),
                  l10n: l10n,
                  field: _NativeLibraryField.glfw,
                  controller: _glfw,
                  focusNode: _glfwFocus,
                  label: l10n.settingsNativeGlfw,
                ),
                const SizedBox(height: AppSpacing.md),
                _pathField(
                  key: const ValueKey('settings-native-openal'),
                  l10n: l10n,
                  field: _NativeLibraryField.openAL,
                  controller: _openAL,
                  focusNode: _openALFocus,
                  label: l10n.settingsNativeOpenAL,
                ),
                const SizedBox(height: AppSpacing.md),
                _pathField(
                  key: const ValueKey('settings-native-sdl'),
                  l10n: l10n,
                  field: _NativeLibraryField.sdl,
                  controller: _sdl,
                  focusNode: _sdlFocus,
                  label: l10n.settingsNativeSdl,
                ),
              ],
            ),
          ),
      ],
    );
  }

  Widget _pathField({
    required Key key,
    required AppLocalizations l10n,
    required _NativeLibraryField field,
    required TextEditingController controller,
    required FocusNode focusNode,
    required String label,
  }) {
    return TextField(
      key: key,
      controller: controller,
      focusNode: focusNode,
      onChanged: (_) => _schedule(field),
      onSubmitted: (_) => _saveField(field),
      decoration: InputDecoration(
        labelText: label,
        hintText: l10n.settingsNativePathHint,
        hintMaxLines: 1,
        errorText: _errorFor(field),
        errorMaxLines: 2,
        suffixIcon: IconButton(
          tooltip: l10n.settingsNativePickFile,
          onPressed: () => _pickFile(field),
          icon: const Icon(AppIcons.folderOpen, size: 18),
        ),
      ),
    );
  }
}
