part of '../settings_page.dart';

enum _SettingsSection {
  appearance,
  launch,
  advanced,
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
              // The tile colour and hover highlight are ink effects: without a
              // material of their own they attach to the shell material outside
              // the route, where they are neither clipped nor torn down with
              // the page.
              child: Material(
                type: MaterialType.transparency,
                clipBehavior: Clip.hardEdge,
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
          ),
      ],
    );
  }
}

class _SettingsTabs extends StatefulWidget {
  const _SettingsTabs({required this.selected, required this.onSelected});

  final _SettingsSection selected;
  final ValueChanged<_SettingsSection> onSelected;

  @override
  State<_SettingsTabs> createState() => _SettingsTabsState();
}

class _SettingsTabsState extends State<_SettingsTabs> {
  final Map<_SettingsSection, GlobalKey> _tabKeys = {
    for (final section in _SettingsSection.values) section: GlobalKey(),
  };

  @override
  void initState() {
    super.initState();
    _ensureSelected();
  }

  @override
  void didUpdateWidget(covariant _SettingsTabs oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.selected != widget.selected) _ensureSelected();
  }

  void _ensureSelected() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      final target = _tabKeys[widget.selected]?.currentContext;
      if (target == null) return;
      Scrollable.ensureVisible(
        target,
        alignment: 0.5,
        duration: const Duration(milliseconds: 180),
        curve: Curves.easeOutCubic,
      );
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final items = _settingsItems(l10n);

    return SizedBox(
      height: 42,
      child: SingleChildScrollView(
        key: const ValueKey('settings-tabs'),
        scrollDirection: Axis.horizontal,
        child: Row(
          children: [
            for (var index = 0; index < items.length; index++) ...[
              if (index > 0) const SizedBox(width: AppSpacing.md),
              _tab(context, tokens, items[index]),
            ],
          ],
        ),
      ),
    );
  }

  Widget _tab(BuildContext context, AppTokens tokens, _SettingsItem item) {
    final active = item.section == widget.selected;
    return Material(
      key: _tabKeys[item.section],
      color: Colors.transparent,
      child: InkWell(
        onTap: () => widget.onSelected(item.section),
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
    _SettingsSection.advanced,
    l10n.settingsAdvanced,
    AppIcons.settings,
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
