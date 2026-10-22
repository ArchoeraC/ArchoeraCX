import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/app_settings.dart';
import '../services/core/system_memory.dart';
import '../stores/appearance.dart';
import '../stores/instances.dart';
import '../stores/locale.dart';
import '../stores/providers.dart';
import '../stores/settings.dart';
import '../theme/tokens.dart';
import '../utils/app_info.dart';
import '../widgets/common/app_section.dart';
import '../widgets/common/toast.dart';

import 'package:go_router/go_router.dart';

import '../widgets/common/app_page_header.dart';

enum _SettingsSection { appearance, storage, java, about }

class SettingsPage extends ConsumerStatefulWidget {
  const SettingsPage({super.key});

  @override
  ConsumerState<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends ConsumerState<SettingsPage> {
  _SettingsSection _section = _SettingsSection.appearance;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final settings = ref.watch(appSettingsProvider).value;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AppPageHeader(
          title: l10n.settingsTitle,
          subtitle: l10n.settingsSubtitle,
        ),
        const SizedBox(height: AppSpacing.md),
        Expanded(
          child: LayoutBuilder(
            builder: (context, constraints) {
              final wide = constraints.maxWidth >= 820;
              final content = KeyedSubtree(
                key: ValueKey(_section),
                child: _sectionContent(context, l10n, settings),
              );

              if (wide) {
                return Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    SizedBox(
                      width: 208,
                      child: _SettingsNav(
                        selected: _section,
                        onSelected: (value) => setState(() => _section = value),
                      ),
                    ),
                    const SizedBox(width: AppSpacing.lg),
                    Expanded(
                      child: SingleChildScrollView(
                        padding: const EdgeInsets.only(bottom: AppSpacing.xl),
                        child: Align(
                          alignment: Alignment.topLeft,
                          child: ConstrainedBox(
                            constraints: const BoxConstraints(maxWidth: 920),
                            child: content,
                          ),
                        ),
                      ),
                    ),
                  ],
                );
              }

              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _SettingsTabs(
                    selected: _section,
                    onSelected: (value) => setState(() => _section = value),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  Expanded(
                    child: SingleChildScrollView(
                      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
                      child: content,
                    ),
                  ),
                ],
              );
            },
          ),
        ),
      ],
    );
  }

  Widget _sectionContent(
    BuildContext context,
    AppLocalizations l10n,
    AppSettings? settings,
  ) {
    final locale = ref.watch(localeProvider);
    final appearance = ref.watch(appearanceProvider);
    final systemAppearance = ref.watch(systemAppearanceProvider).value;
    final systemAccent = systemAppearance?.accent;
    return switch (_section) {
      _SettingsSection.appearance => _appearanceContent(
        context,
        l10n,
        appearance,
        systemAccent,
        locale,
      ),
      _SettingsSection.storage => _SettingsCard(
        children: [
          _InstanceRootsEditor(
            l10n: l10n,
            roots: settings?.roots ?? const <String>[],
            defaultRoot: settings?.instancesRoot,
            onAdd: () => _chooseInstancesRoot(context, l10n),
            onRemove: (root) => _removeInstancesRoot(context, l10n, root),
            onMakeDefault: (root) =>
                _makeDefaultInstancesRoot(context, l10n, root),
          ),
          _SettingRow(
            icon: Icons.storage_outlined,
            title: l10n.settingsDataRoot,
            value: settings?.dataRoot ?? '—',
          ),
        ],
      ),
      _SettingsSection.java => Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _SettingsCard(
            children: [
              _SettingRow(
                icon: Icons.auto_awesome_rounded,
                title: l10n.settingsJavaAuto,
                subtitle: l10n.settingsJavaAutoHint,
                trailing: Switch(
                  value: settings?.javaAuto ?? true,
                  onChanged: (value) => _setJavaAuto(context, l10n, value),
                ),
              ),
              _SettingRow(
                icon: Icons.terminal_rounded,
                title: l10n.settingsJavaCurrent,
                value: (settings?.javaAuto ?? true)
                    ? l10n.settingsJavaAutoValue
                    : (settings?.javaPath ?? '—'),
                trailing: (settings?.javaAuto ?? true)
                    ? null
                    : TextButton(
                        onPressed: () => _setJavaAuto(context, l10n, true),
                        child: Text(l10n.settingsJavaUseAuto),
                      ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _SettingRow(
            icon: Icons.developer_board_rounded,
            title: l10n.javaPageManage,
            subtitle: l10n.javaPageManageHint,
            onTap: () => context.go('/settings/java'),
          ),
          const SizedBox(height: AppSpacing.md),
          _RuntimeSettingsPanel(settings: settings),
        ],
      ),
      _SettingsSection.about => _AboutPanel(settings: settings),
    };
  }

  Widget _appearanceContent(
    BuildContext context,
    AppLocalizations l10n,
    AppearanceSettings appearance,
    Color? systemAccent,
    Locale? locale,
  ) {
    final notifier = ref.read(appearanceProvider.notifier);
    final customized = appearance.accentMode == AccentMode.custom;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _SettingsGroup(
          title: l10n.settingsSectionTheme,
          children: [
            _AppearanceTile(
              icon: Icons.contrast_rounded,
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
        ),
        const SizedBox(height: AppSpacing.lg),
        _SettingsGroup(
          title: l10n.settingsSectionAccent,
          children: [
            _AppearanceTile(
              icon: Icons.palette_outlined,
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
        ),
        const SizedBox(height: AppSpacing.lg),
        _SettingsGroup(
          title: l10n.settingsSectionInterface,
          note: l10n.settingsAppearanceNote,
          children: [
            _AppearanceTile(
              icon: Icons.tonality_rounded,
              title: l10n.settingsGlobalTint,
              subtitle: l10n.settingsGlobalTintDesc,
              trailing: Switch(
                value: appearance.globalTint,
                onChanged: notifier.setGlobalTint,
              ),
            ),
            _AppearanceTile(
              icon: Icons.view_agenda_outlined,
              title: l10n.settingsDensity,
              subtitle: l10n.settingsDensityDesc,
              trailing: _SegmentedControl<UiDensity>(
                selected: appearance.density,
                options: [
                  _SegmentedOption(
                    UiDensity.compact,
                    l10n.settingsDensityCompact,
                  ),
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
              icon: Icons.animation_rounded,
              title: l10n.settingsMotion,
              subtitle: l10n.settingsMotionDesc,
              trailing: Switch(
                value: appearance.reduceMotion,
                onChanged: notifier.setReduceMotion,
              ),
            ),
            _AppearanceTile(
              icon: Icons.restart_alt_rounded,
              title: l10n.settingsResetAppearance,
              trailing: Icon(
                Icons.chevron_right_rounded,
                size: 18,
                color: context.tokens.textTertiary,
              ),
              onTap: () => _resetAppearance(context, l10n),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.lg),
        _SettingsGroup(
          title: l10n.settingsAppearanceLanguage,
          children: [
            _AppearanceTile(
              icon: Icons.translate_rounded,
              title: l10n.settingsLanguage,
              subtitle: _languageLabel(l10n, locale),
              trailing: Icon(
                Icons.chevron_right_rounded,
                size: 18,
                color: context.tokens.textTertiary,
              ),
              onTap: () => _chooseLanguage(context, l10n),
            ),
          ],
        ),
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

  Future<void> _setJavaAuto(
    BuildContext context,
    AppLocalizations l10n,
    bool auto,
  ) async {
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setJavaSelection(
            javaPath: auto
                ? null
                : (ref.read(appSettingsProvider).value?.javaPath),
            javaAuto: auto,
          );
      if (context.mounted) {
        _notify(context, l10n.settingsSaved, type: ToastType.success);
      }
    } catch (error) {
      if (context.mounted) {
        _notify(context, error.toString(), type: ToastType.error);
      }
    }
  }

  Future<void> _chooseInstancesRoot(
    BuildContext context,
    AppLocalizations l10n,
  ) async {
    final selected = await getDirectoryPath(
      confirmButtonText: l10n.settingsChooseFolder,
    );
    if (selected == null || !context.mounted) return;

    try {
      await ref.read(appSettingsProvider.notifier).addInstanceRoot(selected);
      if (!context.mounted) return;
      await _syncInstances(context, l10n);
    } catch (error) {
      if (context.mounted) {
        _notify(context, error.toString(), type: ToastType.error);
      }
    }
  }

  Future<void> _removeInstancesRoot(
    BuildContext context,
    AppLocalizations l10n,
    String root,
  ) async {
    try {
      await ref.read(appSettingsProvider.notifier).removeInstanceRoot(root);
      if (!context.mounted) return;
      await _syncInstances(context, l10n);
    } catch (error) {
      if (context.mounted) {
        _notify(context, error.toString(), type: ToastType.error);
      }
    }
  }

  Future<void> _makeDefaultInstancesRoot(
    BuildContext context,
    AppLocalizations l10n,
    String root,
  ) async {
    try {
      await ref.read(appSettingsProvider.notifier).setDefaultInstanceRoot(root);
      if (!context.mounted) return;
      await _syncInstances(context, l10n);
    } catch (error) {
      if (context.mounted) {
        _notify(context, error.toString(), type: ToastType.error);
      }
    }
  }

  Future<void> _syncInstances(
    BuildContext context,
    AppLocalizations l10n,
  ) async {
    final result = await ref.read(instancesProvider.notifier).sync();
    if (!context.mounted) return;
    final message = StringBuffer(l10n.settingsRootsUpdated);
    if (result != null && result.adopted.isNotEmpty) {
      message.write(' ${l10n.instanceSyncAdopted(result.adopted.length)}');
    }
    if (result != null && result.missing.isNotEmpty) {
      message.write(' ${l10n.instanceSyncMissing(result.missing.length)}');
    }
    _notify(context, message.toString(), type: ToastType.success);
  }

  void _notify(
    BuildContext context,
    String message, {
    ToastType type = ToastType.info,
  }) {
    toast(message, type: type);
  }
}

class _InstanceRootsEditor extends StatelessWidget {
  const _InstanceRootsEditor({
    required this.l10n,
    required this.roots,
    required this.defaultRoot,
    required this.onAdd,
    required this.onRemove,
    required this.onMakeDefault,
  });

  final AppLocalizations l10n;
  final List<String> roots;
  final String? defaultRoot;
  final VoidCallback onAdd;
  final ValueChanged<String> onRemove;
  final ValueChanged<String> onMakeDefault;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm,
            vertical: AppSpacing.xs,
          ),
          child: Row(
            children: [
              Icon(
                Icons.folder_outlined,
                size: 20,
                color: tokens.textSecondary,
              ),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(
                  l10n.settingsInstanceRoots,
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
              ),
              TextButton.icon(
                onPressed: onAdd,
                icon: const Icon(Icons.add_rounded, size: 17),
                label: Text(l10n.settingsAddRoot),
              ),
            ],
          ),
        ),
        for (final root in roots)
          ListTile(
            contentPadding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.sm,
            ),
            leading: Icon(
              Icons.folder_open_rounded,
              size: 20,
              color: root == defaultRoot ? tokens.accent : tokens.textSecondary,
            ),
            title: Text(
              root,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodySmall,
            ),
            subtitle: root == defaultRoot
                ? Text(
                    l10n.settingsDefaultRoot,
                    style: Theme.of(context).textTheme.labelSmall
                        ?.copyWith(color: tokens.accent),
                  )
                : null,
            trailing: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (root != defaultRoot)
                  IconButton(
                    onPressed: () => onMakeDefault(root),
                    tooltip: l10n.settingsMakeDefault,
                    icon: const Icon(Icons.star_outline_rounded, size: 19),
                  ),
                if (roots.length > 1)
                  IconButton(
                    onPressed: () => onRemove(root),
                    tooltip: l10n.settingsRemoveRoot,
                    icon: Icon(
                      Icons.remove_circle_outline_rounded,
                      size: 19,
                      color: tokens.danger,
                    ),
                  ),
              ],
            ),
          ),
      ],
    );
  }
}

class _SettingsNav extends StatelessWidget {
  const _SettingsNav({required this.selected, required this.onSelected});

  final _SettingsSection selected;
  final ValueChanged<_SettingsSection> onSelected;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final items = _settingsItems(l10n);

    return Column(
      children: [
        for (final item in items)
          Padding(
            padding: const EdgeInsets.only(bottom: 2),
            child: Container(
              decoration: BoxDecoration(
                border: Border(
                  left: BorderSide(
                    color: item.section == selected
                        ? tokens.accent
                        : Colors.transparent,
                    width: 2,
                  ),
                ),
              ),
              child: ListTile(
                dense: true,
                selected: item.section == selected,
                selectedTileColor: tokens.accentSoft,
                hoverColor: tokens.surface2,
                contentPadding: const EdgeInsets.symmetric(
                  horizontal: AppSpacing.sm,
                ),
                leading: Icon(
                  item.icon,
                  size: 19,
                  color: item.section == selected
                      ? tokens.accent
                      : tokens.textSecondary,
                ),
                title: Text(
                  item.label,
                  style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: item.section == selected
                        ? tokens.accent
                        : tokens.textPrimary,
                    fontWeight: FontWeight.w500,
                  ),
                ),
                onTap: () => onSelected(item.section),
              ),
            ),
          ),
      ],
    );
  }
}

