import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../widgets/common/app_card.dart';
import '../widgets/common/app_page_header.dart';
import '../theme/tokens.dart';
import '../l10n/generated/app_localizations.dart';
import '../stores/settings.dart';

class SettingsPage extends ConsumerWidget {
  const SettingsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final settings = ref.watch(appSettingsProvider).value;

    return SingleChildScrollView(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AppPageHeader(
            title: l10n.settingsTitle,
            subtitle: l10n.settingsSubtitle,
          ),
          const SizedBox(height: AppSpacing.lg),
          _Section(
            title: l10n.settingsAppearance,
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
                  onChanged: (_) =>
                      _notify(context, l10n.commonCoreNotConnected),
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _Section(
            title: l10n.settingsStorage,
            children: [
              _SettingRow(
                icon: Icons.folder_outlined,
                title: l10n.settingsInstancesRoot,
                value: settings?.instancesRoot ?? '—',
                onTap: () => _chooseInstancesRoot(context, ref, l10n),
              ),
              _SettingRow(
                icon: Icons.storage_outlined,
                title: l10n.settingsDataRoot,
                value: settings?.dataRoot ?? '—',
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _Section(
            title: l10n.settingsJava,
            children: [
              _SettingRow(
                icon: Icons.coffee_rounded,
                title: 'Java 21',
                value: 'Auto-detect',
              ),
              _SettingRow(
                icon: Icons.memory_rounded,
                title: 'Memory',
                value: '512 – 4096 MB',
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _Section(
            title: l10n.settingsNetwork,
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
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _Section(
            title: l10n.settingsCompliance,
            children: [
              _SettingRow(
                icon: Icons.shield_outlined,
                title: 'Download level',
                value: 'L0 · official-cn',
                color: tokens.warning,
              ),
              _SettingRow(
                icon: Icons.description_outlined,
                title: 'Consent',
                value: 'Not accepted',
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.md),
          _Section(
            title: l10n.settingsAbout,
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
                value: 'github.com/…',
              ),
              _SettingRow(
                icon: Icons.health_and_safety_outlined,
                title: l10n.settingsDiagnostics,
                subtitle: l10n.settingsDiagnosticsDesc,
                value: 'Open',
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
          Text(
            l10n.commonMockData,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.textTertiary),
          ),
        ],
      ),
    );
  }

  Future<void> _chooseInstancesRoot(
    BuildContext context,
    WidgetRef ref,
    AppLocalizations l10n,
  ) async {
    final selected = await getDirectoryPath(
      confirmButtonText: l10n.settingsChooseFolder,
    );
    if (selected == null || !context.mounted) return;

    try {
      await ref.read(appSettingsProvider.notifier).setInstancesRoot(selected);
      if (context.mounted) _notify(context, l10n.settingsSaved);
    } catch (error) {
      if (context.mounted) _notify(context, error.toString());
    }
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

class _Section extends StatelessWidget {
  const _Section({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    return AppCard(
      padding: const EdgeInsets.all(AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.only(bottom: AppSpacing.xs),
            child: Text(title, style: Theme.of(context).textTheme.titleMedium),
          ),
          ...children,
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
