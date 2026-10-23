import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/system/system_status.dart';
import '../services/core/wiki/wiki_feed.dart';
import '../stores/wiki.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../utils/format.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_panel.dart';
import '../widgets/common/safe_url.dart';
import '../widgets/common/toast.dart';

part 'wiki/system_load.dart';
part 'wiki/feed.dart';

/// Community page: what happened around Minecraft, plus what this machine is
/// doing right now.
///
/// Both halves come from C# (`wiki.feed`, `system.status`); the page only lays
/// out already-normalized data and never fetches or polls on its own. Refresh
/// is explicit — no timers, in line with the wake-driven core contract.
class WikiPage extends ConsumerWidget {
  const WikiPage({super.key});

  static const newsSource = 'mojang-news';
  static const javaSource = 'mojang-java';
  static const wikiSource = 'minecraft-wiki';

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final feed = ref.watch(wikiFeedProvider);
    final status = ref.watch(systemStatusProvider);
    final busy = feed.isLoading || status.isLoading;

    return SingleChildScrollView(
      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppPageHeader(
            title: l10n.navWiki,
            subtitle: l10n.wikiSubtitle,
            actions: [
              OutlinedButton.icon(
                onPressed: busy ? null : () => _refresh(ref),
                icon: busy
                    ? const SizedBox(
                        width: 14,
                        height: 14,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(AppIcons.refresh, size: 18),
                label: Text(l10n.wikiRefresh),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
          _SystemLoadPanel(status: status),
          const SizedBox(height: AppSpacing.xl),
          _FeedPanel(
            feed: feed,
            onRetry: () => ref.read(wikiFeedProvider.notifier).refresh(),
          ),
        ],
      ),
    );
  }

  void _refresh(WidgetRef ref) {
    ref.read(wikiFeedProvider.notifier).refresh();
    ref.read(systemStatusProvider.notifier).refresh();
  }
}
