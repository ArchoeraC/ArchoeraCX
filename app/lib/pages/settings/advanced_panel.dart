part of '../settings_page.dart';

/// Global defaults that are too detailed for the everyday launch page:
/// game window, Linux performance presets, Java/memory/GC and the separate
/// developer-only native library overrides.
///
/// Instances inherit these values unless they define an override of their own.
class _AdvancedSettingsPanel extends ConsumerStatefulWidget {
  const _AdvancedSettingsPanel({this.settings});

  final AppSettings? settings;

  @override
  ConsumerState<_AdvancedSettingsPanel> createState() =>
      _AdvancedSettingsPanelState();
}

class _AdvancedSettingsPanelState
    extends ConsumerState<_AdvancedSettingsPanel> {
  static const _presets = <(int, int)>[
    (854, 480),
    (1280, 720),
    (1600, 900),
    (1920, 1080),
  ];

  bool _windowBusy = false;

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

  /// Switching modes writes both mutually exclusive flags plus, for maximized,
  /// the primary display's usable size. A failed display lookup keeps the
  /// currently configured size instead of writing zeroes or crashing.
  Future<void> _setWindowMode(GameWindowMode mode) async {
    if (_windowBusy) return;
    setState(() => _windowBusy = true);
    final size = mode == GameWindowMode.maximized
        ? await ref.read(maximizedWindowSizeProvider)()
        : null;
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(
            fullscreen: mode == GameWindowMode.fullscreen,
            maximized: mode == GameWindowMode.maximized,
            windowWidth: size?.width,
            windowHeight: size?.height,
          );
    } catch (error) {
      if (mounted) toast('$error', type: ToastType.error);
    } finally {
      if (mounted) setState(() => _windowBusy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final settings = widget.settings;
    final width = settings?.windowWidth ?? 854;
    final height = settings?.windowHeight ?? 480;
    final mode = gameWindowModeFrom(
      fullscreen: settings?.fullscreen ?? false,
      maximized: settings?.maximized ?? false,
    );
    final windowed = mode == GameWindowMode.windowed;
    final current = '${width}x$height';
    final knownPreset = _presets.any((p) => '${p.$1}x${p.$2}' == current);
    final linux = ref.watch(linuxPlatformProvider);

    final windowGroup = _SettingsGroup(
      title: l10n.settingsSectionWindow,
      note: l10n.settingsWindowNote,
      children: [
        _AppearanceTile(
          icon: AppIcons.window,
          title: l10n.settingsWindowMode,
          subtitle: l10n.settingsWindowModeDesc,
          trailing: AppDropdown<GameWindowMode>(
            key: const ValueKey('settings-window-mode'),
            value: mode,
            items: [
              DropdownMenuItem(
                value: GameWindowMode.windowed,
                child: Text(l10n.settingsWindowModeWindowed),
              ),
              DropdownMenuItem(
                value: GameWindowMode.fullscreen,
                child: Text(l10n.settingsWindowModeFullscreen),
              ),
              DropdownMenuItem(
                value: GameWindowMode.maximized,
                child: Text(l10n.settingsWindowModeMaximized),
              ),
            ],
            onChanged: (value) {
              if (value == null || _windowBusy) return;
              _setWindowMode(value);
            },
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.density,
          title: l10n.settingsWindowSize,
          subtitle: switch (mode) {
            GameWindowMode.fullscreen => l10n.settingsWindowSizeFullscreen,
            GameWindowMode.maximized => l10n.settingsWindowSizeMaximized,
            GameWindowMode.windowed => l10n.settingsWindowSizeDesc,
          },
          trailing: AppDropdown<String>(
            value: current,
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
            onChanged: windowed
                ? (value) {
                    if (value == null) return;
                    final parts = value.split('x');
                    final w = int.tryParse(parts.first);
                    final h = int.tryParse(parts.last);
                    if (w == null || h == null) return;
                    _applyWindow(w, h);
                  }
                : null,
          ),
        ),
      ],
    );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          l10n.settingsAdvancedNote,
          style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(height: AppSpacing.md),
        windowGroup,
        if (linux) ...[
          const SizedBox(height: AppSpacing.lg),
          _PerformanceSettingsPanel(settings: settings),
        ],
        const SizedBox(height: AppSpacing.lg),
        Text(
          l10n.settingsSectionJavaMemory,
          style: textTheme.labelSmall?.copyWith(
            color: tokens.textSecondary,
            letterSpacing: 1.2,
            fontWeight: FontWeight.w600,
          ),
        ),
        const SizedBox(height: AppSpacing.xs),
        _SettingsCard(
          children: [
            _SettingRow(
              icon: AppIcons.java,
              title: l10n.javaPageManage,
              subtitle: l10n.javaPageManageHint,
              onTap: () => context.go('/settings/java'),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.xs),
        _RuntimeSettingsPanel(settings: settings),
        if (linux) ...[
          const SizedBox(height: AppSpacing.lg),
          _NativeLibrariesPanel(settings: settings),
        ],
      ],
    );
  }
}
