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
import '../widgets/common/app_page_header.dart';
import '../widgets/common/java_status_card.dart';
import '../widgets/settings/settings_instance_roots.dart';
import '../widgets/settings/settings_nav.dart';
import '../widgets/settings/settings_panel.dart';

class SettingsPage extends ConsumerStatefulWidget {
  const SettingsPage({super.key});

  @override
  ConsumerState<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends ConsumerState<SettingsPage> {
  SettingsSection _section = SettingsSection.appearance;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final settings = ref.watch(appSettingsProvider).value;
    final entries = _settingsNavEntries(l10n);

    return CustomScrollView(
      slivers: [
        SliverToBoxAdapter(
          child: AppPageHeader(
            title: l10n.settingsTitle,
            subtitle: l10n.settingsSubtitle,
          ),
        ),
        const SliverToBoxAdapter(child: SizedBox(height: AppSpacing.md)),
        SliverToBoxAdapter(
          child: LayoutBuilder(
            builder: (context, constraints) {
              final wide = constraints.maxWidth >= 880;
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
                      width: 236,
                      child: SettingsSideNav(
                        entries: entries,
                        selected: _section,
                        headerLabel: l10n.settingsGeneral,
                        onSelected: (value) => setState(() => _section = value),
                      ),
                    ),
                    const SizedBox(width: AppSpacing.lg),
                    Expanded(
                      child: Align(
                        alignment: Alignment.topLeft,
                        child: ConstrainedBox(
                          constraints: const BoxConstraints(maxWidth: 920),
                          child: content,
                        ),
                      ),
                    ),
                  ],
                );
              }

              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  SettingsTabStrip(
                    entries: entries,
                    selected: _section,
                    onSelected: (value) => setState(() => _section = value),
                  ),
                  const SizedBox(height: AppSpacing.md),
                  content,
                ],
              );
            },
          ),
        ),
        const SliverToBoxAdapter(child: SizedBox(height: AppSpacing.xl)),
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
      SettingsSection.appearance => SettingsPanel(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SettingsRow(
              icon: Icons.palette_outlined,
              title: l10n.settingsTheme,
              value: l10n.settingsThemeSystem,
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.translate_rounded,
              title: l10n.settingsLanguage,
              value: _languageLabel(l10n, locale),
              onTap: () => _chooseLanguage(context, l10n),
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.animation_rounded,
              title: l10n.settingsMotion,
              subtitle: l10n.settingsMotionDesc,
              trailing: Switch(
                value: false,
                onChanged: (_) => _notify(context, l10n.commonCoreNotConnected),
              ),
            ),
          ],
        ),
      ),
      SettingsSection.storage => SettingsPanel(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SettingsInstanceRootsEditor(
              l10n: l10n,
              roots: settings?.roots ?? const <String>[],
              defaultRoot: settings?.instancesRoot,
              onAdd: () => _chooseInstancesRoot(context, l10n),
              onRemove: (root) => _removeInstancesRoot(context, l10n, root),
              onMakeDefault: (root) =>
                  _makeDefaultInstancesRoot(context, l10n, root),
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.storage_outlined,
              title: l10n.settingsDataRoot,
              value: settings?.dataRoot ?? '—',
            ),
          ],
        ),
      ),
      SettingsSection.java => SettingsPanel(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SettingsRow(
              icon: Icons.auto_awesome_rounded,
              title: l10n.settingsJavaAuto,
              subtitle: l10n.settingsJavaAutoHint,
              trailing: Switch(
                value: settings?.javaAuto ?? true,
                onChanged: (value) => _setJavaAuto(context, l10n, value),
              ),
            ),
            const SettingsDivider(),
            _JavaCurrentRow(
              l10n: l10n,
              javaAuto: settings?.javaAuto ?? true,
              javaPath: settings?.javaPath,
              onUseAuto: () => _setJavaAuto(context, l10n, true),
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.memory_rounded,
              title: l10n.settingsMemory,
              value: l10n.settingsMemoryValue(
                settings?.memoryMinMb ?? 512,
                settings?.memoryMaxMb ?? 2048,
              ),
              onTap: () => _editRuntimeOptions(context, l10n, settings),
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.recycling_rounded,
              title: l10n.settingsGc,
              value: _gcLabel(l10n, settings?.gcMode ?? 'auto'),
              onTap: () => _editRuntimeOptions(context, l10n, settings),
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.terminal_rounded,
              title: l10n.settingsJvmArgs,
              value: (settings?.jvmArgs?.trim().isNotEmpty ?? false)
                  ? settings!.jvmArgs!
                  : l10n.settingsJvmArgsValue,
              onTap: () => _editRuntimeOptions(context, l10n, settings),
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.sports_esports_rounded,
              title: l10n.settingsGameArgs,
              value: (settings?.gameArgs?.trim().isNotEmpty ?? false)
                  ? settings!.gameArgs!
                  : l10n.settingsGameArgsValue,
              onTap: () => _editRuntimeOptions(context, l10n, settings),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
              child: JavaStatusCard(
                autoScan: true,
                selectedPath: settings?.javaPath,
                onSelect: (runtime) => _selectJava(context, l10n, runtime),
              ),
            ),
          ],
        ),
      ),
      SettingsSection.network => SettingsPanel(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SettingsRow(
              icon: Icons.wifi_rounded,
              title: l10n.settingsProxy,
              value: l10n.settingsProxySystem,
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.speed_rounded,
              title: l10n.settingsDownloads,
              value: l10n.settingsDownloadsStreams(16),
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.cloud_outlined,
              title: l10n.settingsProvider,
              value: l10n.sourceModrinth,
            ),
          ],
        ),
      ),
      SettingsSection.compliance => SettingsPanel(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SettingsRow(
              icon: Icons.shield_outlined,
              title: l10n.settingsDownloadLevel,
              value: l10n.settingsDownloadLevelValue,
              valueColor: context.tokens.warning,
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.description_outlined,
              title: l10n.settingsConsent,
              value: l10n.settingsConsentNotAccepted,
            ),
          ],
        ),
      ),
      SettingsSection.about => SettingsPanel(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SettingsRow(
              icon: Icons.info_outline_rounded,
              title: l10n.settingsVersion,
              value: AppInfo.version,
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.gavel_rounded,
              title: l10n.settingsLicense,
              value: 'AGPL-3.0-only',
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.code_rounded,
              title: l10n.settingsSource,
              value: 'github.com/BetaStudio2/ArchoeraCX',
            ),
            const SettingsDivider(),
            SettingsRow(
              icon: Icons.health_and_safety_outlined,
              title: l10n.settingsDiagnostics,
              subtitle: l10n.settingsDiagnosticsDesc,
              value: l10n.settingsDiagnosticsOpen,
            ),
          ],
        ),
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
      _notify(context, AppLocalizations.of(context).settingsLanguageChanged);
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
      _notify(context, l10n.settingsMemoryInvalid);
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
      if (context.mounted) _notify(context, l10n.settingsRuntimeSaved);
    } catch (error) {
      if (context.mounted) _notify(context, error.toString());
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
      if (context.mounted) _notify(context, l10n.settingsSaved);
    } catch (error) {
      if (context.mounted) _notify(context, error.toString());
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
      if (context.mounted) _notify(context, l10n.settingsJavaSelected);
    } catch (error) {
      if (context.mounted) _notify(context, error.toString());
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
      if (context.mounted) _notify(context, error.toString());
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
      if (context.mounted) _notify(context, error.toString());
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
      if (context.mounted) _notify(context, error.toString());
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
    _notify(context, message.toString());
  }

  void _notify(BuildContext context, String message) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          behavior: SnackBarBehavior.floating,
          content: Text(message),
          duration: const Duration(seconds: 2),
        ),
      );
  }
}

