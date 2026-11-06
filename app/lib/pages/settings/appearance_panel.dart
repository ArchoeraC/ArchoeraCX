part of '../settings_page.dart';

/// Appearance section content and its accent/language dialogs.
extension _AppearancePanelX on _SettingsPageState {
  Widget _appearanceContent(
    BuildContext context,
    AppLocalizations l10n,
    AppearanceSettings appearance,
    Color? systemAccent,
    Locale? locale,
  ) {
    final notifier = ref.read(appearanceProvider.notifier);
    final prefs = ref.watch(preferencesProvider);
    final customized = appearance.accentMode == AccentMode.custom;
    // A selected image forces dark colors and a global tint; the stored theme
    // mode is preserved for when the image is removed.
    final imageMode = appearance.hasImageBackground;

    final themeGroup = _SettingsGroup(
      title: l10n.settingsSectionTheme,
      children: [
        _AppearanceTile(
          icon: AppIcons.globalTint,
          title: l10n.settingsThemeMode,
          subtitle: imageMode
              ? l10n.settingsThemeModeImageForced
              : l10n.settingsThemeModeDesc,
          enabled: !imageMode,
          trailing: _SegmentedControl<ThemeMode>(
            selected: appearance.themeMode,
            options: [
              _SegmentedOption(ThemeMode.light, l10n.settingsThemeLight),
              _SegmentedOption(ThemeMode.dark, l10n.settingsThemeDark),
              _SegmentedOption(ThemeMode.system, l10n.settingsThemeSystem),
            ],
            onChanged: notifier.setThemeMode,
          ),
        ),
      ],
    );
    final accentGroup = _SettingsGroup(
      title: l10n.settingsSectionAccent,
      children: [
        _AppearanceTile(
          icon: AppIcons.appearance,
          title: l10n.settingsThemeAccent,
          subtitle: customized
              ? l10n.settingsThemeAccentCustomDesc
              : l10n.settingsThemeAccentSystemDesc,
          trailing: _SegmentedControl<AccentMode>(
            selected: appearance.accentMode,
            options: [
              _SegmentedOption(
                AccentMode.system,
                l10n.settingsThemeAccentSystem,
              ),
              _SegmentedOption(
                AccentMode.custom,
                l10n.settingsThemeAccentCustom,
              ),
            ],
            onChanged: notifier.setAccentMode,
          ),
        ),
        if (customized)
          _AccentSwatchRow(
            current: appearance.accentColor,
            onPick: (color) => notifier.setAccentColor(color),
            onChoose: () => _chooseAccent(context, l10n),
          )
        else
          _SystemAccentRow(color: systemAccent ?? AppTokens.brandAccent),
      ],
    );
    final imageStyleSelected =
        appearance.appearanceStyle == AppearanceStyle.image;
    // The scrim floor follows the wallpaper's brightness. A bright photo needs
    // a stronger base scrim before the forced dark palette reads on it, so the
    // slider's lower end is the measured floor rather than a fixed 30 %.
    final dimFloor = appearance.backgroundDimFloor;
    final dimValue = appearance.effectiveBackgroundDim;
    final dimMarks = <double, String>{
      dimFloor: '${(dimFloor * 100).round()}%',
      if (0.9 - dimFloor > 0.15)
        (dimFloor + 0.9) / 2: '${(((dimFloor + 0.9) / 2) * 100).round()}%',
      0.9: '90%',
    };
    final backgroundGroup = _SettingsGroup(
      title: l10n.settingsSectionBackground,
      note: imageStyleSelected
          ? imageMode
                ? l10n.settingsBackgroundImageNote
                : l10n.settingsBackgroundImageNoPath
          : null,
      children: [
        _AppearanceTile(
          icon: AppIcons.background,
          title: l10n.settingsAppearanceStyle,
          subtitle: l10n.settingsAppearanceStyleDesc,
          trailing: _SegmentedControl<AppearanceStyle>(
            selected: appearance.appearanceStyle,
            options: [
              _SegmentedOption(
                AppearanceStyle.solid,
                l10n.settingsAppearanceStyleSolid,
              ),
              _SegmentedOption(
                AppearanceStyle.image,
                l10n.settingsAppearanceStyleImage,
              ),
            ],
            onChanged: notifier.setAppearanceStyle,
          ),
        ),
        if (imageStyleSelected)
          _AppearanceTile(
            icon: AppIcons.background,
            title: l10n.settingsBackgroundImage,
            subtitle:
                appearance.backgroundImage ?? l10n.settingsBackgroundImageDesc,
            trailing: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (appearance.backgroundImage != null) ...[
                  _BackgroundImagePreview(path: appearance.backgroundImage!),
                  const SizedBox(width: AppSpacing.xs),
                ],
                TextButton.icon(
                  onPressed: () => _pickBackgroundImage(context, l10n),
                  icon: const Icon(AppIcons.background, size: 18),
                  label: Text(
                    appearance.backgroundImage == null
                        ? l10n.settingsBackgroundImageSelect
                        : l10n.settingsBackgroundImageReplace,
                  ),
                ),
                if (appearance.backgroundImage != null)
                  IconButton(
                    onPressed: () => _clearBackgroundImage(context, l10n),
                    tooltip: l10n.settingsBackgroundImageClear,
                    icon: const Icon(AppIcons.delete, size: 18),
                  ),
              ],
            ),
          ),
        if (imageMode) ...[
          _AppearanceSlider(
            icon: AppIcons.background,
            title: l10n.settingsBackgroundBlur,
            subtitle: l10n.settingsBackgroundBlurDesc,
            value: appearance.backgroundBlur,
            min: 0,
            max: 80,
            divisions: 80,
            label: '${appearance.backgroundBlur.round()}px',
            valueFormatter: (value) => '${value.round()}px',
            marks: {0.0: '0', 40.0: '40', 80.0: '80'},
            onChanged: notifier.setBackgroundBlur,
          ),
          _AppearanceSlider(
            icon: AppIcons.background,
            title: l10n.settingsBackgroundDim,
            subtitle: dimFloor > minWallpaperDim + 0.001
                ? '${l10n.settingsBackgroundDimDesc} '
                      '${l10n.settingsBackgroundDimAuto}'
                : l10n.settingsBackgroundDimDesc,
            value: dimValue,
            min: dimFloor,
            max: maxWallpaperDim,
            divisions: ((maxWallpaperDim - dimFloor) * 100)
                .round()
                .clamp(1, 100)
                .toInt(),
            label: '${(dimValue * 100).round()}%',
            valueFormatter: (value) => '${(value * 100).round()}%',
            marks: dimMarks,
            onChanged: notifier.setBackgroundDim,
          ),
          _AppearanceSlider(
            icon: AppIcons.background,
            title: l10n.settingsBackgroundScale,
            subtitle: l10n.settingsBackgroundScaleDesc,
            value: appearance.backgroundScale,
            min: 1,
            max: 2,
            divisions: 50,
            label: '${appearance.backgroundScale.toStringAsFixed(1)}x',
            valueFormatter: (value) => '${value.toStringAsFixed(1)}x',
            marks: {1.0: '1.0x', 1.5: '1.5x', 2.0: '2.0x'},
            onChanged: notifier.setBackgroundScale,
          ),
        ],
      ],
    );
    final interfaceGroup = _SettingsGroup(
      title: l10n.settingsSectionInterface,
      note: l10n.settingsAppearanceNote,
      children: [
        _AppearanceTile(
          icon: AppIcons.globalTint,
          title: l10n.settingsGlobalTint,
          subtitle: imageMode
              ? l10n.settingsGlobalTintImageForced
              : l10n.settingsGlobalTintDesc,
          enabled: !imageMode,
          trailing: Switch(
            value: imageMode || appearance.globalTint,
            onChanged: imageMode ? null : notifier.setGlobalTint,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.density,
          title: l10n.settingsDensity,
          subtitle: l10n.settingsDensityDesc,
          trailing: _SegmentedControl<UiDensity>(
            selected: appearance.density,
            options: [
              _SegmentedOption(UiDensity.compact, l10n.settingsDensityCompact),
              _SegmentedOption(
                UiDensity.standard,
                l10n.settingsDensityStandard,
              ),
              _SegmentedOption(
                UiDensity.comfortable,
                l10n.settingsDensityComfortable,
              ),
            ],
            onChanged: notifier.setDensity,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.reduceMotion,
          title: l10n.settingsMotion,
          subtitle: l10n.settingsMotionDesc,
          trailing: Switch(
            value: appearance.reduceMotion,
            onChanged: notifier.setReduceMotion,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.window,
          title: l10n.settingsCloseToTray,
          subtitle: l10n.settingsCloseToTrayDesc,
          trailing: Switch(
            value: prefs.closeToTray,
            onChanged: ref.read(preferencesProvider.notifier).setCloseToTray,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.notifications,
          title: l10n.settingsToastDuration,
          subtitle: l10n.settingsToastDurationDesc,
          trailing: _SegmentedControl<int>(
            selected: prefs.toastSeconds,
            options: const [
              _SegmentedOption(2, '2s'),
              _SegmentedOption(3, '3s'),
              _SegmentedOption(5, '5s'),
              _SegmentedOption(8, '8s'),
            ],
            onChanged: ref.read(preferencesProvider.notifier).setToastSeconds,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.instances,
          title: l10n.settingsStartPage,
          subtitle: l10n.settingsStartPageDesc,
          trailing: AppDropdown<String>(
            key: const ValueKey('start-page-dropdown'),
            value: prefs.startPage,
            items: [
              DropdownMenuItem(
                value: '/',
                child: Text(l10n.startPageDashboard),
              ),
              DropdownMenuItem(
                value: '/instances',
                child: Text(l10n.startPageInstances),
              ),
              DropdownMenuItem(
                value: '/download',
                child: Text(l10n.startPageDownload),
              ),
              DropdownMenuItem(
                value: '/accounts',
                child: Text(l10n.startPageAccounts),
              ),
              DropdownMenuItem(value: '/wiki', child: Text(l10n.startPageWiki)),
              DropdownMenuItem(
                value: '/settings',
                child: Text(l10n.startPageSettings),
              ),
            ],
            onChanged: (value) {
              if (value == null) return;
              ref.read(preferencesProvider.notifier).setStartPage(value);
            },
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.reset,
          title: l10n.settingsResetAppearance,
          trailing: Icon(
            AppIcons.chevronRight,
            size: 18,
            color: context.tokens.textTertiary,
          ),
          onTap: () => _resetAppearance(context, l10n),
        ),
      ],
    );
    final languageGroup = _SettingsGroup(
      title: l10n.settingsAppearanceLanguage,
      children: [
        _AppearanceTile(
          icon: AppIcons.language,
          title: l10n.settingsLanguage,
          subtitle: _languageLabel(l10n, locale),
          trailing: Icon(
            AppIcons.chevronRight,
            size: 18,
            color: context.tokens.textTertiary,
          ),
          onTap: () => _chooseLanguage(context, l10n),
        ),
      ],
    );

    // Single-column stack, matching the storage/java/about sections.
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        themeGroup,
        const SizedBox(height: AppSpacing.lg),
        accentGroup,
        const SizedBox(height: AppSpacing.lg),
        backgroundGroup,
        const SizedBox(height: AppSpacing.lg),
        interfaceGroup,
        const SizedBox(height: AppSpacing.lg),
        languageGroup,
      ],
    );
  }

  Future<void> _chooseAccent(
    BuildContext context,
    AppLocalizations l10n,
  ) async {
    final current = ref.read(appearanceProvider).accentColor;
    final controller = TextEditingController(text: _hexColor(current));
    String? error;
    final chosen = await showDialog<Color>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) {
          Color? parsed = _parseHexColor(controller.text);
          return AlertDialog(
            title: Text(l10n.settingsThemeAccent),
            content: SizedBox(
              width: 340,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Wrap(
                    spacing: AppSpacing.xs,
                    runSpacing: AppSpacing.xs,
                    children: [
                      for (final color in _accentPresets)
                        _AccentSwatch(
                          color: color,
                          selected: parsed?.toARGB32() == color.toARGB32(),
                          size: 30,
                          onTap: () {
                            controller.text = _hexColor(color);
                            setDialogState(() => error = null);
                          },
                        ),
                    ],
                  ),
                  const SizedBox(height: AppSpacing.md),
                  TextField(
                    controller: controller,
                    autofocus: true,
                    decoration: InputDecoration(
                      labelText: l10n.settingsThemeAccentCustomHint,
                      errorText: error,
                    ),
                    onChanged: (value) => setDialogState(() => error = null),
                  ),
                  const SizedBox(height: AppSpacing.xs),
                  Text(
                    l10n.settingsThemeAccentCustomHint,
                    style: Theme.of(dialogContext).textTheme.bodySmall
                        ?.copyWith(color: context.tokens.textTertiary),
                  ),
                ],
              ),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(dialogContext).pop(),
                child: Text(l10n.commonCancel),
              ),
              FilledButton(
                onPressed: () {
                  final color = _parseHexColor(controller.text);
                  if (color == null) {
                    setDialogState(
                      () => error = l10n.settingsThemeAccentInvalid,
                    );
                    return;
                  }
                  Navigator.of(dialogContext).pop(color);
                },
                child: Text(l10n.commonSave),
              ),
            ],
          );
        },
      ),
    );
    controller.dispose();
    if (chosen == null || !context.mounted) return;
    await ref.read(appearanceProvider.notifier).setAccentColor(chosen);
  }

  Future<void> _pickBackgroundImage(
    BuildContext context,
    AppLocalizations l10n,
  ) async {
    const typeGroup = XTypeGroup(
      label: 'images',
      extensions: ['png', 'jpg', 'jpeg', 'webp', 'bmp'],
    );
    XFile? file;
    try {
      file = await openFile(acceptedTypeGroups: const [typeGroup]);
    } catch (_) {
      if (context.mounted) {
        _notify(
          context,
          l10n.settingsBackgroundImagePickFailed,
          type: ToastType.error,
        );
      }
      return;
    }
    if (file == null || !context.mounted) return;

    final path = file.path;
    final extension = path.contains('.')
        ? path.split('.').last.toLowerCase()
        : '';
    const allowed = {'png', 'jpg', 'jpeg', 'webp', 'bmp'};
    final picked = File(path);
    if (!allowed.contains(extension) ||
        !picked.existsSync() ||
        picked.lengthSync() > 64 * 1024 * 1024) {
      _notify(
        context,
        l10n.settingsBackgroundImageUnsupported,
        type: ToastType.error,
      );
      return;
    }

    await ref.read(appearanceProvider.notifier).setBackgroundImage(path);
    if (context.mounted) {
      _notify(
        context,
        l10n.settingsBackgroundImagePicked,
        type: ToastType.success,
      );
    }
  }

  Future<void> _clearBackgroundImage(
    BuildContext context,
    AppLocalizations l10n,
  ) async {
    await ref.read(appearanceProvider.notifier).setBackgroundImage(null);
    if (context.mounted) {
      _notify(
        context,
        l10n.settingsBackgroundImageCleared,
        type: ToastType.success,
      );
    }
  }

  Future<void> _resetAppearance(
    BuildContext context,
    AppLocalizations l10n,
  ) async {
    await ref.read(appearanceProvider.notifier).reset();
    if (context.mounted) {
      _notify(
        context,
        l10n.settingsAppearanceResetDone,
        type: ToastType.success,
      );
    }
  }

  String _languageLabel(AppLocalizations l10n, Locale? locale) {
    return switch (locale?.languageCode) {
      'en' => l10n.settingsLanguageEnglish,
      'zh' => l10n.settingsLanguageChinese,
      _ => l10n.settingsLanguageSystem,
    };
  }

  Future<void> _chooseLanguage(
    BuildContext context,
    AppLocalizations l10n,
  ) async {
    final choice = await showDialog<String>(
      context: context,
      builder: (context) => SimpleDialog(
        title: Text(l10n.settingsLanguage),
        children: [
          SimpleDialogOption(
            onPressed: () => Navigator.of(context).pop('system'),
            child: Text(l10n.settingsLanguageSystem),
          ),
          SimpleDialogOption(
            onPressed: () => Navigator.of(context).pop('en'),
            child: Text(l10n.settingsLanguageEnglish),
          ),
          SimpleDialogOption(
            onPressed: () => Navigator.of(context).pop('zh'),
            child: Text(l10n.settingsLanguageChinese),
          ),
        ],
      ),
    );
    if (choice == null || !context.mounted) return;
    await ref
        .read(localeProvider.notifier)
        .setLocale(choice == 'system' ? null : Locale(choice));
    if (context.mounted) {
      _notify(
        context,
        AppLocalizations.of(context).settingsLanguageChanged,
        type: ToastType.success,
      );
    }
  }
}

