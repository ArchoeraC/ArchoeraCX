part of '../instances_page.dart';

/// Shared menu entry: value + icon + label. All ten items previously repeated
/// the same Row(Icon + gap + Flexible(Text)) wrapper.
PopupMenuItem<String> _popupItem(String value, Widget icon, String label) {
  return PopupMenuItem<String>(
    value: value,
    child: Row(
      children: [
        icon,
        const SizedBox(width: AppSpacing.xs),
        Flexible(child: Text(label, overflow: TextOverflow.ellipsis)),
      ],
    ),
  );
}

class _InstanceMenu extends StatelessWidget {
  const _InstanceMenu({
    required this.l10n,
    required this.downloadsEnabled,
    required this.onCheck,
    required this.onManageLoader,
    required this.onSettings,
    required this.onInstallGame,
    required this.onManageMods,
    required this.onDuplicate,
    required this.onViewLogs,
    required this.onOpenFolder,
    required this.onDelete,
  });

  final AppLocalizations l10n;
  final bool downloadsEnabled;
  final ValueChanged<String> onCheck;
  final VoidCallback onManageLoader;
  final VoidCallback onSettings;
  final VoidCallback onInstallGame;
  final VoidCallback onManageMods;
  final VoidCallback onDuplicate;
  final VoidCallback onViewLogs;
  final VoidCallback onOpenFolder;
  final VoidCallback onDelete;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return PopupMenuButton<String>(
      tooltip: l10n.instanceManage,
      icon: Icon(AppIcons.more, color: tokens.textSecondary),
      onSelected: (value) {
        if (value == 'delete') onDelete();
        if (value == 'manageLoader') onManageLoader();
        if (value == 'settings') onSettings();
        if (value == 'installGame') onInstallGame();
        if (value == 'manageMods') onManageMods();
        if (value == 'duplicate') onDuplicate();
        if (value == 'viewLogs') onViewLogs();
        if (value == 'openFolder') onOpenFolder();
        if (value == 'checkQuick') onCheck('quick');
        if (value == 'checkFull') onCheck('full');
      },
      itemBuilder: (context) => [
        if (downloadsEnabled) ...[
          _popupItem(
            'manageLoader',
            const Icon(AppIcons.repair, size: 20),
            l10n.instanceManageLoader,
          ),
          _popupItem(
            'installGame',
            const Icon(AppIcons.download, size: 20),
            l10n.instanceInstallGame,
          ),
        ],
        _popupItem(
          'settings',
          const Icon(AppIcons.settings, size: 20),
          l10n.instanceSettingsTitle,
        ),
        _popupItem(
          'duplicate',
          const Icon(AppIcons.copy, size: 20),
          l10n.instanceDuplicate,
        ),
        _popupItem(
          'manageMods',
          const Icon(AppIcons.mod, size: 20),
          l10n.instanceManageMods,
        ),
        _popupItem(
          'viewLogs',
          const Icon(AppIcons.news, size: 20),
          l10n.instanceViewLogs,
        ),
        _popupItem(
          'openFolder',
          const Icon(AppIcons.folder, size: 20),
          l10n.instanceOpenFolder,
        ),
        _popupItem(
          'checkQuick',
          const Icon(AppIcons.quick, size: 20),
          l10n.instanceCheckQuick,
        ),
        _popupItem(
          'checkFull',
          const Icon(AppIcons.checklist, size: 20),
          l10n.instanceCheckFull,
        ),
        _popupItem(
          'delete',
          Icon(AppIcons.delete, color: tokens.danger),
          l10n.instanceDelete,
        ),
      ],
    );
  }
}