class _SettingsTabs extends StatelessWidget {
  const _SettingsTabs({required this.selected, required this.onSelected});

  final _SettingsSection selected;
  final ValueChanged<_SettingsSection> onSelected;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final items = _settingsItems(l10n);

    return SizedBox(
      height: 42,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        itemCount: items.length,
        separatorBuilder: (_, _) => const SizedBox(width: AppSpacing.md),
        itemBuilder: (context, index) {
          final item = items[index];
          final active = item.section == selected;
          return Material(
            color: Colors.transparent,
            child: InkWell(
              onTap: () => onSelected(item.section),
              hoverColor: tokens.surface2,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xxs),
                decoration: BoxDecoration(
                  border: Border(
                    bottom: BorderSide(
                      color: active ? tokens.accent : Colors.transparent,
                      width: 2,
                    ),
                  ),
                ),
                child: Row(
                  children: [
                    Icon(
                      item.icon,
                      size: 16,
                      color: active ? tokens.accent : tokens.textSecondary,
                    ),
                    const SizedBox(width: AppSpacing.xxs),
                    Text(
                      item.label,
                      style: Theme.of(context).textTheme.labelLarge?.copyWith(
                        color: active ? tokens.accent : tokens.textSecondary,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}

class _SettingsItem {
  const _SettingsItem(this.section, this.label, this.icon);

  final _SettingsSection section;
  final String label;
  final IconData icon;
}

List<_SettingsItem> _settingsItems(AppLocalizations l10n) => [
  _SettingsItem(
    _SettingsSection.appearance,
    l10n.settingsAppearance,
    Icons.palette_outlined,
  ),
  _SettingsItem(
    _SettingsSection.storage,
    l10n.settingsStorage,
    Icons.folder_outlined,
  ),
  _SettingsItem(_SettingsSection.java, l10n.settingsJava, Icons.coffee_rounded),
  _SettingsItem(
    _SettingsSection.about,
    l10n.settingsAbout,
    Icons.info_outline_rounded,
  ),
];

class _RuntimeSettingsPanel extends ConsumerStatefulWidget {
  const _RuntimeSettingsPanel({this.settings});

  final AppSettings? settings;

  @override
  ConsumerState<_RuntimeSettingsPanel> createState() =>
      _RuntimeSettingsPanelState();
}

class _RuntimeSettingsPanelState extends ConsumerState<_RuntimeSettingsPanel> {
  final _min = TextEditingController();
  final _max = TextEditingController();
  final _jvm = TextEditingController();
  final _game = TextEditingController();
  final _minFocus = FocusNode();
  final _maxFocus = FocusNode();
  final _jvmFocus = FocusNode();
  final _gameFocus = FocusNode();

  Timer? _debounce;
  String _gcMode = 'auto';
  String? _error;
  bool _saving = false;
  bool _saveInFlight = false;
  bool _saveRequested = false;
  SystemMemoryInfo? _memory;
  bool _memoryLoading = true;

  @override
  void initState() {
    super.initState();
    _syncControllers();
    _loadMemory();
    for (final focus in [_minFocus, _maxFocus, _jvmFocus, _gameFocus]) {
      focus.addListener(() {
        if (!focus.hasFocus) _saveNow();
      });
    }
  }

  @override
  void didUpdateWidget(covariant _RuntimeSettingsPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.settings != widget.settings) _syncControllers();
  }

  void _syncControllers() {
    final settings = widget.settings;
    if (!_minFocus.hasFocus) _min.text = '${settings?.memoryMinMb ?? 512}';
    if (!_maxFocus.hasFocus) _max.text = '${settings?.memoryMaxMb ?? 2048}';
    if (!_jvmFocus.hasFocus) _jvm.text = settings?.jvmArgs ?? '';
    if (!_gameFocus.hasFocus) _game.text = settings?.gameArgs ?? '';
    _gcMode = settings?.gcMode ?? 'auto';
  }

  Future<void> _loadMemory() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      if (mounted) setState(() => _memoryLoading = false);
      return;
    }
    if (mounted && !_memoryLoading) setState(() => _memoryLoading = true);
    try {
      final memory = await client.getSystemMemory();
      if (mounted) {
        setState(() {
          _memory = memory;
          _memoryLoading = false;
        });
      }
    } catch (_) {
      if (mounted) setState(() => _memoryLoading = false);
    }
  }

  void _scheduleSave() {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 500), _saveNow);
  }

  Future<void> _saveNow() async {
    if (!mounted) return;
    if (_saveInFlight) {
      _saveRequested = true;
      return;
    }

    final l10n = AppLocalizations.of(context);
    final min = int.tryParse(_min.text.trim());
    final max = int.tryParse(_max.text.trim());
    if (min == null || max == null || min < 128 || max > 65536 || min > max) {
      setState(() => _error = l10n.settingsMemoryInvalid);
      return;
    }

    _saveInFlight = true;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(
            memoryMinMb: min,
            memoryMaxMb: max,
            jvmArgs: _jvm.text.trim(),
            gcMode: _gcMode,
            gameArgs: _game.text.trim(),
          );
    } catch (error) {
      if (mounted) setState(() => _error = error.toString());
    } finally {
      _saveInFlight = false;
      if (mounted) {
        setState(() => _saving = false);
        if (_saveRequested) {
          _saveRequested = false;
          unawaited(_saveNow());
        }
      }
    }
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _min.dispose();
    _max.dispose();
    _jvm.dispose();
    _game.dispose();
    _minFocus.dispose();
    _maxFocus.dispose();
    _jvmFocus.dispose();
    _gameFocus.dispose();
    super.dispose();
  }

  String _formatMb(int mb) =>
      mb >= 1024 ? '${(mb / 1024).toStringAsFixed(1)} GB' : '$mb MB';

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
    final memory = _memory;
    final total = memory?.totalMb ?? 0;
    final available = memory?.availableMb ?? 0;
    final maxMb = int.tryParse(_max.text.trim()) ?? 2048;
    final estimated = maxMb + 512;
    final overAvailable = memory?.isKnown == true && estimated > available;
    final ratio = total > 0 ? (estimated / total).clamp(0.0, 1.0) : 0.0;

    return _SettingsCard(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.sm,
            AppSpacing.xs,
            AppSpacing.sm,
            AppSpacing.sm,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    Icons.memory_rounded,
                    size: 19,
                    color: tokens.textSecondary,
                  ),
                  const SizedBox(width: AppSpacing.xs),
                  Text(
                    l10n.settingsMemorySystem,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                  const Spacer(),
                  if (_memoryLoading)
                    const SizedBox(
                      width: 15,
                      height: 15,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  else
                    IconButton(
                      onPressed: _loadMemory,
                      tooltip: l10n.refresh,
                      icon: const Icon(Icons.refresh_rounded, size: 18),
                    ),
                ],
              ),
              const SizedBox(height: AppSpacing.xs),
              Wrap(
                spacing: AppSpacing.lg,
                runSpacing: AppSpacing.xs,
                children: [
                  _MemoryMetric(
                    label: l10n.settingsMemoryTotal,
                    value: memory?.isKnown == true
                        ? _formatMb(total)
                        : l10n.settingsMemoryUnavailable,
                  ),
                  _MemoryMetric(
                    label: l10n.settingsMemoryAvailable,
                    value: memory?.isKnown == true
                        ? _formatMb(available)
                        : l10n.settingsMemoryUnavailable,
                  ),
                  _MemoryMetric(
                    label: l10n.settingsMemoryEstimated,
                    value: _formatMb(estimated),
                    color: overAvailable ? tokens.warning : null,
                  ),
                ],
              ),
              if (memory?.isKnown == true) ...[
                const SizedBox(height: AppSpacing.sm),
                ClipRect(
                  child: LinearProgressIndicator(
                    value: ratio,
                    minHeight: 4,
                    backgroundColor: tokens.surface3,
                    color: overAvailable ? tokens.warning : tokens.accent,
                  ),
                ),
              ],
              if (overAvailable) ...[
                const SizedBox(height: AppSpacing.xxs),
                Text(
                  l10n.settingsMemoryOverAvailable,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.warning),
                ),
              ],
              const SizedBox(height: AppSpacing.xxs),
              Text(
                l10n.settingsRuntimeHint,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
          ),
        ),
        Padding(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm,
            vertical: AppSpacing.xs,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Wrap(
                spacing: AppSpacing.sm,
                runSpacing: AppSpacing.sm,
                children: [
                  SizedBox(
                    width: 150,
                    child: TextField(
                      controller: _min,
                      focusNode: _minFocus,
                      keyboardType: TextInputType.number,
                      onChanged: (_) {
                        setState(() {});
                        _scheduleSave();
                      },
                      onSubmitted: (_) => _saveNow(),
                      decoration: InputDecoration(
                        labelText: l10n.settingsMemoryMin,
                      ),
                    ),
                  ),
                  SizedBox(
                    width: 150,
                    child: TextField(
                      controller: _max,
                      focusNode: _maxFocus,
                      keyboardType: TextInputType.number,
                      onChanged: (_) {
                        setState(() {});
                        _scheduleSave();
                      },
                      onSubmitted: (_) => _saveNow(),
                      decoration: InputDecoration(
                        labelText: l10n.settingsMemoryMax,
                      ),
                    ),
                  ),
                  SizedBox(
                    width: 240,
                    child: DropdownButtonFormField<String>(
                      key: ValueKey('gc-$_gcMode'),
                      initialValue: _gcMode,
                      isExpanded: true,
                      decoration: InputDecoration(labelText: l10n.settingsGc),
                      items: [
                        for (final item in _gcOptions(l10n))
                          DropdownMenuItem(
                            value: item.$1,
                            child: Text(item.$2),
                          ),
                      ],
                      onChanged: (value) {
                        if (value == null) return;
                        setState(() => _gcMode = value);
                        _saveNow();
                      },
                    ),
                  ),
                ],
              ),
              const SizedBox(height: AppSpacing.sm),
              TextField(
                controller: _jvm,
                focusNode: _jvmFocus,
                onChanged: (_) => _scheduleSave(),
                onSubmitted: (_) => _saveNow(),
                decoration: InputDecoration(
                  labelText: l10n.settingsJvmArgs,
                  hintText: l10n.settingsJvmArgsHint,
                ),
              ),
              const SizedBox(height: AppSpacing.sm),
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
  }
}