/// The current Java row is taller than a regular row because the manual mode
/// adds an inline "use automatic" action.
class _JavaCurrentRow extends StatelessWidget {
  const _JavaCurrentRow({
    required this.l10n,
    required this.javaAuto,
    required this.javaPath,
    required this.onUseAuto,
  });

  final AppLocalizations l10n;
  final bool javaAuto;
  final String? javaPath;
  final VoidCallback onUseAuto;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final value = javaAuto ? l10n.settingsJavaAutoValue : (javaPath ?? '—');

    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.md,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.terminal_rounded, size: 18, color: tokens.textSecondary),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  l10n.settingsJavaCurrent,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodyMedium,
                ),
                const SizedBox(height: 2),
                Tooltip(
                  message: value,
                  child: Text(
                    value,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textSecondary,
                    ),
                  ),
                ),
                if (!javaAuto) ...[
                  const SizedBox(height: AppSpacing.xxs),
                  TextButton.icon(
                    onPressed: onUseAuto,
                    style: TextButton.styleFrom(
                      tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                      minimumSize: const Size(0, 30),
                      padding: const EdgeInsets.symmetric(
                        horizontal: AppSpacing.xs,
                      ),
                      visualDensity: VisualDensity.compact,
                    ),
                    icon: const Icon(Icons.auto_awesome_rounded, size: 16),
                    label: Text(l10n.settingsJavaUseAuto),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

List<SettingsNavEntry> _settingsNavEntries(AppLocalizations l10n) => [
  SettingsNavEntry(
    section: SettingsSection.appearance,
    label: l10n.settingsAppearance,
    icon: Icons.palette_outlined,
  ),
  SettingsNavEntry(
    section: SettingsSection.storage,
    label: l10n.settingsStorage,
    icon: Icons.folder_outlined,
  ),
  SettingsNavEntry(
    section: SettingsSection.java,
    label: l10n.settingsJava,
    icon: Icons.coffee_rounded,
  ),
  SettingsNavEntry(
    section: SettingsSection.network,
    label: l10n.settingsNetwork,
    icon: Icons.wifi_rounded,
  ),
  SettingsNavEntry(
    section: SettingsSection.compliance,
    label: l10n.settingsCompliance,
    icon: Icons.shield_outlined,
  ),
  SettingsNavEntry(
    section: SettingsSection.about,
    label: l10n.settingsAbout,
    icon: Icons.info_outline_rounded,
  ),
];
