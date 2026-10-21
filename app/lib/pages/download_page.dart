import 'package:flutter/material.dart';

import '../l10n/generated/app_localizations.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/download/resource_search_panel.dart';

/// Download hub.
///
/// Two independent levels:
/// 1. provider (only Modrinth is wired today),
/// 2. content category (versions, loaders, mods, modpacks, resource packs,
///    shaders).
class DownloadPage extends StatefulWidget {
  const DownloadPage({super.key});

  @override
  State<DownloadPage> createState() => _DownloadPageState();
}

class _DownloadPageState extends State<DownloadPage> {
  /// Kept explicit so a second provider can be enabled without reshaping
  /// the page; CurseForge is visible but disabled until its key flow exists.
  String _source = 'modrinth';

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return DefaultTabController(
      length: 6,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppPageHeader(
            title: l10n.downloadTitle,
            subtitle: l10n.downloadSubtitle,
          ),
          const SizedBox(height: AppSpacing.sm),
          _SourceSelector(
            selected: _source,
            onSelected: (source) => setState(() => _source = source),
          ),
          const SizedBox(height: AppSpacing.xs),
          _CategoryTabs(l10n: l10n),
          const SizedBox(height: AppSpacing.sm),
          Expanded(
            child: TabBarView(
              children: [
                _ComingSoonTab(l10n: l10n, icon: Icons.memory_rounded),
                _ComingSoonTab(l10n: l10n, icon: Icons.layers_rounded),
                ResourceSearchPanel(
                  kind: 'mod',
                  title: l10n.tabMods,
                  icon: Icons.extension_rounded,
                ),
                _ComingSoonTab(l10n: l10n, icon: Icons.inventory_2_rounded),
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
}

/// Provider row. Rendered as a compact toolbar so it never competes with the
/// category tabs underneath it.
class _SourceSelector extends StatelessWidget {
  const _SourceSelector({required this.selected, required this.onSelected});

  final String selected;
  final ValueChanged<String> onSelected;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xxs),
      child: Wrap(
        spacing: AppSpacing.xs,
        runSpacing: AppSpacing.xxs,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.public_rounded, size: 15, color: tokens.textTertiary),
              const SizedBox(width: AppSpacing.xxs),
              Text(
                l10n.sourceLabel,
                style: Theme.of(context).textTheme.labelMedium
                    ?.copyWith(color: tokens.textSecondary),
              ),
            ],
          ),
          _SourceChip(
            label: l10n.sourceModrinth,
            selected: selected == 'modrinth',
            onSelected: () => onSelected('modrinth'),
          ),
          _SourceChip(
            label: l10n.sourceCurseForge,
            selected: selected == 'curseforge',
            tooltip: l10n.sourceCurseForgeKeyHint,
          ),
        ],
      ),
    );
  }
}

class _SourceChip extends StatelessWidget {
  const _SourceChip({
    required this.label,
    required this.selected,
    this.onSelected,
    this.tooltip,
  });

  final String label;
  final bool selected;

  /// `null` renders the chip as unavailable.
  final VoidCallback? onSelected;
  final String? tooltip;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final enabled = onSelected != null;

    final chip = Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onSelected,
        borderRadius: BorderRadius.circular(tokens.radiusS),
        child: AnimatedContainer(
          duration: tokens.fast,
          curve: tokens.curve,
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm,
            vertical: AppSpacing.xs,
          ),
          decoration: BoxDecoration(
            color: selected
                ? tokens.accentSoft
                : (enabled ? tokens.surface2 : Colors.transparent),
            borderRadius: BorderRadius.circular(tokens.radiusS),
            border: Border.all(
              color: selected
                  ? tokens.accent
                  : (enabled ? Colors.transparent : tokens.border),
            ),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (!enabled) ...[
                Icon(
                  Icons.lock_outline_rounded,
                  size: 14,
                  color: tokens.textTertiary,
                ),
                const SizedBox(width: AppSpacing.xxs),
              ],
              Text(
                label,
                style: textTheme.labelMedium?.copyWith(
                  color: selected
                      ? tokens.accent
                      : (enabled ? tokens.textSecondary : tokens.textTertiary),
                  fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
                ),
              ),
            ],
          ),
        ),
      ),
    );

    if (tooltip == null) return chip;
    return Tooltip(
      message: tooltip!,
      triggerMode: TooltipTriggerMode.tap,
      child: chip,
    );
  }
}

/// Underline tabs on the page background: visually a separate level from the
/// provider toolbar above.
class _CategoryTabs extends StatelessWidget {
  const _CategoryTabs({required this.l10n});

  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return TabBar(
      isScrollable: true,
      tabAlignment: TabAlignment.start,
      dividerColor: tokens.border,
      dividerHeight: 1,
      indicatorSize: TabBarIndicatorSize.tab,
      indicator: UnderlineTabIndicator(
        borderSide: BorderSide(color: tokens.accent, width: 2),
        insets: const EdgeInsets.symmetric(horizontal: AppSpacing.xxs),
      ),
      labelColor: tokens.accent,
      unselectedLabelColor: tokens.textSecondary,
      labelStyle: textTheme.labelLarge?.copyWith(fontWeight: FontWeight.w600),
      unselectedLabelStyle: textTheme.labelLarge,
      labelPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm),
      overlayColor: WidgetStatePropertyAll(tokens.accentSoft),
      tabs: [
        Tab(text: l10n.tabVersions),
        Tab(text: l10n.tabLoaders),
        Tab(text: l10n.tabMods),
        Tab(text: l10n.tabPacks),
        Tab(text: l10n.tabResourcePacks),
        Tab(text: l10n.tabShaders),
      ],
    );
  }
}

/// Honest placeholder for categories whose native flow is not wired yet.
class _ComingSoonTab extends StatelessWidget {
  const _ComingSoonTab({required this.l10n, required this.icon});

  final AppLocalizations l10n;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      children: [
        AppEmptyState(
          icon: icon,
          title: l10n.comingSoon,
          message: l10n.providerNotWired,
        ),
      ],
    );
  }
}
