import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/app_settings.dart';
import '../services/core/java_scan.dart';
import '../stores/instances.dart';
import '../stores/settings.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_section.dart';
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

    return CustomScrollView(
      slivers: [
        SliverToBoxAdapter(
          child: AppPageHeader(
            title: l10n.settingsTitle,
            subtitle: l10n.settingsSubtitle,
          ),
        ),
        const SliverToBoxAdapter(child: SizedBox(height: AppSpacing.lg)),
        SliverToBoxAdapter(
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
                      width: 220,
                      child: _SettingsNav(
                        selected: _section,
                        onSelected: (value) => setState(() => _section = value),
                      ),
                    ),
                    const SizedBox(width: AppSpacing.md),
                    Expanded(child: content),
                  ],
                );
              }

              return Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _SettingsTabs(
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
        const SliverToBoxAdapter(child: SizedBox(height: AppSpacing.lg)),
      ],
    );
  }

  Widget _sectionContent(
    BuildContext context,
    AppLocalizations l10n,
    AppSettings? settings,
  ) {
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
            value: l10n.settingsLanguageValue,
          ),
          _SettingRow(
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
          JavaStatusCard(
            selectedPath: settings?.javaPath,
            onSelect: (runtime) => _selectJava(context, l10n, runtime),
          ),
        ],
      ),
      _SettingsSection.network => _SettingsCard(
        children: [
          _SettingRow(
            icon: Icons.wifi_rounded,
            title: 'Proxy',
            value: 'System',
          ),
          _SettingRow(
            icon: Icons.speed_rounded,
            title: 'Downloads',
            value: '16 streams',
          ),
          _SettingRow(
            icon: Icons.cloud_outlined,
            title: 'Provider',
            value: 'Modrinth',
          ),
        ],
      ),
      _SettingsSection.compliance => _SettingsCard(
        children: [
          _SettingRow(
            icon: Icons.shield_outlined,
            title: 'Download level',
            value: 'L0 · official-cn',
            color: Theme.of(context).extension<AppTokens>()?.warning,
          ),
          _SettingRow(
            icon: Icons.description_outlined,
            title: 'Consent',
            value: 'Not accepted',
          ),
        ],
      ),
      _SettingsSection.about => _SettingsCard(
        children: [
          _SettingRow(
            icon: Icons.info_outline_rounded,
            title: l10n.settingsVersion,
            value: '0.1.0+1',
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
            value: 'Open',
          ),
        ],
      ),
    };
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
                    fontWeight: item.section == selected
                        ? FontWeight.w500
                        : FontWeight.w500,
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
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final String? value;
  final Widget? trailing;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs),
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