/// Small landscape preview for the selected wallpaper.
class _BackgroundImagePreview extends StatelessWidget {
  const _BackgroundImagePreview({required this.path});

  final String path;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return ClipRRect(
      borderRadius: BorderRadius.circular(tokens.radiusS),
      child: SizedBox(
        width: 64,
        height: 40,
        child: Image.file(
          File(path),
          fit: BoxFit.cover,
          cacheWidth: 128,
          cacheHeight: 80,
          errorBuilder: (_, _, _) => ColoredBox(
            color: tokens.surface2,
            child: Icon(
              AppIcons.imagePlaceholder,
              size: 18,
              color: tokens.textTertiary,
            ),
          ),
        ),
      ),
    );
  }
}

/// One slider row with the same icon/title rhythm as [_AppearanceTile].
class _AppearanceSlider extends StatelessWidget {
  const _AppearanceSlider({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.value,
    required this.min,
    required this.max,
    required this.divisions,
    required this.label,
    required this.onChanged,
    this.valueFormatter,
    this.marks,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final double value;
  final double min;
  final double max;
  final int divisions;
  final String label;
  final ValueChanged<double> onChanged;

  /// Formats arbitrary slider values for accessibility announcements.
  final String Function(double value)? valueFormatter;

  /// Range labels shown under the track.
  final Map<double, String>? marks;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    final heading = Row(
      children: [
        Container(
          width: 34,
          height: 34,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: tokens.accent.withValues(alpha: 0.10),
            borderRadius: BorderRadius.circular(tokens.radiusM),
          ),
          child: Icon(icon, size: 18, color: tokens.accent),
        ),
        const SizedBox(width: AppSpacing.sm),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: textTheme.titleSmall),
              const SizedBox(height: 2),
              Text(
                subtitle,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
            ],
          ),
        ),
      ],
    );

    final valueText = Text(
      label,
      style: textTheme.labelMedium?.copyWith(color: tokens.textSecondary),
    );

    final slider = AppSlider(
      value: value.clamp(min, max),
      min: min,
      max: max,
      divisions: divisions,
      valueLabel: label,
      valueFormatter: valueFormatter,
      semanticLabel: title,
      marks: marks,
      onChanged: onChanged,
    );

    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.sm,
      ),
      child: LayoutBuilder(
        builder: (context, constraints) {
          // Narrow panels stack the control under the text; wide panels keep it
          // in a fixed column on the right instead of stretching the track
          // across the whole row.
          if (constraints.maxWidth < 720) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    Expanded(child: heading),
                    const SizedBox(width: AppSpacing.sm),
                    valueText,
                  ],
                ),
                const SizedBox(height: AppSpacing.xs),
                slider,
              ],
            );
          }
          return Row(
            children: [
              Expanded(child: heading),
              const SizedBox(width: AppSpacing.lg),
              SizedBox(
                width: 320,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Align(alignment: Alignment.centerRight, child: valueText),
                    const SizedBox(height: AppSpacing.xxs),
                    slider,
                  ],
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}
