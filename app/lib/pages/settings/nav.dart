part of '../settings_page.dart';

enum _SettingsSection {
  appearance,
  launch,
  download,
  storage,
  java,
  tasks,
  notifications,
  about,
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
    AppIcons.appearance,
  ),
  _SettingsItem(
    _SettingsSection.launch,
    l10n.settingsSectionLaunch,
    AppIcons.play,
  ),
  _SettingsItem(
    _SettingsSection.storage,
    l10n.settingsStorage,
    AppIcons.folder,
  ),
  _SettingsItem(
    _SettingsSection.download,
    l10n.settingsDownload,
    AppIcons.download,
  ),
  _SettingsItem(_SettingsSection.java, l10n.settingsJava, AppIcons.java),
  _SettingsItem(
    _SettingsSection.tasks,
    l10n.settingsSectionTasks,
    AppIcons.list,
  ),
  _SettingsItem(
    _SettingsSection.notifications,
    l10n.settingsSectionNotifications,
    AppIcons.notifications,
  ),
  _SettingsItem(_SettingsSection.about, l10n.settingsAbout, AppIcons.info),
];
