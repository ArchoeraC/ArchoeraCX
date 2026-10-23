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

    final themeGroup = _SettingsGroup(
      title: l10n.settingsSectionTheme,
      children: [
        _AppearanceTile(
          icon: AppIcons.globalTint,
          title: l10n.settingsThemeMode,
          subtitle: l10n.settingsThemeModeDesc,
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
    final interfaceGroup = _SettingsGroup(
      title: l10n.settingsSectionInterface,
      note: l10n.settingsAppearanceNote,
      children: [
        _AppearanceTile(
          icon: AppIcons.globalTint,
          title: l10n.settingsGlobalTint,
          subtitle: l10n.settingsGlobalTintDesc,
          trailing: Switch(
            value: appearance.globalTint,
            onChanged: notifier.setGlobalTint,
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
          trailing: DropdownButton<String>(
            key: const ValueKey('start-page-dropdown'),
            value: prefs.startPage,
            isDense: true,
            underline: const SizedBox.shrink(),
            borderRadius: BorderRadius.circular(context.tokens.radiusM),
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
