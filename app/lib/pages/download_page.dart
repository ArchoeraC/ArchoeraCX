import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/build_policy.dart';
import '../services/core/download_policy.dart';
import '../stores/providers.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/download/resource_search_panel.dart';
import '../widgets/instances/create_instance_dialog.dart';
import '../widgets/instances/vanilla_install_dialog.dart';
import '../services/core/version_models.dart';

/// Download hub.
///
/// Two independent levels:
/// 1. provider (only Modrinth is wired today),
/// 2. content category (versions, mods, modpacks, resource packs, shaders).
///
/// Loader installation is an instance operation and lives in Instances.
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

    return Consumer(
      builder: (context, ref, _) {
        final policyAsync = ref.watch(downloadPolicyProvider);
        return policyAsync.when(
          loading: () => Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              AppPageHeader(
                title: l10n.downloadTitle,
                subtitle: l10n.downloadSubtitle,
              ),
              const Expanded(child: Center(child: CircularProgressIndicator())),
            ],
          ),
          error: (error, _) => _DownloadUnavailable(
            l10n: l10n,
            message: error.toString(),
            policy: null,
            onReload: () => ref.invalidate(downloadPolicyProvider),
          ),
          data: (policy) {
            if (!policy.enabled) {
              return _DownloadUnavailable(
                l10n: l10n,
                message: l10n.downloadUnavailableMessage,
                policy: policy,
                onReload: () => ref.invalidate(downloadPolicyProvider),
              );
            }
            return DefaultTabController(
              length: 5,
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
                        const _VersionsPanel(),
                        ResourceSearchPanel(
                          kind: 'mod',
                          title: l10n.tabMods,
                          icon: Icons.extension_rounded,
                          global: true,
                        ),
                        _ComingSoonTab(
                          l10n: l10n,
                          icon: Icons.inventory_2_rounded,
                          message: l10n.downloadModpackPending,
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
          },
        );
      },
    );
  }
}

class _DownloadUnavailable extends StatelessWidget {
  const _DownloadUnavailable({
    required this.l10n,
    required this.message,
    required this.policy,
    required this.onReload,
  });

  final AppLocalizations l10n;
  final String message;
  final DownloadPolicyState? policy;
  final VoidCallback onReload;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final configPath = policy?.configPath;
    final missing = policy?.missingFields ?? const <String>[];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AppPageHeader(
          title: l10n.downloadTitle,
          subtitle: l10n.downloadSubtitle,
        ),
        const SizedBox(height: AppSpacing.md),
        Expanded(
          child: AppEmptyState(
            icon: Icons.download_for_offline_outlined,
            title: l10n.downloadUnavailableTitle,
            message: message,
            primaryAction: FilledButton.icon(
              onPressed: onReload,
              icon: const Icon(Icons.refresh_rounded, size: 18),
              label: Text(l10n.downloadConfigReload),
            ),
            secondaryAction: configPath == null || configPath.isEmpty
                ? null
                : Column(
                    children: [
                      Text(
                        l10n.downloadConfigPath(configPath),
                        textAlign: TextAlign.center,
                        style: Theme.of(context).textTheme.bodySmall
                            ?.copyWith(color: tokens.textSecondary),
                      ),
                      if (missing.isNotEmpty) ...[
                        const SizedBox(height: AppSpacing.xxs),
                        Text(
                          l10n.downloadConfigFields,
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.bodySmall
                              ?.copyWith(color: tokens.textTertiary),
                        ),
                      ],
                    ],
                  ),
          ),
        ),
      ],
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
                  fontWeight: selected ? FontWeight.w500 : FontWeight.w500,
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
      labelStyle: textTheme.labelLarge?.copyWith(fontWeight: FontWeight.w500),
      unselectedLabelStyle: textTheme.labelLarge,
      labelPadding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm),
      overlayColor: WidgetStatePropertyAll(tokens.accentSoft),
      tabs: [
        Tab(text: l10n.tabVersions),
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
  const _ComingSoonTab({required this.l10n, required this.icon, this.message});

  final AppLocalizations l10n;
  final IconData icon;
  final String? message;

  @override
  Widget build(BuildContext context) {
    return ListView(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      children: [
        AppEmptyState(
          icon: icon,
          title: l10n.comingSoon,
          message: message ?? l10n.providerNotWired,
        ),
      ],
    );
  }
}

