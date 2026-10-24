import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../app/shell.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/game_instance.dart';
import '../services/core/downloads/build_policy.dart';
import '../services/core/launch/launch_action.dart';
import '../stores/instances.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../utils/last_played.dart';
import '../widgets/common/app_panel.dart';
import '../widgets/common/loader_mark.dart';
import '../widgets/instances/create_instance_dialog.dart';

part 'dashboard/panels.dart';

class DashboardPage extends ConsumerWidget {
  const DashboardPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final instancesAsync = ref.watch(instancesProvider);
    final instances = instancesAsync.value ?? const <GameInstance>[];
    final selected = ref.watch(selectedInstanceProvider);
    final downloadsEnabled = ref.watch(downloadEnabledProvider);

    return SingleChildScrollView(
      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _LaunchHero(
            l10n: l10n,
            instance: selected,
            onLaunch: () {
              if (selected == null) return;
              ref.read(selectedInstanceIdProvider.notifier).select(selected.id);
              launchInstance(context, ref, selected);
            },
            onBrowse: () => openAppBranch(
              context,
              AppBranch.instances,
              fallbackPath: '/instances',
            ),
            onCreate: () => showCreateInstanceDialog(context),
          ),
          const SizedBox(height: AppSpacing.sm),
          _ShortcutStrip(
            l10n: l10n,
            instanceCount: instances.length,
            onInstances: () => openAppBranch(
              context,
              AppBranch.instances,
              fallbackPath: '/instances',
            ),
            onDownload: downloadsEnabled
                ? () => openAppBranch(
                    context,
                    AppBranch.download,
                    fallbackPath: '/download',
                  )
                : null,
          ),
          const SizedBox(height: AppSpacing.lg),
          _RecentInstancesSection(
            l10n: l10n,
            instances: instances,
            selectedId: selected?.id,
            loading: instancesAsync.isLoading && instances.isEmpty,
            onBrowse: () => openAppBranch(
              context,
              AppBranch.instances,
              fallbackPath: '/instances',
            ),
            onSelect: (instance) {
              ref.read(selectedInstanceIdProvider.notifier).select(instance.id);
            },
          ),
          const SizedBox(height: AppSpacing.xl),
          Text(
            l10n.homeFootnote,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.textTertiary),
          ),
        ],
      ),
    );
  }
}
