part of '../settings_page.dart';

/// Task center history length and the log viewer's default tail size.
class _TasksSettingsPanel extends ConsumerWidget {
  const _TasksSettingsPanel();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final prefs = ref.watch(preferencesProvider);
    final settings = ref.watch(appSettingsProvider).value;
    final notifier = ref.read(preferencesProvider.notifier);

    return _SettingsGroup(
      title: l10n.settingsSectionTasks,
      note: l10n.settingsTasksNote,
      children: [
        _AppearanceTile(
          icon: AppIcons.list,
          title: l10n.settingsTaskHistory,
          subtitle: l10n.settingsTaskHistoryDesc,
          trailing: _SegmentedControl<int>(
            selected: prefs.taskHistoryLimit,
            options: const [
              _SegmentedOption(6, '6'),
              _SegmentedOption(12, '12'),
              _SegmentedOption(24, '24'),
              _SegmentedOption(40, '40'),
            ],
            onChanged: notifier.setTaskHistoryLimit,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.terminal,
          title: l10n.settingsLogLines,
          subtitle: l10n.settingsLogLinesDesc,
          trailing: _SegmentedControl<int>(
            selected: prefs.logLineLimit,
            options: const [
              _SegmentedOption(200, '200'),
              _SegmentedOption(400, '400'),
              _SegmentedOption(800, '800'),
              _SegmentedOption(1200, '1200'),
            ],
            onChanged: notifier.setLogLineLimit,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.storage,
          title: l10n.settingsLogRetentionFiles,
          subtitle: l10n.settingsLogRetentionFilesDesc,
          trailing: _SegmentedControl<int>(
            selected: settings?.logRetentionFiles ?? 10,
            options: const [
              _SegmentedOption(5, '5'),
              _SegmentedOption(10, '10'),
              _SegmentedOption(20, '20'),
            ],
            onChanged: (value) => ref
                .read(appSettingsProvider.notifier)
                .setRuntimeOptions(logRetentionFiles: value),
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.storage,
          title: l10n.settingsLogRetentionTotal,
          subtitle: l10n.settingsLogRetentionTotalDesc,
          trailing: _SegmentedControl<int>(
            selected: settings?.logRetentionMegabytes ?? 32,
            options: const [
              _SegmentedOption(16, '16 MiB'),
              _SegmentedOption(32, '32 MiB'),
              _SegmentedOption(64, '64 MiB'),
            ],
            onChanged: (value) => ref
                .read(appSettingsProvider.notifier)
                .setRuntimeOptions(logRetentionMegabytes: value),
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.delete,
          title: l10n.settingsClearLogs,
          subtitle: l10n.settingsClearLogsDesc,
          trailing: const Icon(AppIcons.chevronRight, size: 18),
          onTap: () => _clearAllLaunchLogs(context, ref, l10n),
        ),
      ],
    );
  }
}

/// Desktop notifications the shell can raise without a native module change.
class _NotificationsSettingsPanel extends ConsumerWidget {
  const _NotificationsSettingsPanel();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final prefs = ref.watch(preferencesProvider);
    final notifier = ref.read(preferencesProvider.notifier);

    return _SettingsGroup(
      title: l10n.settingsSectionNotifications,
      note: l10n.settingsNotificationsNote,
      children: [
        _AppearanceTile(
          icon: AppIcons.notifications,
          title: l10n.settingsNotifyDesktop,
          subtitle: l10n.settingsNotifyDesktopDesc,
          trailing: Switch(
            value: prefs.desktopNotifications,
            onChanged: notifier.setDesktopNotifications,
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.download,
          title: l10n.settingsNotifyDownloads,
          subtitle: l10n.settingsNotifyDownloadsDesc,
          trailing: Switch(
            value: prefs.downloadNotifications,
            onChanged: notifier.setDownloadNotifications,
          ),
        ),
      ],
    );
  }
}

/// Deletes archived launch logs of every instance; a running game keeps its
/// active log so the viewer can keep following.
Future<void> _clearAllLaunchLogs(
  BuildContext context,
  WidgetRef ref,
  AppLocalizations l10n,
) async {
  final client = ref.read(coreClientProvider);
  if (client == null) {
    toast(l10n.commonCoreNotConnected, type: ToastType.error);
    return;
  }
  final confirmed = await confirmDestructive(
    context,
    l10n,
    title: l10n.gameLogClearTitle,
    body: l10n.settingsClearLogsBody,
    confirmLabel: l10n.commonDelete,
  );
  if (confirmed != true || !context.mounted) return;

  try {
    final result = await client.clearLaunchLogs(all: true);
    if (!context.mounted) return;
    toast(
      l10n.gameLogClearDone(result.deletedFiles, formatBytes(result.freedBytes)),
      type: ToastType.success,
    );
  } catch (error) {
    if (!context.mounted) return;
    toast('$error', type: ToastType.error);
  }
}

/// Deletes stale `.part` files of interrupted downloads.
///
/// Only files untouched for a few days are removed, so in-flight transfers
/// (which keep their `.part` fresh) are never touched.
Future<void> _cleanPartialDownloads(
  BuildContext context,
  WidgetRef ref,
  AppLocalizations l10n,
) async {
  final client = ref.read(coreClientProvider);
  if (client == null) {
    toast(l10n.commonCoreNotConnected, type: ToastType.error);
    return;
  }
  final confirmed = await confirmDestructive(
    context,
    l10n,
    title: l10n.settingsCleanPartials,
    body: l10n.settingsCleanPartialsBody,
    confirmLabel: l10n.commonDelete,
  );
  if (confirmed != true || !context.mounted) return;

  try {
    final result = await client.cleanPartialDownloads();
    if (!context.mounted) return;
    if (result.deletedFiles == 0) {
      toast(l10n.settingsCleanPartialsNone, type: ToastType.info);
      return;
    }
    toast(
      l10n.settingsCleanPartialsDone(
        result.deletedFiles,
        formatBytes(result.freedBytes),
      ),
      type: ToastType.success,
    );
  } catch (error) {
    if (!context.mounted) return;
    toast('$error', type: ToastType.error);
  }
}