class _VersionsPanel extends ConsumerStatefulWidget {
  const _VersionsPanel();

  @override
  ConsumerState<_VersionsPanel> createState() => _VersionsPanelState();
}

class _VersionsPanelState extends ConsumerState<_VersionsPanel> {
  final _queryController = TextEditingController();
  List<MojangGameVersion> _items = const [];
  bool _loading = true;
  bool _includeSnapshots = false;
  String? _error;

  bool _started = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_started) {
      _started = true;
      _load();
    }
  }

  @override
  void dispose() {
    _queryController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(() {
        _loading = false;
        _error = AppLocalizations.of(context).commonCoreNotConnected;
      });
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final result = await client.listGameVersions(includeSnapshots: true);
      if (!mounted) return;
      setState(() {
        _items = result.items;
        _loading = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = error.toString();
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final query = _queryController.text.trim().toLowerCase();
    final filtered = _items
        .where((item) => _includeSnapshots || item.isRelease)
        .where((item) => query.isEmpty || item.id.toLowerCase().contains(query))
        .toList(growable: false);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: TextField(
                controller: _queryController,
                onChanged: (_) => setState(() {}),
                decoration: InputDecoration(
                  hintText: l10n.versionSearchHint,
                  prefixIcon: const Icon(Icons.search_rounded, size: 20),
                  isDense: true,
                ),
              ),
            ),
            const SizedBox(width: AppSpacing.sm),
            Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Checkbox(
                  value: _includeSnapshots,
                  onChanged: (value) => setState(() {
                    _includeSnapshots = value ?? false;
                  }),
                ),
                Text(l10n.versionIncludeSnapshots),
              ],
            ),
            IconButton(
              onPressed: _loading ? null : _load,
              tooltip: l10n.refresh,
              icon: const Icon(Icons.refresh_rounded, size: 20),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.sm),
        if (_loading)
          const Expanded(child: Center(child: CircularProgressIndicator()))
        else if (_error != null)
          Expanded(
            child: Center(
              child: Text(
                '${l10n.versionLoadFailed} $_error',
                textAlign: TextAlign.center,
                style: TextStyle(color: tokens.danger),
              ),
            ),
          )
        else if (filtered.isEmpty)
          Expanded(
            child: Center(
              child: Text(
                l10n.versionEmpty,
                style: TextStyle(color: tokens.textSecondary),
              ),
            ),
          )
        else
          Expanded(
            child: ListView.separated(
              padding: const EdgeInsets.only(bottom: AppSpacing.lg),
              itemCount: filtered.length,
              separatorBuilder: (_, _) =>
                  Divider(height: 1, color: tokens.border),
              itemBuilder: (context, index) {
                final item = filtered[index];
                return ListTile(
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: AppSpacing.xs,
                  ),
                  leading: Icon(
                    item.isRelease
                        ? Icons.verified_outlined
                        : Icons.science_outlined,
                    size: 20,
                    color: item.isRelease
                        ? tokens.success
                        : tokens.textTertiary,
                  ),
                  title: Text(item.id),
                  subtitle: Text(
                    [
                      item.type,
                      if (item.releaseTime != null)
                        MaterialLocalizations.of(context)
                            .formatShortDate(item.releaseTime!.toLocal()),
                    ].join(' · '),
                  ),
                  trailing: TextButton(
                    onPressed: () async {
                      final created = await showCreateInstanceDialog(
                        context,
                        initialVersion: item.id,
                      );
                      if (created != null && context.mounted) {
                        await showVanillaInstallDialog(context, created);
                      }
                    },
                    child: Text(l10n.versionUse),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}
