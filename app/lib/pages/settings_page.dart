import 'dart:async';

import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/app_settings.dart';
import '../services/core/java_scan.dart';
import '../services/core/system_memory.dart';
import '../stores/instances.dart';
import '../stores/locale.dart';
import '../stores/providers.dart';
import '../stores/settings.dart';
import '../theme/tokens.dart';
import '../utils/app_info.dart';
import '../widgets/common/app_section.dart';
import '../widgets/common/toast.dart';
import '../widgets/common/java_status_card.dart';
import '../widgets/common/app_page_header.dart';

enum _SettingsSection { appearance, storage, java, network, compliance, about }

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
    return switch (_section) {
      _SettingsSection.appearance => _SettingsCard(
        children: [
          _SettingRow(
            icon: Icons.palette_outlined,
            title: l10n.settingsTheme,
            value: l10n.settingsThemeSystem,
          ),
          _SettingRow(
            icon: Icons.translate_rounded,
            title: l10n.settingsLanguage,
            value: _languageLabel(l10n, locale),
            onTap: () => _chooseLanguage(context, l10n),
          ),
          _SettingRow(
            icon: Icons.animation_rounded,
            title: l10n.settingsMotion,
            subtitle: l10n.settingsMotionDesc,
            trailing: Switch(
              value: false,
              onChanged: (_) => _notify(
                context,
                l10n.commonCoreNotConnected,
                type: ToastType.error,
              ),
            ),
          ),
        ],
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
          _RuntimeSettingsPanel(settings: settings),
          const SizedBox(height: AppSpacing.md),
          JavaStatusCard(
            autoScan: true,
            selectedPath: settings?.javaPath,
            onSelect: (runtime) => _selectJava(context, l10n, runtime),
          ),
        ],
      ),
      _SettingsSection.network => _SettingsCard(
        children: [
          _SettingRow(
            icon: Icons.wifi_rounded,
            title: l10n.settingsProxy,
            value: l10n.settingsProxySystem,
          ),
          _SettingRow(
            icon: Icons.speed_rounded,
            title: l10n.settingsDownloads,
            value: l10n.settingsDownloadsStreams(16),
          ),
          _SettingRow(
            icon: Icons.cloud_outlined,
            title: l10n.settingsProvider,
            value: l10n.sourceModrinth,
          ),
        ],
      ),
      _SettingsSection.compliance => _SettingsCard(
        children: [
          _SettingRow(
            icon: Icons.shield_outlined,
            title: l10n.settingsDownloadLevel,
            value: l10n.settingsDownloadLevelValue,
            color: Theme.of(context).extension<AppTokens>()?.warning,
          ),
          _SettingRow(
            icon: Icons.description_outlined,
            title: l10n.settingsConsent,
            value: l10n.settingsConsentNotAccepted,
          ),
        ],
      ),
      _SettingsSection.about => _SettingsCard(
        children: [
          _SettingRow(
            icon: Icons.info_outline_rounded,
            title: l10n.settingsVersion,
            value: AppInfo.version,
          ),
          _SettingRow(
            icon: Icons.gavel_rounded,
            title: l10n.settingsLicense,
            value: 'AGPL-3.0-only',
          ),
          _SettingRow(
            icon: Icons.code_rounded,
            title: l10n.settingsSource,
            value: 'github.com/BetaStudio2/ArchoeraCX',
          ),
          _SettingRow(
            icon: Icons.health_and_safety_outlined,
            title: l10n.settingsDiagnostics,
            subtitle: l10n.settingsDiagnosticsDesc,
            value: l10n.settingsDiagnosticsOpen,
          ),
        ],
      ),
    };
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

  Future<void> _selectJava(
    BuildContext context,
    AppLocalizations l10n,
    JavaRuntime runtime,
  ) async {
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setJavaSelection(javaPath: runtime.path, javaAuto: false);
      if (context.mounted) {
        _notify(context, l10n.settingsJavaSelected, type: ToastType.success);
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
    _SettingsSection.network,
    l10n.settingsNetwork,
    Icons.wifi_rounded,
  ),
  _SettingsItem(
    _SettingsSection.compliance,
    l10n.settingsCompliance,
    Icons.shield_outlined,
  ),
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
    this.color,
    this.onTap,
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final String? value;
  final Widget? trailing;
  final Color? color;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs),
      onTap: onTap,
      leading: Icon(icon, size: 20, color: color ?? tokens.textSecondary),
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
                        ?.copyWith(color: color ?? tokens.textTertiary),
                  ),
                )),
    );
  }
}
