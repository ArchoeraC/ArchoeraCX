part of '../settings_page.dart';

/// Linux-only convenience switches for common wrapper/environment presets.
///
/// The native owner applies these while building the launch plan; a custom
/// wrapper command or an environment entry with the same effect wins. They do
/// not touch downloads or the compliance developer mode.
class _PerformanceSettingsPanel extends ConsumerWidget {
  const _PerformanceSettingsPanel({this.settings});

  final AppSettings? settings;

  Future<void> _set(
    BuildContext context,
    WidgetRef ref,
    Future<void> Function() update,
  ) async {
    try {
      await update();
    } catch (error) {
      if (!context.mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final current = settings;

    return _SettingsGroup(
      title: l10n.settingsSectionPerformance,
      note: l10n.settingsPerformanceNote,
      children: [
        _AppearanceTile(
          icon: AppIcons.quick,
          title: l10n.settingsGameMode,
          subtitle: l10n.settingsGameModeDesc,
          trailing: Switch(
            key: const ValueKey('settings-gamemode'),
            value: current?.enableGameMode ?? false,
            onChanged: (value) => _set(
              context,
              ref,
              () => ref
                  .read(appSettingsProvider.notifier)
                  .setRuntimeOptions(enableGameMode: value),
            ),
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.throughput,
          title: l10n.settingsMangoHud,
          subtitle: l10n.settingsMangoHudDesc,
          trailing: Switch(
            key: const ValueKey('settings-mangohud'),
            value: current?.enableMangoHud ?? false,
            onChanged: (value) => _set(
              context,
              ref,
              () => ref
                  .read(appSettingsProvider.notifier)
                  .setRuntimeOptions(enableMangoHud: value),
            ),
          ),
        ),
        _AppearanceTile(
          icon: AppIcons.shader,
          title: l10n.settingsZink,
          subtitle: l10n.settingsZinkDesc,
          trailing: Switch(
            key: const ValueKey('settings-zink'),
            value: current?.useZink ?? false,
            onChanged: (value) => _set(
              context,
              ref,
              () => ref
                  .read(appSettingsProvider.notifier)
                  .setRuntimeOptions(useZink: value),
            ),
          ),
        ),
      ],
    );
  }
}
