import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../l10n/generated/app_localizations.dart';
import '../stores/instances.dart';
import '../stores/providers.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/download/resource_search_panel.dart';
import '../widgets/instances/create_instance_dialog.dart';
import '../services/core/version_models.dart';

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
                const _VersionsPanel(),
                const _LoaderInstallPanel(),
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

class _LoaderInstallPanel extends ConsumerStatefulWidget {
  const _LoaderInstallPanel();

  @override
  ConsumerState<_LoaderInstallPanel> createState() =>
      _LoaderInstallPanelState();
}

class _LoaderInstallPanelState extends ConsumerState<_LoaderInstallPanel> {
  late final TextEditingController _gameController;
  final _loaderController = TextEditingController();
  String _kind = 'forge';
  String? _installerPath;
  bool _busy = false;
  bool _requiresJava = false;
  String? _message;
  bool _ok = false;

  @override
  void initState() {
    super.initState();
    final instance = ref.read(selectedInstanceProvider);
    _gameController = TextEditingController(text: instance?.gameVersion ?? '');
  }

  @override
  void dispose() {
    _gameController.dispose();
    _loaderController.dispose();
    super.dispose();
  }

  Future<void> _chooseInstaller(AppLocalizations l10n) async {
    final file = await openFile(
      acceptedTypeGroups: <XTypeGroup>[
        XTypeGroup(label: l10n.loaderInstallerLabel, extensions: const ['jar']),
      ],
    );
    if (file == null || !mounted) return;
    setState(() {
      _installerPath = file.path;
      _message = null;
    });
  }

  Future<void> _install(AppLocalizations l10n) async {
    final client = ref.read(coreClientProvider);
    final instance = ref.read(selectedInstanceProvider);
    if (client == null) {
      setState(() => _message = l10n.commonCoreNotConnected);
      return;
    }
    if (instance == null || instance.rootPath.isEmpty) {
      setState(() => _message = l10n.loaderInstallNoInstance);
      return;
    }
    final installer = _installerPath;
    if (installer == null || installer.isEmpty) {
      setState(() => _message = l10n.loaderInstallPickInstaller);
      return;
    }
    final game = _gameController.text.trim();
    final loaderVersion = _loaderController.text.trim();
    if (game.isEmpty || loaderVersion.isEmpty) {
      setState(() => _message = l10n.loaderInstallFailed);
      return;
    }

    setState(() {
      _busy = true;
      _message = null;
      _ok = false;
      _requiresJava = false;
    });
    try {
      final plan = await client.planLoaderInstall(
        root: instance.rootPath,
        installerPath: installer,
        kind: _kind,
        gameVersion: game,
        loaderVersion: loaderVersion,
      );
      final result = await client.executeDownload(plan.plan);
      if (!result.ok) {
        if (!mounted) return;
        setState(() {
          _busy = false;
          _message = result.stderr?.isNotEmpty == true
              ? result.stderr
              : l10n.loaderInstallFailed;
        });
        return;
      }
      await client.commitLoaderVersion(
        root: instance.rootPath,
        versionId: plan.versionId,
        versionJson: plan.versionJson,
      );
      if (!mounted) return;
      setState(() {
        _busy = false;
        _ok = true;
        _requiresJava = plan.requiresJava;
        _message = l10n.loaderInstallDone;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _ok = false;
        _message = error.toString();
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final instance = ref.watch(selectedInstanceProvider);

    if (instance == null) {
      return Center(
        child: Text(
          l10n.loaderInstallNoInstance,
          style: Theme.of(context).textTheme.bodyMedium
              ?.copyWith(color: tokens.textSecondary),
        ),
      );
    }

    return ListView(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      children: [
        Text(
          l10n.loaderInstallTitle,
          style: Theme.of(context).textTheme.titleLarge,
        ),
        const SizedBox(height: AppSpacing.xxs),
        Text(
          '${instance.name} · ${instance.rootPath}',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: Theme.of(context).textTheme.bodySmall
              ?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(height: AppSpacing.md),
        Wrap(
          spacing: AppSpacing.sm,
          runSpacing: AppSpacing.sm,
          children: [
            SizedBox(
              width: 210,
              child: DropdownButtonFormField<String>(
                initialValue: _kind,
                decoration: InputDecoration(labelText: l10n.loaderKindLabel),
                items: const [
                  DropdownMenuItem(value: 'forge', child: Text('Forge')),
                  DropdownMenuItem(value: 'neoforge', child: Text('NeoForge')),
                ],
                onChanged: _busy
                    ? null
                    : (value) {
                        if (value != null) setState(() => _kind = value);
                      },
              ),
            ),
            SizedBox(
              width: 180,
              child: TextField(
                controller: _gameController,
                enabled: !_busy,
                decoration: InputDecoration(
                  labelText: l10n.loaderGameVersionLabel,
                ),
              ),
            ),
            SizedBox(
              width: 180,
              child: TextField(
                controller: _loaderController,
                enabled: !_busy,
                decoration: InputDecoration(labelText: l10n.loaderVersionLabel),
              ),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.md),
        Row(
          children: [
            Expanded(
              child: InputDecorator(
                decoration: InputDecoration(
                  labelText: l10n.loaderInstallerLabel,
                ),
                child: Text(
                  _installerPath ?? '—',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: _installerPath == null
                        ? tokens.textTertiary
                        : tokens.textSecondary,
                  ),
                ),
              ),
            ),
            const SizedBox(width: AppSpacing.xs),
            OutlinedButton.icon(
              onPressed: _busy ? null : () => _chooseInstaller(l10n),
              icon: const Icon(Icons.file_open_outlined, size: 18),
              label: Text(l10n.loaderChooseInstaller),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.md),
        Row(
          children: [
            FilledButton.icon(
              onPressed: _busy ? null : () => _install(l10n),
              icon: _busy
                  ? const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.download_rounded, size: 18),
              label: Text(
                _busy ? l10n.loaderInstalling : l10n.loaderInstallAction,
              ),
            ),
            const SizedBox(width: AppSpacing.sm),
            if (_message != null)
              Expanded(
                child: Text(
                  _message!,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: _ok ? tokens.success : tokens.danger),
                ),
              ),
          ],
        ),
        if (_requiresJava) ...[
          const SizedBox(height: AppSpacing.sm),
          Text(
            l10n.loaderInstallRequiresJava,
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(color: tokens.warning),
          ),
        ],
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
                    onPressed: () => showCreateInstanceDialog(
                      context,
                      initialVersion: item.id,
                    ),
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
