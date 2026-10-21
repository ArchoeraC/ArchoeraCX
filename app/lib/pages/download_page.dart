import 'package:flutter/material.dart';

import '../widgets/common/app_card.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/download/resource_search_panel.dart';
import '../theme/tokens.dart';
import '../l10n/generated/app_localizations.dart';

class DownloadPage extends StatefulWidget {
  const DownloadPage({super.key});

  @override
  State<DownloadPage> createState() => _DownloadPageState();
}

class _DownloadPageState extends State<DownloadPage> {
  String _source = 'modrinth';

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return DefaultTabController(
      length: 6,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          AppPageHeader(
            title: l10n.downloadTitle,
            subtitle: l10n.downloadSubtitle,
          ),
          const SizedBox(height: AppSpacing.md),
          TextField(
            decoration: InputDecoration(
              hintText: l10n.searchDownload,
              prefixIcon: const Icon(Icons.search_rounded, size: 20),
            ),
          ),
          const SizedBox(height: AppSpacing.sm),
          Wrap(
            spacing: AppSpacing.xs,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              _SourceChip(
                label: l10n.sourceModrinth,
                selected: _source == 'modrinth',
                onSelected: () => setState(() => _source = 'modrinth'),
              ),
              _SourceChip(
                label: l10n.sourceCurseForge,
                selected: false,
                onSelected: () =>
                    _notify(context, l10n.sourceCurseForgeKeyHint),
              ),
              Text(
                l10n.sourceModrinth,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          TabBar(
            isScrollable: true,
            tabAlignment: TabAlignment.start,
            dividerColor: Colors.transparent,
            tabs: [
              Tab(text: l10n.tabVersions),
              Tab(text: l10n.tabLoaders),
              Tab(text: l10n.tabMods),
              Tab(text: l10n.tabPacks),
              Tab(text: l10n.tabResourcePacks),
              Tab(text: l10n.tabShaders),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          Expanded(
            child: TabBarView(
              children: [
                _VersionTab(l10n: l10n),
                _LoaderTab(l10n: l10n),
                ResourceSearchPanel(
                  kind: 'mod',
                  title: l10n.tabMods,
                  icon: Icons.extension_rounded,
                ),
                _PlaceholderTab(
                  l10n: l10n,
                  title: l10n.tabPacks,
                  icon: Icons.inventory_2_rounded,
                ),
                ResourceSearchPanel(
                  kind: 'resourcepack',
                  title: l10n.tabResourcePacks,
                  icon: Icons.photo_library_rounded,
                ),
                ResourceSearchPanel(
                  kind: 'shader',
                  title: l10n.tabShaders,
                  icon: Icons.flare_rounded,
                ),
              ],
            ),
          ),
        ],
      ),
    );
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

class _SourceChip extends StatelessWidget {
  const _SourceChip({
    required this.label,
    required this.selected,
    required this.onSelected,
  });

  final String label;
  final bool selected;
  final VoidCallback onSelected;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return ChoiceChip(
      label: Text(label),
      selected: selected,
      onSelected: (_) => onSelected(),
      showCheckmark: false,
      side: BorderSide(color: selected ? tokens.accent : tokens.border),
      selectedColor: tokens.accentSoft,
      labelStyle: Theme.of(context).textTheme.labelMedium
          ?.copyWith(color: selected ? tokens.accent : tokens.textSecondary),
    );
  }
}

class _VersionTab extends StatelessWidget {
  const _VersionTab({required this.l10n});

  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;

    return ListView.separated(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      itemCount: 4,
      separatorBuilder: (_, _) => const SizedBox(height: AppSpacing.xs),
      itemBuilder: (context, index) {
        final stable = index < 3;
        return AppCard(
          padding: const EdgeInsets.all(AppSpacing.md),
          onTap: () => _notify(context, l10n.commonCoreNotConnected),
          child: Row(
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(
                  color: (stable ? tokens.success : tokens.warning).withValues(
                    alpha: 0.14,
                  ),
                  borderRadius: BorderRadius.circular(tokens.radiusM),
                ),
                child: Icon(
                  stable ? Icons.grass_rounded : Icons.science_rounded,
                  color: stable ? tokens.success : tokens.warning,
                  size: 20,
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      stable
                          ? '1.21.${4 - index}'
                          : '26.4-snapshot-${5 - index}',
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      stable ? l10n.versionStable : l10n.versionSnapshot,
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                  ],
                ),
              ),
              Text(
                stable ? 'Java 21' : 'Java 25',
                style: Theme.of(context).textTheme.labelMedium
                    ?.copyWith(color: tokens.textTertiary),
              ),
              const SizedBox(width: AppSpacing.sm),
              Icon(Icons.chevron_right_rounded, color: tokens.textTertiary),
            ],
          ),
        );
      },
    );
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

class _LoaderTab extends StatelessWidget {
  const _LoaderTab({required this.l10n});

  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    const loaders = [
      ('Fabric', '0.16.10', Icons.auto_awesome_rounded),
      ('Quilt', '0.27.0', Icons.layers_rounded),
      ('Forge', '52.1.0', Icons.handyman_rounded),
      ('NeoForge', '21.1.209', Icons.extension_rounded),
    ];

    return ListView.separated(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      itemCount: loaders.length,
      separatorBuilder: (_, _) => const SizedBox(height: AppSpacing.xs),
      itemBuilder: (context, index) {
        final loader = loaders[index];
        return AppCard(
          padding: const EdgeInsets.all(AppSpacing.md),
          onTap: () => _notify(context, l10n.commonCoreNotConnected),
          child: Row(
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(
                  color: tokens.accentSoft,
                  borderRadius: BorderRadius.circular(tokens.radiusM),
                ),
                child: Icon(loader.$3, color: tokens.accent, size: 20),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      loader.$1,
                      style: Theme.of(context).textTheme.titleSmall,
                    ),
                    Text(
                      '${loader.$2} · 1.21.4',
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                  ],
                ),
              ),
              Icon(Icons.chevron_right_rounded, color: tokens.textTertiary),
            ],
          ),
        );
      },
    );
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

class _PlaceholderTab extends StatelessWidget {
  const _PlaceholderTab({
    required this.l10n,
    required this.title,
    required this.icon,
  });

  final AppLocalizations l10n;
  final String title;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      children: [
        AppEmptyState(
          icon: icon,
          title: title,
          message: l10n.providerNotWired,
          primaryAction: OutlinedButton(
            onPressed: null,
            child: Text(l10n.comingSoon),
          ),
        ),
      ],
    );
  }
}
