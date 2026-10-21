import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/app_settings.dart';
import '../services/core/java_scan.dart';
import '../stores/instances.dart';
import '../stores/locale.dart';
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
              final content = AnimatedSwitcher(
                duration: const Duration(milliseconds: 180),
                transitionBuilder: (child, animation) => FadeTransition(
                  opacity: animation,
                  child: SlideTransition(
                    position: Tween<Offset>(
                      begin: const Offset(0, 0.015),
                      end: Offset.zero,
                    ).animate(animation),
                    child: child,
                  ),
                ),
                child: KeyedSubtree(
                  key: ValueKey(_section),
                  child: _sectionContent(context, l10n, settings),
                ),
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
          _SettingsCard(
            children: [
              _SettingRow(
                icon: Icons.memory_rounded,
                title: l10n.settingsMemory,
                value: l10n.settingsMemoryValue(
                  settings?.memoryMinMb ?? 512,
                  settings?.memoryMaxMb ?? 2048,
                ),
                onTap: () => _editRuntimeOptions(context, l10n, settings),
              ),
              _SettingRow(
                icon: Icons.recycling_rounded,
                title: l10n.settingsGc,
                value: _gcLabel(l10n, settings?.gcMode ?? 'auto'),
                onTap: () => _editRuntimeOptions(context, l10n, settings),
              ),
              _SettingRow(
                icon: Icons.terminal_rounded,
                title: l10n.settingsJvmArgs,
                value: (settings?.jvmArgs?.trim().isNotEmpty ?? false)
                    ? settings!.jvmArgs!
                    : l10n.settingsJvmArgsValue,
                onTap: () => _editRuntimeOptions(context, l10n, settings),
              ),
              _SettingRow(
                icon: Icons.sports_esports_rounded,
                title: l10n.settingsGameArgs,
                value: (settings?.gameArgs?.trim().isNotEmpty ?? false)
                    ? settings!.gameArgs!
                    : l10n.settingsGameArgsValue,
                onTap: () => _editRuntimeOptions(context, l10n, settings),
              ),
            ],
          ),
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

  Future<void> _editRuntimeOptions(
    BuildContext context,
    AppLocalizations l10n,
    AppSettings? settings,
  ) async {
    final min = TextEditingController(text: '${settings?.memoryMinMb ?? 512}');
    final max = TextEditingController(text: '${settings?.memoryMaxMb ?? 2048}');
    final args = TextEditingController(text: settings?.jvmArgs ?? '');
    final gameArgs = TextEditingController(text: settings?.gameArgs ?? '');
    var gcMode = settings?.gcMode ?? 'auto';
    final saved = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(l10n.settingsRuntimeTitle),
        content: SizedBox(
          width: 420,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                l10n.settingsRuntimeHint,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: context.tokens.textSecondary),
              ),
              const SizedBox(height: AppSpacing.md),
              TextField(
                controller: min,
                keyboardType: TextInputType.number,
                decoration: InputDecoration(labelText: l10n.settingsMemoryMin),
              ),
              const SizedBox(height: AppSpacing.sm),
              TextField(
                controller: max,
                keyboardType: TextInputType.number,
                decoration: InputDecoration(labelText: l10n.settingsMemoryMax),
              ),
              const SizedBox(height: AppSpacing.sm),
              TextField(
                controller: args,
                decoration: InputDecoration(
                  labelText: l10n.settingsJvmArgs,
                  hintText: l10n.settingsJvmArgsHint,
                ),
              ),
              const SizedBox(height: AppSpacing.sm),
              DropdownButtonFormField<String>(
                initialValue: gcMode,
                decoration: InputDecoration(labelText: l10n.settingsGc),
                items: [
                  for (final item in _gcOptions(l10n))
                    DropdownMenuItem(value: item.$1, child: Text(item.$2)),
                ],
                onChanged: (value) {
                  if (value != null) gcMode = value;
                },
              ),
              const SizedBox(height: AppSpacing.sm),
              TextField(
                controller: gameArgs,
                decoration: InputDecoration(
                  labelText: l10n.settingsGameArgs,
                  hintText: l10n.settingsGameArgsHint,
                ),
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(l10n.commonCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(l10n.commonSave),
          ),
        ],
      ),
    );
    final minValue = int.tryParse(min.text.trim());
    final maxValue = int.tryParse(max.text.trim());
    final jvmArgsText = args.text.trim();
    final gameArgsText = gameArgs.text.trim();
    min.dispose();
    max.dispose();
    args.dispose();
    gameArgs.dispose();
    if (saved != true || !context.mounted) return;
    if (minValue == null ||
        maxValue == null ||
        minValue < 128 ||
        maxValue > 65536 ||
        minValue > maxValue) {
      _notify(context, l10n.settingsMemoryInvalid, type: ToastType.error);
      return;
    }

    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setRuntimeOptions(
            memoryMinMb: minValue,
            memoryMaxMb: maxValue,
            jvmArgs: jvmArgsText,
            gcMode: gcMode,
            gameArgs: gameArgsText,
          );
      if (context.mounted) {
        _notify(context, l10n.settingsRuntimeSaved, type: ToastType.success);
      }
    } catch (error) {
      if (context.mounted) {
        _notify(context, error.toString(), type: ToastType.error);
      }
    }
  }

  String _gcLabel(AppLocalizations l10n, String mode) => switch (mode) {
    'g1gc' => l10n.settingsGcG1,
    'zgc' => l10n.settingsGcZgc,
    'parallel' => l10n.settingsGcParallel,
    'serial' => l10n.settingsGcSerial,
    'shenandoah' => l10n.settingsGcShenandoah,
    'epsilon' => l10n.settingsGcEpsilon,
    _ => l10n.settingsGcAuto,
  };

  List<(String, String)> _gcOptions(AppLocalizations l10n) => [
    ('auto', l10n.settingsGcAuto),
    ('g1gc', l10n.settingsGcG1),
    ('zgc', l10n.settingsGcZgc),
    ('parallel', l10n.settingsGcParallel),
    ('serial', l10n.settingsGcSerial),
    ('shenandoah', l10n.settingsGcShenandoah),
    ('epsilon', l10n.settingsGcEpsilon),
  ];

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
              : Text(
                  value!,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: color ?? tokens.textTertiary),
                )),
    );
  }
}