class _MemoryMetric extends StatelessWidget {
  const _MemoryMetric({required this.label, required this.value, this.color});

  final String label;
  final String value;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: Theme.of(context).textTheme.bodySmall
              ?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(height: 1),
        Text(
          value,
          style: Theme.of(context).textTheme.titleSmall
              ?.copyWith(color: color ?? tokens.textPrimary),
        ),
      ],
    );
  }
}

class _SettingsCard extends StatelessWidget {
  const _SettingsCard({required this.children});

  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return AppSection(
      showTop: true,
      showBottom: true,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Column(
        children: [
          for (var i = 0; i < children.length; i++) ...[
            children[i],
            if (i < children.length - 1)
              Divider(
                height: 1,
                indent: AppSpacing.sm,
                endIndent: AppSpacing.sm,
                color: context.tokens.border,
              ),
          ],
        ],
      ),
    );
  }
}

class _SettingRow extends StatelessWidget {
  const _SettingRow({
    required this.icon,
    required this.title,
    this.subtitle,
    this.value,
    this.trailing,
    this.onTap,
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final String? value;
  final Widget? trailing;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs),
      onTap: onTap,
      leading: Icon(icon, size: 20, color: tokens.textSecondary),
      title: Text(title, style: Theme.of(context).textTheme.bodyMedium),
      subtitle: subtitle == null
          ? null
          : Text(
              subtitle!,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textTertiary),
            ),
      trailing:
          trailing ??
          (value == null
              ? null
              : ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 380),
                  child: Text(
                    value!,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodySmall
                        ?.copyWith(color: tokens.textTertiary),
                  ),
                )),
    );
  }
}

