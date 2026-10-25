part of '../instances_page.dart';

/// Instance action menu.
///
/// The same entries back the "more" button and the row/card context menu.
/// Keeping one builder and one dispatcher means a secondary click cannot drift
/// from the button, and adding an action only touches this file.
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
    return AppMenuButton<String>(
      tooltip: l10n.instanceManage,
      icon: AppIcons.more,
      items: _items(),
      onSelected: _dispatch,
    );
  }

  /// Opens the identical menu at a pointer position for a secondary click.
  Future<void> showAt(BuildContext context, Offset globalPosition) async {
    final value = await showContextMenuAt<String>(
      context,
      globalPosition: globalPosition,
      items: appMenuEntries(context, _items()),
    );
    if (value != null) _dispatch(value);
  }

  List<AppMenuEntry<String>> _items() {
    return [
      if (downloadsEnabled) ...[
        AppMenuEntry(
          value: 'manageLoader',
          label: l10n.instanceManageLoader,
          icon: AppIcons.repair,
        ),
        AppMenuEntry(
          value: 'installGame',
          label: l10n.instanceInstallGame,
          icon: AppIcons.download,
        ),
      ],
      AppMenuEntry(
        value: 'settings',
        label: l10n.instanceSettingsTitle,
        icon: AppIcons.settings,
      ),
      AppMenuEntry(
        value: 'duplicate',
        label: l10n.instanceDuplicate,
        icon: AppIcons.copy,
      ),
      AppMenuEntry(
        value: 'manageMods',
        label: l10n.instanceManageMods,
        icon: AppIcons.mod,
      ),
      AppMenuEntry(
        value: 'viewLogs',
        label: l10n.instanceViewLogs,
        icon: AppIcons.news,
      ),
      AppMenuEntry(
        value: 'openFolder',
        label: l10n.instanceOpenFolder,
        icon: AppIcons.folder,
      ),
      AppMenuEntry(
        value: 'checkQuick',
        label: l10n.instanceCheckQuick,
        icon: AppIcons.quick,
      ),
      AppMenuEntry(
        value: 'checkFull',
        label: l10n.instanceCheckFull,
        icon: AppIcons.checklist,
      ),
      AppMenuEntry(
        value: 'delete',
        label: l10n.instanceDelete,
        icon: AppIcons.delete,
        danger: true,
      ),
    ];
  }

  void _dispatch(String value) {
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
  }
}