// ---------------------------------------------------------------------------
// Appearance helpers
// ---------------------------------------------------------------------------

const _accentPresets = <Color>[
  Color(0xFF4F7CFF),
  Color(0xFF7A9BFF),
  Color(0xFF9B8CFF),
  Color(0xFFFF6B9D),
  Color(0xFFFF6B61),
  Color(0xFFFFA24D),
  Color(0xFF4DDB9B),
  Color(0xFF4DD8E0),
];

String _hexColor(Color color) {
  final value = color.toARGB32() & 0xFFFFFF;
  return '#${value.toRadixString(16).padLeft(6, '0').toUpperCase()}';
}

Color? _parseHexColor(String raw) {
  var text = raw.trim();
  if (text.startsWith('#')) text = text.substring(1);
  if (text.length != 6 && text.length != 8) return null;
  final value = int.tryParse(text, radix: 16);
  if (value == null) return null;
  return Color(text.length == 8 ? value : 0xFF000000 | value);
}

/// Settings group: a small section label above a filled row card, with an
/// optional note underneath. Mirrors the section/title/note rhythm used by the
/// other Archoera apps while keeping the launcher's angular panel shape.
class _SettingsGroup extends StatelessWidget {
  const _SettingsGroup({
    required this.title,
    required this.children,
    this.note,
  });

  final String title;
  final List<Widget> children;
  final String? note;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final noteText = note;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.only(left: 2, bottom: AppSpacing.xs),
          child: Text(
            title,
            style: textTheme.labelSmall?.copyWith(
              color: tokens.textSecondary,
              letterSpacing: 1.2,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
        AppSection(
          showTop: true,
          showBottom: true,
          padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
          child: Column(
            children: [
              for (var index = 0; index < children.length; index++) ...[
                children[index],
                if (index < children.length - 1)
                  Divider(
                    height: 1,
                    indent: AppSpacing.sm,
                    endIndent: AppSpacing.sm,
                    color: tokens.border,
                  ),
              ],
            ],
          ),
        ),
        if (noteText != null)
          Padding(
            padding: const EdgeInsets.only(left: 2, top: AppSpacing.xs),
            child: Text(
              noteText,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
          ),
      ],
    );
  }
}

/// One appearance option: icon badge, title with a short description, and a
/// trailing control. The whole row is tappable when [onTap] is set.
class _AppearanceTile extends StatelessWidget {
  const _AppearanceTile({
    required this.icon,
    required this.title,
    this.subtitle,
    this.trailing,
    this.onTap,
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final Widget? trailing;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final subtitleText = subtitle;
    final control = trailing;

    final content = Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        children: [
          Container(
            width: 34,
            height: 34,
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
                if (subtitleText != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    subtitleText,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                ],
              ],
            ),
          ),
          if (control != null) ...[
            const SizedBox(width: AppSpacing.sm),
            control,
          ],
        ],
      ),
    );

    if (onTap == null) return content;
    return Material(
      color: Colors.transparent,
      child: InkWell(onTap: onTap, child: content),
    );
  }
}

class _SegmentedOption<T> {
  const _SegmentedOption(this.value, this.label);

  final T value;
  final String label;
}

/// Compact segmented control with a flat track and an accent-tinted selection.
class _SegmentedControl<T> extends StatelessWidget {
  const _SegmentedControl({
    required this.options,
    required this.selected,
    required this.onChanged,
  });

  final List<_SegmentedOption<T>> options;
  final T selected;
  final ValueChanged<T> onChanged;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Container(
      padding: const EdgeInsets.all(3),
      decoration: BoxDecoration(
        color: tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusM),
        border: Border.all(color: tokens.border),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final option in options)
            Material(
              color: Colors.transparent,
              child: InkWell(
                borderRadius: BorderRadius.circular(tokens.radiusS),
                onTap: () => onChanged(option.value),
                child: AnimatedContainer(
                  duration: tokens.fast,
                  curve: tokens.curve,
                  padding: const EdgeInsets.symmetric(
                    horizontal: AppSpacing.sm,
                    vertical: 5,
                  ),
                  decoration: BoxDecoration(
                    color: option.value == selected
                        ? tokens.accent.withValues(alpha: 0.14)
                        : Colors.transparent,
                    borderRadius: BorderRadius.circular(tokens.radiusS),
                  ),
                  child: Text(
                    option.label,
                    maxLines: 1,
                    style: textTheme.labelMedium?.copyWith(
                      color: option.value == selected
                          ? tokens.accent
                          : tokens.textSecondary,
                      fontWeight: option.value == selected
                          ? FontWeight.w600
                          : FontWeight.w400,
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

/// Preset swatches plus a picker for a custom hex color.
class _AccentSwatchRow extends StatelessWidget {
  const _AccentSwatchRow({
    required this.current,
    required this.onPick,
    required this.onChoose,
  });

  final Color current;
  final ValueChanged<Color> onPick;
  final VoidCallback onChoose;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Padding(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.sm,
        0,
        AppSpacing.sm,
        AppSpacing.sm,
      ),
      child: Row(
        children: [
          Expanded(
            child: Wrap(
              spacing: AppSpacing.xs,
              runSpacing: AppSpacing.xs,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                for (final color in _accentPresets)
                  _AccentSwatch(
                    color: color,
                    selected: color.toARGB32() == current.toARGB32(),
                    onTap: () => onPick(color),
                  ),
                Tooltip(
                  message: _hexColor(current),
                  child: InkWell(
                    borderRadius: BorderRadius.circular(26),
                    onTap: onChoose,
                    child: Container(
                      width: 26,
                      height: 26,
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: tokens.surface2,
                        border: Border.all(color: tokens.borderStrong),
                      ),
                      child: Icon(
                        Icons.colorize_rounded,
                        size: 14,
                        color: tokens.textSecondary,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          Text(
            _hexColor(current),
            style: textTheme.labelSmall?.copyWith(
              color: tokens.textTertiary,
              letterSpacing: 0,
            ),
          ),
        ],
      ),
    );
  }
}

/// Read-only swatch shown when the launcher follows the desktop accent.
class _SystemAccentRow extends StatelessWidget {
  const _SystemAccentRow({required this.color});

  final Color color;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Padding(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.sm,
        0,
        AppSpacing.sm,
        AppSpacing.sm,
      ),
      child: Row(
        children: [
          Container(
            width: 22,
            height: 22,
            decoration: BoxDecoration(
              color: color,
              shape: BoxShape.circle,
              border: Border.all(color: tokens.borderStrong),
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          const Spacer(),
          Text(
            _hexColor(color),
            style: textTheme.labelSmall?.copyWith(
              color: tokens.textTertiary,
              letterSpacing: 0,
            ),
          ),
        ],
      ),
    );
  }
}

class _AccentSwatch extends StatelessWidget {
  const _AccentSwatch({
    required this.color,
    required this.selected,
    required this.onTap,
    this.size = 26,
  });

  final Color color;
  final bool selected;
  final VoidCallback onTap;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Tooltip(
      message: _hexColor(color),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(size),
        child: Container(
          width: size,
          height: size,
          decoration: BoxDecoration(
            color: color,
            shape: BoxShape.circle,
            border: Border.all(
              color: selected ? tokens.textPrimary : tokens.border,
              width: selected ? 2 : 1,
            ),
          ),
          child: selected
              ? Icon(
                  Icons.check_rounded,
                  size: size * 0.6,
                  color: color.computeLuminance() > 0.55
                      ? const Color(0xFF0B1020)
                      : Colors.white,
                )
              : null,
        ),
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// About
// ---------------------------------------------------------------------------

const _repoUrl = 'https://github.com/BetaStudio2/ArchoeraCX';
const _issuesUrl = 'https://github.com/BetaStudio2/ArchoeraCX/issues';
const _privacyUrl =
    'https://github.com/BetaStudio2/ArchoeraCX/blob/main/PRIVACY.md';
const _securityUrl =
    'https://github.com/BetaStudio2/ArchoeraCX/blob/main/SECURITY.md';
const _licenseUrl =
    'https://github.com/BetaStudio2/ArchoeraCX/blob/main/LICENSE';

const _thanks = <(String, String, String)>[
  ('Flutter', 'flutter.dev', 'https://flutter.dev'),
  ('.NET', 'dotnet.microsoft.com', 'https://dotnet.microsoft.com'),
  ('Rust', 'rust-lang.org', 'https://www.rust-lang.org'),
  ('Modrinth', 'modrinth.com', 'https://modrinth.com'),
  ('BMCLAPI', 'bmclapi2.bangbang93.com', 'https://bmclapi2.bangbang93.com'),
  (
    'Noto Sans SC',
    'Google Fonts',
    'https://fonts.google.com/noto/specimen/Noto+Sans+SC',
  ),
];

Map<String, Object?>? _parseBuildInfo(String? raw) {
  if (raw == null || raw.isEmpty) return null;
  try {
    final decoded = jsonDecode(raw);
    return decoded is Map<String, Object?> ? decoded : null;
  } on FormatException {
    return null;
  }
}

class _AboutPanel extends ConsumerWidget {
  const _AboutPanel({this.settings});

  final AppSettings? settings;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final client = ref.watch(coreClientProvider);
    final build = _parseBuildInfo(client?.buildInfo);
    final coreLabel = build == null
        ? l10n.settingsAboutCoreUnknown
        : '${build['name'] ?? 'archoera_core'} '
              '${build['version'] ?? '—'} '
              '(ABI ${build['abiVersion'] ?? '—'})';
    final environment = <String>[
      'ArchoeraCX ${AppInfo.version}',
      '${l10n.settingsAboutPlatform}: ${Platform.operatingSystem} '
          '${Platform.operatingSystemVersion}',
      '${l10n.settingsAboutRuntime}: Dart ${Platform.version.split(' ').first}',
      '${l10n.settingsAboutCore}: $coreLabel',
      if (settings?.dataRoot != null)
        '${l10n.settingsDataRoot}: ${settings!.dataRoot}',
    ].join('\n');

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _SettingsCard(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.xs,
                AppSpacing.sm,
                AppSpacing.xs,
                AppSpacing.sm,
              ),
              child: Row(
                children: [
                  Container(
                    width: 64,
                    height: 64,
                    decoration: BoxDecoration(
                      color: tokens.surface2,
                      borderRadius: BorderRadius.circular(tokens.radiusM),
                      border: Border.all(color: tokens.border),
                    ),
                    padding: const EdgeInsets.all(AppSpacing.xs),
                    child: Image.asset(
                      'assets/branding/archoera_icon_256.png',
                      fit: BoxFit.contain,
                    ),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'ArchoeraCX',
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          l10n.settingsAboutTagline,
                          style: Theme.of(context).textTheme.bodySmall
                              ?.copyWith(color: tokens.textSecondary),
                        ),
                        const SizedBox(height: AppSpacing.xs),
                        Wrap(
                          spacing: AppSpacing.xs,
                          runSpacing: AppSpacing.xxs,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            _InfoChip(
                              label: AppInfo.version,
                              color: tokens.accent,
                              background: tokens.accentSoft,
                            ),
                            if (AppInfo.isDevelopmentBuild)
                              _InfoChip(
                                label: l10n.developmentBuildBadge,
                                color: tokens.warning,
                                background: tokens.warning.withValues(
                                  alpha: 0.14,
                                ),
                              ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.xs,
                0,
                AppSpacing.xs,
                AppSpacing.sm,
              ),
              child: Text(
                l10n.settingsAboutDesc,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.lg),
        _AboutSectionLabel(text: l10n.settingsAboutEnvironment),
        const SizedBox(height: AppSpacing.xs),
        _SettingsCard(
          children: [
            _AboutEnvRow(
              icon: Icons.tag_rounded,
              label: l10n.settingsVersion,
              value: AppInfo.version,
            ),
            _AboutEnvRow(
              icon: Icons.devices_rounded,
              label: l10n.settingsAboutPlatform,
              value:
                  '${Platform.operatingSystem} ${Platform.operatingSystemVersion}',
            ),
            _AboutEnvRow(
              icon: Icons.memory_rounded,
              label: l10n.settingsAboutRuntime,
              value: 'Dart ${Platform.version.split(' ').first}',
            ),
            _AboutEnvRow(
              icon: Icons.settings_suggest_rounded,
              label: l10n.settingsAboutCore,
              value: coreLabel,
            ),
            if (settings?.dataRoot != null)
              _AboutEnvRow(
                icon: Icons.storage_rounded,
                label: l10n.settingsDataRoot,
                value: settings!.dataRoot,
              ),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.xs,
                AppSpacing.xxs,
                AppSpacing.xs,
                AppSpacing.sm,
              ),
              child: Align(
                alignment: Alignment.centerRight,
                child: TextButton.icon(
                  onPressed: () {
                    Clipboard.setData(ClipboardData(text: environment));
                    _notify(
                      context,
                      l10n.settingsAboutCopied,
                      type: ToastType.success,
                    );
                  },
                  icon: const Icon(Icons.copy_rounded, size: 17),
                  label: Text(l10n.settingsAboutCopyEnvironment),
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.lg),
        _AboutSectionLabel(text: l10n.settingsAboutCommunity),
        const SizedBox(height: AppSpacing.xs),
        _SettingsCard(
          children: [
            _AboutLinkRow(
              icon: Icons.code_rounded,
              title: l10n.settingsAboutRepository,
              value: 'github.com/BetaStudio2/ArchoeraCX',
              onTap: () => _openExternal(context, l10n, _repoUrl),
            ),
            _AboutLinkRow(
              icon: Icons.bug_report_outlined,
              title: l10n.settingsAboutIssues,
              value: 'github.com/BetaStudio2/ArchoeraCX/issues',
              onTap: () => _openExternal(context, l10n, _issuesUrl),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.lg),
        _AboutSectionLabel(text: l10n.settingsAboutThanks),
        const SizedBox(height: AppSpacing.xs),
        _SettingsCard(
          children: [
            for (final item in _thanks)
              _AboutLinkRow(
                icon: Icons.favorite_outline_rounded,
                title: item.$1,
                value: item.$2,
                onTap: () => _openExternal(context, l10n, item.$3),
              ),
          ],
        ),
        const SizedBox(height: AppSpacing.lg),
        _AboutSectionLabel(text: l10n.settingsAboutLegal),
        const SizedBox(height: AppSpacing.xs),
        _SettingsCard(
          children: [
            _AboutLinkRow(
              icon: Icons.gavel_rounded,
              title: l10n.settingsLicense,
              value: 'AGPL-3.0-only',
              onTap: () => _openExternal(context, l10n, _licenseUrl),
            ),
            _AboutLinkRow(
              icon: Icons.inventory_2_outlined,
              title: l10n.settingsAboutThirdParty,
              value: l10n.settingsAboutThirdPartyDesc,
              onTap: () => showLicensePage(
                context: context,
                applicationName: 'ArchoeraCX',
                applicationVersion: AppInfo.version,
                applicationLegalese: 'AGPL-3.0-only',
                applicationIcon: Image.asset(
                  'assets/branding/archoera_icon_256.png',
                  width: 48,
                  height: 48,
                ),
              ),
            ),
            _AboutLinkRow(
              icon: Icons.privacy_tip_outlined,
              title: l10n.settingsAboutPrivacy,
              value: 'PRIVACY.md',
              onTap: () => _openExternal(context, l10n, _privacyUrl),
            ),
            _AboutLinkRow(
              icon: Icons.security_rounded,
              title: l10n.settingsAboutSecurity,
              value: 'SECURITY.md',
              onTap: () => _openExternal(context, l10n, _securityUrl),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.md),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xxs),
          child: Text(
            l10n.settingsAboutDisclaimer,
            style: Theme.of(context).textTheme.labelSmall
                ?.copyWith(color: tokens.textTertiary, letterSpacing: 0.2),
          ),
        ),
      ],
    );
  }

  Future<void> _openExternal(
    BuildContext context,
    AppLocalizations l10n,
    String url,
  ) async {
    var opened = false;
    try {
      opened = await launchUrl(
        Uri.parse(url),
        mode: LaunchMode.externalApplication,
      );
    } catch (_) {
      opened = false;
    }
    if (!opened && context.mounted) {
      _notify(context, l10n.settingsAboutOpenFailed, type: ToastType.error);
    }
  }
}

class _AboutSectionLabel extends StatelessWidget {
  const _AboutSectionLabel({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(left: AppSpacing.xxs),
      child: Text(
        text.toUpperCase(),
        style: Theme.of(context).textTheme.labelSmall
            ?.copyWith(letterSpacing: 1.0, color: context.tokens.textTertiary),
      ),
    );
  }
}

class _InfoChip extends StatelessWidget {
  const _InfoChip({
    required this.label,
    required this.color,
    required this.background,
  });

  final String label;
  final Color color;
  final Color background;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: 2,
      ),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(context.tokens.radiusS),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(color: color),
      ),
    );
  }
}

class _AboutEnvRow extends StatelessWidget {
  const _AboutEnvRow({
    required this.icon,
    required this.label,
    required this.value,
  });

  final IconData icon;
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: AppSpacing.xs,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: tokens.textSecondary),
          const SizedBox(width: AppSpacing.xs),
          SizedBox(
            width: 112,
            child: Text(
              label,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textSecondary),
            ),
          ),
          Expanded(
            child: SelectableText(
              value,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        ],
      ),
    );
  }
}

class _AboutLinkRow extends StatelessWidget {
  const _AboutLinkRow({
    required this.icon,
    required this.title,
    required this.value,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String value;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs),
      onTap: onTap,
      leading: Icon(icon, size: 20, color: tokens.textSecondary),
      title: Text(title, style: Theme.of(context).textTheme.bodyMedium),
      subtitle: Text(
        value,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: Theme.of(context).textTheme.bodySmall
            ?.copyWith(color: tokens.textTertiary),
      ),
      trailing: Icon(
        Icons.open_in_new_rounded,
        size: 16,
        color: tokens.textTertiary,
      ),
    );
  }
}

void _notify(
  BuildContext context,
  String message, {
  ToastType type = ToastType.info,
}) {
  toast(message, type: type);
}
