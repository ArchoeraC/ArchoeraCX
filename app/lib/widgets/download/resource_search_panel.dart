import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../services/core/resource_models.dart';
import '../../stores/instances.dart';
import '../../stores/providers.dart';
import '../../theme/tokens.dart';
import '../common/app_empty_state.dart';
import '../common/app_network_image.dart';
import '../common/app_section.dart';
import '../common/toast.dart';
import 'pack_install_dialog.dart';

/// Real Modrinth search + install surface for one content tab.
///
/// Layout is a fixed header (query, target, sort) over an independently
/// scrolling result list, so the search stays reachable while browsing. Each
/// row opens a project detail dialog with the provider gallery; the trailing
/// action opens the compatible-version sheet directly.
class ResourceSearchPanel extends ConsumerStatefulWidget {
  const ResourceSearchPanel({
    super.key,
    required this.kind,
    required this.title,
    required this.icon,
    this.global = false,
  });

  /// `mod`, `shader`, `resourcepack` or `modpack`.
  final String kind;
  final String title;
  final IconData icon;

  /// Global searches omit the game-version/loader facets. Versions and install
  /// still resolve against the selected instance.
  final bool global;

  @override
  ConsumerState<ResourceSearchPanel> createState() =>
      _ResourceSearchPanelState();
}

class _ResourceSearchPanelState extends ConsumerState<ResourceSearchPanel> {
  static const _pageSize = 20;

  final _queryController = TextEditingController();
  final _scrollController = ScrollController();
  Timer? _debounce;

  List<ResourceProject> _items = const [];
  bool _loading = false;
  bool _loadingMore = false;
  bool _started = false;
  int _page = 0;
  int _total = 0;
  int _requestSeq = 0;
  String _sort = 'relevance';
  bool _installing = false;
  String? _error;

  bool get _hasMore => _items.length < _total;

  @override
  void initState() {
    super.initState();
    _scrollController.addListener(_onScroll);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // The branch is kept alive in an IndexedStack. Do not hit Modrinth until
    // the download branch (and this tab) is actually visible.
    if (!_started && TickerMode.valuesOf(context).enabled) {
      _started = true;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) _search();
      });
    }
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _queryController.dispose();
    _scrollController
      ..removeListener(_onScroll)
      ..dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final instance = ref.watch(selectedInstanceProvider);
    final target = _targetFor(instance, l10n);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _SearchHeader(
          l10n: l10n,
          controller: _queryController,
          target: target,
          busy: _loading,
          installing: _installing,
          total: _total,
          sort: _sort,
          onQueryChanged: _onQueryChanged,
          onSearch: _search,
          onSortChanged: (value) {
            if (value == _sort) return;
            setState(() => _sort = value);
            _search();
          },
        ),
        const SizedBox(height: AppSpacing.sm),
        Expanded(child: _buildResults(l10n)),
      ],
    );
  }

  Widget _buildResults(AppLocalizations l10n) {
    if (_loading && _items.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (_error != null && _items.isEmpty) {
      return ListView(
        padding: const EdgeInsets.only(bottom: AppSpacing.lg),
        children: [
          AppEmptyState(
            icon: Icons.cloud_off_rounded,
            title: l10n.downloadSearchError,
            message: _error!,
            primaryAction: FilledButton(
              onPressed: _search,
              child: Text(l10n.refresh),
            ),
          ),
        ],
      );
    }

    if (_items.isEmpty) {
      return ListView(
        padding: const EdgeInsets.only(bottom: AppSpacing.lg),
        children: [
          AppEmptyState(
            icon: widget.icon,
            title: widget.title,
            message: l10n.downloadSearchEmpty,
          ),
        ],
      );
    }

    final showFooter = _hasMore || _loadingMore;
    return ListView.separated(
      controller: _scrollController,
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      itemCount: _items.length + (showFooter ? 1 : 0),
      separatorBuilder: (_, _) => const SizedBox(height: AppSpacing.xs),
      itemBuilder: (context, index) {
        if (index >= _items.length) {
          return Padding(
            padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
            child: Center(
              child: _loadingMore
                  ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : TextButton.icon(
                      onPressed: () => _search(reset: false),
                      icon: const Icon(Icons.expand_more_rounded, size: 18),
                      label: Text(l10n.downloadLoadMore),
                    ),
            ),
          );
        }

        final project = _items[index];
        return _ResourceRow(
          project: project,
          onTap: _installing ? null : () => _openDetails(project),
          onInstall: _installing ? null : () => _openVersions(project),
        );
      },
    );
  }

  String _targetFor(GameInstance? instance, AppLocalizations l10n) {
    if (widget.kind == 'modpack') return l10n.packTargetNewInstance;
    if (widget.global) return l10n.downloadGlobalTarget;
    final game = instance?.gameVersion ?? '1.21.1';
    final loader = (instance?.loader ?? GameLoader.fabric).labelFor(l10n);
    return switch (widget.kind) {
      'shader' => l10n.resourceTargetShader(game),
      'resourcepack' => l10n.resourceTargetGame(game),
      _ => l10n.resourceTargetGameLoader(game, loader),
    };
  }

  String _loaderFor(GameInstance instance) => switch (instance.loader) {
    GameLoader.fabric => 'fabric',
    GameLoader.forge => 'forge',
    GameLoader.neoforge => 'neoforge',
    GameLoader.quilt => 'quilt',
    _ => 'fabric',
  };

  void _onQueryChanged(String _) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), () {
      if (mounted) _search();
    });
  }

  void _onScroll() {
    if (!_scrollController.hasClients ||
        _loading ||
        _loadingMore ||
        !_hasMore) {
      return;
    }
    final position = _scrollController.position;
    if (position.pixels > position.maxScrollExtent - 420) {
      _search(reset: false);
    }
  }

  Future<void> _search({bool reset = true}) async {
    _debounce?.cancel();
    if (!reset && (_loading || _loadingMore || !_hasMore)) return;

    final client = ref.read(coreClientProvider);
    if (client == null) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = AppLocalizations.of(context).commonCoreNotConnected;
        });
      }
      return;
    }

    final instance = ref.read(selectedInstanceProvider);
    final packMode = widget.kind == 'modpack';
    final game = widget.global || packMode
        ? null
        : (instance?.gameVersion ?? '1.21.1');
    final loader = packMode
        ? null
        : (instance == null ? 'fabric' : _loaderFor(instance));
    final requestSeq = ++_requestSeq;
    final page = reset ? 0 : _page + 1;

    setState(() {
      _error = null;
      if (reset) {
        _items = const [];
        _page = 0;
        _total = 0;
        _loading = true;
      } else {
        _loadingMore = true;
      }
    });

    try {
      final result = await client.searchModrinth(
        query: _queryController.text.trim(),
        kind: widget.kind,
        gameVersion: game,
        loader: widget.global || packMode ? null : loader,
        shaderLoader: !widget.global && widget.kind == 'shader' ? 'iris' : null,
        sort: _sort,
        page: page,
        pageSize: _pageSize,
      );
      if (!mounted || requestSeq != _requestSeq) return;
      setState(() {
        _items = reset ? result.items : [..._items, ...result.items];
        _page = result.page;
        _total = result.total;
        _error = null;
      });
      if (reset) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (mounted && _scrollController.hasClients) {
            _scrollController.jumpTo(0);
          }
        });
      }
    } catch (error) {
      if (!mounted || requestSeq != _requestSeq) return;
      if (reset) {
        setState(() => _error = error.toString());
      } else {
        _notify(error.toString(), type: ToastType.error);
      }
    } finally {
      if (mounted && requestSeq == _requestSeq) {
        setState(() {
          _loading = false;
          _loadingMore = false;
        });
      }
    }
  }

  Future<void> _openDetails(ResourceProject project) async {
    final install = await showDialog<bool>(
      context: context,
      builder: (_) => _ProjectDetailDialog(project: project),
    );
    if (install == true && mounted) {
      await _openVersions(project);
    }
  }

  Future<void> _openVersions(ResourceProject project) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      _notify(
        AppLocalizations.of(context).commonCoreNotConnected,
        type: ToastType.error,
      );
      return;
    }

    if (widget.kind == 'modpack') {
      await _openPackVersions(project);
      return;
    }

    final instance = ref.read(selectedInstanceProvider);
    if (instance == null) {
      _notify(
        AppLocalizations.of(context).selectInstance,
        type: ToastType.warning,
      );
      return;
    }

    if (widget.kind == 'mod' && instance.loader == GameLoader.vanilla) {
      _notify(
        AppLocalizations.of(context).resourceNeedsLoader,
        type: ToastType.warning,
      );
      return;
    }

    setState(() => _installing = true);
    try {
      final versions = await client.modrinthVersions(
        projectId: project.id,
        kind: widget.kind,
        gameVersion: instance.gameVersion,
        loader: _loaderFor(instance),
        shaderLoader: widget.kind == 'shader' ? 'iris' : null,
      );
      if (!mounted) return;
      if (versions.isEmpty) {
        _notify(
          AppLocalizations.of(context).downloadNoVersions,
          type: ToastType.warning,
        );
        return;
      }

      final selected = await showModalBottomSheet<ResourceVersion>(
        context: context,
        showDragHandle: true,
        isScrollControlled: true,
        constraints: BoxConstraints(
          maxHeight: MediaQuery.sizeOf(context).height * 0.82,
        ),
        builder: (context) =>
            _VersionSheet(project: project, versions: versions),
      );
      if (selected != null && mounted) {
        await _install(project, selected);
      }
    } catch (error) {
      if (mounted) _notify(error.toString(), type: ToastType.error);
    } finally {
      if (mounted) setState(() => _installing = false);
    }
  }

  Future<void> _openPackVersions(ResourceProject project) async {
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    final l10n = AppLocalizations.of(context);

    setState(() => _installing = true);
    try {
      final versions = await client.modrinthVersions(
        projectId: project.id,
        kind: 'modpack',
      );
      if (!mounted) return;
      if (versions.isEmpty) {
        _notify(l10n.downloadNoVersions, type: ToastType.warning);
        return;
      }

      final selected = await showModalBottomSheet<ResourceVersion>(
        context: context,
        showDragHandle: true,
        isScrollControlled: true,
        constraints: BoxConstraints(
          maxHeight: MediaQuery.sizeOf(context).height * 0.82,
        ),
        builder: (context) =>
            _VersionSheet(project: project, versions: versions),
      );
      if (selected != null && mounted) {
        await showPackInstallDialog(
          context,
          projectId: project.id,
          versionId: selected.id,
          title: l10n.packInstallTitle,
          taskLabel: project.name,
        );
      }
    } catch (error) {
      if (mounted) _notify(error.toString(), type: ToastType.error);
    } finally {
      if (mounted) setState(() => _installing = false);
    }
  }

  Future<void> _install(
    ResourceProject project,
    ResourceVersion version,
  ) async {
    final client = ref.read(coreClientProvider);
    if (client == null) return;

    final instance = ref.read(selectedInstanceProvider);
    if (instance == null) {
      _notify(
        AppLocalizations.of(context).selectInstance,
        type: ToastType.warning,
      );
      return;
    }

    if (widget.kind == 'mod' && instance.loader == GameLoader.vanilla) {
      _notify(
        AppLocalizations.of(context).resourceNeedsLoader,
        type: ToastType.warning,
      );
      return;
    }

    final downloadRoot = instance.rootPath;
    if (downloadRoot.isEmpty) {
      _notify(
        AppLocalizations.of(context).selectInstance,
        type: ToastType.warning,
      );
      return;
    }

    setState(() => _installing = true);
    try {
      final plan = await client.planModrinthDownload(
        projectId: project.id,
        versionId: version.id,
        kind: widget.kind,
        gameVersion: instance.gameVersion,
        loader: _loaderFor(instance),
        shaderLoader: widget.kind == 'shader' ? 'iris' : null,
        includeDependencies: true,
        root: downloadRoot,
      );
      if (!mounted) return;
      final result = await client.executeDownload(plan, label: project.name);
      if (!mounted) return;
      _notify(AppLocalizations.of(context).downloadResult(result.status));
    } catch (error) {
      if (mounted) _notify(error.toString(), type: ToastType.error);
    } finally {
      if (mounted) setState(() => _installing = false);
    }
  }

  void _notify(String message, {ToastType type = ToastType.info}) {
    toast(message, type: type);
  }
}

/// Search field + target + sort. The target line explains which instance the
/// query is scoped to without adding another card.
class _SearchHeader extends StatelessWidget {
  const _SearchHeader({
    required this.l10n,
    required this.controller,
    required this.target,
    required this.busy,
    required this.installing,
    required this.total,
    required this.sort,
    required this.onQueryChanged,
    required this.onSearch,
    required this.onSortChanged,
  });

  final AppLocalizations l10n;
  final TextEditingController controller;
  final String target;
  final bool busy;
  final bool installing;
  final int total;
  final String sort;
  final ValueChanged<String> onQueryChanged;
  final VoidCallback onSearch;
  final ValueChanged<String> onSortChanged;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return AppSection(
      showTop: true,
      showBottom: true,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          LayoutBuilder(
            builder: (context, constraints) {
              final field = TextField(
                controller: controller,
                textInputAction: TextInputAction.search,
                onChanged: onQueryChanged,
                onSubmitted: (_) => onSearch(),
                decoration: InputDecoration(
                  hintText: l10n.searchDownload,
                  prefixIcon: const Icon(Icons.search_rounded, size: 20),
                  isDense: true,
                ),
              );
              final button = FilledButton(
                onPressed: busy ? null : onSearch,
                style: FilledButton.styleFrom(
                  minimumSize: const Size(0, 44),
                  padding: const EdgeInsets.symmetric(
                    horizontal: AppSpacing.md,
                  ),
                ),
                child: Text(l10n.downloadSearchAction),
              );

              if (constraints.maxWidth < 420) {
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    field,
                    const SizedBox(height: AppSpacing.xs),
                    button,
                  ],
                );
              }

              return Row(
                children: [
                  Expanded(child: field),
                  const SizedBox(width: AppSpacing.xs),
                  button,
                ],
              );
            },
          ),
          const SizedBox(height: AppSpacing.xs),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xxs),
            child: Row(
              children: [
                Icon(Icons.tune_rounded, size: 14, color: tokens.textTertiary),
                const SizedBox(width: AppSpacing.xxs),
                Expanded(
                  child: Text(
                    target,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.labelSmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                ),
                if (total > 0) ...[
                  Text(
                    l10n.downloadResults(total),
                    style: textTheme.labelSmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                  const SizedBox(width: AppSpacing.xs),
                ],
                PopupMenuButton<String>(
                  tooltip: l10n.downloadSort,
                  initialValue: sort,
                  onSelected: onSortChanged,
                  itemBuilder: (context) => [
                    _sortItem('relevance', l10n.downloadSortRelevance),
                    _sortItem('downloads', l10n.downloadSortDownloads),
                    _sortItem('follows', l10n.downloadSortFollows),
                    _sortItem('newest', l10n.downloadSortNewest),
                    _sortItem('updated', l10n.downloadSortUpdated),
                  ],
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(
                        Icons.sort_rounded,
                        size: 14,
                        color: tokens.textSecondary,
                      ),
                      const SizedBox(width: 2),
                      Text(
                        _sortLabel(l10n, sort),
                        style: textTheme.labelSmall?.copyWith(
                          color: tokens.textSecondary,
                        ),
                      ),
                    ],
                  ),
                ),
                if (installing) ...[
                  const SizedBox(width: AppSpacing.xs),
                  SizedBox(
                    width: 12,
                    height: 12,
                    child: CircularProgressIndicator(
                      strokeWidth: 1.6,
                      color: tokens.accent,
                    ),
                  ),
                  const SizedBox(width: AppSpacing.xxs),
                  Text(
                    l10n.downloadInstalling,
                    style: textTheme.labelSmall?.copyWith(color: tokens.accent),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }

  PopupMenuItem<String> _sortItem(String value, String label) =>
      PopupMenuItem(value: value, child: Text(label));

  String _sortLabel(AppLocalizations l10n, String value) => switch (value) {
    'downloads' => l10n.downloadSortDownloads,
    'follows' => l10n.downloadSortFollows,
    'newest' => l10n.downloadSortNewest,
    'updated' => l10n.downloadSortUpdated,
    _ => l10n.downloadSortRelevance,
  };
}

class _ResourceRow extends StatelessWidget {
  const _ResourceRow({required this.project, this.onTap, this.onInstall});

  final ResourceProject project;
  final VoidCallback? onTap;
  final VoidCallback? onInstall;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final author = project.author?.trim();

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        hoverColor: tokens.surface2,
        child: Container(
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.xs,
            vertical: AppSpacing.sm,
          ),
          decoration: BoxDecoration(
            border: Border(bottom: BorderSide(color: tokens.border)),
          ),
          child: Row(
            children: [
              _ProjectThumb(project: project, size: 56),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text.rich(
                      TextSpan(
                        children: [
                          TextSpan(text: project.name),
                          if (author != null && author.isNotEmpty)
                            TextSpan(
                              text: '  ${l10n.downloadByAuthor(author)}',
                              style: textTheme.bodySmall?.copyWith(
                                color: tokens.textTertiary,
                              ),
                            ),
                        ],
                      ),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.titleSmall,
                    ),
                    if (project.summary.isNotEmpty) ...[
                      const SizedBox(height: AppSpacing.xxs),
                      Text(
                        project.summary,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.bodySmall?.copyWith(
                          color: tokens.textSecondary,
                          height: 1.35,
                        ),
                      ),
                    ],
                    const SizedBox(height: AppSpacing.xxs),
                    Wrap(
                      spacing: AppSpacing.sm,
                      runSpacing: 2,
                      children: [
                        _MetaText(
                          icon: Icons.file_download_outlined,
                          text: _formatCount(project.downloads),
                        ),
                        _MetaText(
                          icon: Icons.favorite_border_rounded,
                          text: _formatCount(project.follows),
                        ),
                        for (final category in project.categories.take(2))
                          _Tag(label: category),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(width: AppSpacing.xs),
              IconButton(
                onPressed: onInstall,
                tooltip: l10n.downloadInstall,
                icon: const Icon(Icons.download_rounded, size: 20),
              ),
              Icon(Icons.chevron_right_rounded, color: tokens.textTertiary),
            ],
          ),
        ),
      ),
    );
  }
}

class _ProjectDetailDialog extends ConsumerStatefulWidget {
  const _ProjectDetailDialog({required this.project});

  final ResourceProject project;

  @override
  ConsumerState<_ProjectDetailDialog> createState() =>
      _ProjectDetailDialogState();
}

class _ProjectDetailDialogState extends ConsumerState<_ProjectDetailDialog> {
  ResourceProject? _detail;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      if (mounted) setState(() => _loading = false);
      return;
    }
    try {
      final detail = await client.modrinthProject(projectId: widget.project.id);
      if (mounted) {
        setState(() {
          _detail = detail;
          _loading = false;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = error.toString();
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final project = _detail ?? widget.project;
    final gallery = project.gallery;

    return Dialog(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxWidth: 860,
          maxHeight: MediaQuery.sizeOf(context).height * 0.88,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.lg,
                AppSpacing.md,
                AppSpacing.sm,
                0,
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _ProjectThumb(project: project, size: 68),
                  const SizedBox(width: AppSpacing.md),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          project.name,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        if (project.author?.isNotEmpty == true) ...[
                          const SizedBox(height: 2),
                          Text(
                            l10n.downloadByAuthor(project.author!),
                            style: Theme.of(context).textTheme.bodySmall
                                ?.copyWith(color: tokens.textTertiary),
                          ),
                        ],
                        const SizedBox(height: AppSpacing.xs),
                        Text(
                          project.summary,
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.bodyMedium
                              ?.copyWith(color: tokens.textSecondary),
                        ),
                      ],
                    ),
                  ),
                  IconButton(
                    onPressed: () => Navigator.of(context).pop(false),
                    tooltip: l10n.commonClose,
                    icon: const Icon(Icons.close_rounded),
                  ),
                ],
              ),
            ),
            if (_loading) const LinearProgressIndicator(minHeight: 2),
            Expanded(
              child: SingleChildScrollView(
                padding: const EdgeInsets.fromLTRB(
                  AppSpacing.lg,
                  AppSpacing.md,
                  AppSpacing.lg,
                  AppSpacing.md,
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Wrap(
                      spacing: AppSpacing.xs,
                      runSpacing: AppSpacing.xs,
                      children: [
                        _MetaText(
                          icon: Icons.file_download_outlined,
                          text:
                              '${l10n.statDownloads}: ${_formatCount(project.downloads)}',
                        ),
                        _MetaText(
                          icon: Icons.favorite_border_rounded,
                          text: _formatCount(project.follows),
                        ),
                        if (project.license?.isNotEmpty == true)
                          _Tag(label: project.license!),
                        for (final loader in project.loaders.take(4))
                          _Tag(label: loader),
                        for (final category in project.categories.take(6))
                          _Tag(label: category),
                      ],
                    ),
                    if (_error != null) ...[
                      const SizedBox(height: AppSpacing.md),
                      Text(
                        _error!,
                        style: Theme.of(context).textTheme.bodySmall
                            ?.copyWith(color: tokens.danger),
                      ),
                    ],
                    if (gallery.isNotEmpty) ...[
                      const SizedBox(height: AppSpacing.lg),
                      Text(
                        l10n.downloadScreenshots,
                        style: Theme.of(context).textTheme.titleSmall,
                      ),
                      const SizedBox(height: AppSpacing.xs),
                      SizedBox(
                        height: 168,
                        child: ListView.separated(
                          scrollDirection: Axis.horizontal,
                          itemCount: gallery.length,
                          separatorBuilder: (_, _) =>
                              const SizedBox(width: AppSpacing.xs),
                          itemBuilder: (context, index) {
                            final image = gallery[index];
                            return Semantics(
                              label: l10n.downloadImagePreview,
                              button: true,
                              child: InkWell(
                                borderRadius: BorderRadius.circular(
                                  tokens.radiusM,
                                ),
                                onTap: () => _previewImage(context, image),
                                child: ClipRRect(
                                  borderRadius: BorderRadius.circular(
                                    tokens.radiusM,
                                  ),
                                  child: SizedBox(
                                    width: 280,
                                    height: 168,
                                    child: AppNetworkImage(
                                      url: image.url,
                                      fallbackIcon: Icons.photo_outlined,
                                    ),
                                  ),
                                ),
                              ),
                            );
                          },
                        ),
                      ),
                    ],
                    if (project.body?.trim().isNotEmpty == true) ...[
                      const SizedBox(height: AppSpacing.lg),
                      Text(
                        l10n.downloadDescription,
                        style: Theme.of(context).textTheme.titleSmall,
                      ),
                      const SizedBox(height: AppSpacing.xs),
                      SelectableText(
                        _plainBody(project.body!),
                        style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                          color: tokens.textSecondary,
                          height: 1.55,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.lg,
                AppSpacing.sm,
                AppSpacing.lg,
                AppSpacing.md,
              ),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      l10n.downloadSelectVersion,
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                  ),
                  FilledButton.icon(
                    onPressed: () => Navigator.of(context).pop(true),
                    icon: const Icon(Icons.download_rounded, size: 18),
                    label: Text(l10n.downloadInstall),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  void _previewImage(BuildContext context, ResourceGalleryImage image) {
    showDialog<void>(
      context: context,
      barrierColor: Colors.black.withValues(alpha: 0.88),
      builder: (context) => Dialog(
        backgroundColor: Colors.black,
        insetPadding: const EdgeInsets.all(AppSpacing.lg),
        child: SizedBox(
          width: MediaQuery.sizeOf(context).width * 0.9,
          height: MediaQuery.sizeOf(context).height * 0.86,
          child: Stack(
            children: [
              Positioned.fill(
                child: InteractiveViewer(
                  minScale: 0.6,
                  maxScale: 5,
                  child: SizedBox.expand(
                    child: AppNetworkImage(
                      url: image.url,
                      fit: BoxFit.contain,
                      fallbackIcon: Icons.broken_image_outlined,
                    ),
                  ),
                ),
              ),
              Positioned(
                top: 4,
                right: 4,
                child: IconButton(
                  onPressed: () => Navigator.of(context).pop(),
                  tooltip: MaterialLocalizations.of(context).closeButtonTooltip,
                  icon: const Icon(Icons.close_rounded, color: Colors.white),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _VersionSheet extends StatelessWidget {
  const _VersionSheet({required this.project, required this.versions});

  final ResourceProject project;
  final List<ResourceVersion> versions;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(
              AppSpacing.lg,
              0,
              AppSpacing.lg,
              AppSpacing.sm,
            ),
            child: Row(
              children: [
                _ProjectThumb(project: project, size: 44),
                const SizedBox(width: AppSpacing.sm),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        project.name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.titleMedium,
                      ),
                      const SizedBox(height: 2),
                      Text(
                        l10n.downloadSelectVersion,
                        style: textTheme.bodySmall?.copyWith(
                          color: tokens.textTertiary,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
          Flexible(
            child: ListView.separated(
              shrinkWrap: true,
              padding: const EdgeInsets.only(bottom: AppSpacing.sm),
              itemCount: versions.length,
              separatorBuilder: (_, _) => Divider(
                height: 1,
                indent: AppSpacing.lg,
                endIndent: AppSpacing.lg,
                color: tokens.border,
              ),
              itemBuilder: (context, index) {
                final version = versions[index];
                final file = version.primaryFile;
                final dependencyCount = version.dependencies.length;
                return ListTile(
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: AppSpacing.lg,
                  ),
                  title: Text(version.name),
                  subtitle: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '${version.versionNumber} · '
                        '${_versionTypeLabel(l10n, version.versionType)}'
                        '${file == null ? '' : ' · ${_formatSize(file.size)}'}',
                      ),
                      if (dependencyCount > 0)
                        Text(
                          l10n.downloadDependencies(dependencyCount),
                          style: textTheme.bodySmall?.copyWith(
                            color: tokens.textTertiary,
                          ),
                        ),
                    ],
                  ),
                  trailing: const Icon(Icons.download_rounded),
                  onTap: () => Navigator.of(context).pop(version),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

class _ProjectThumb extends StatelessWidget {
  const _ProjectThumb({required this.project, required this.size});

  final ResourceProject project;
  final double size;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final icon = _kindIcon(project.kind);
    final url = project.iconUrl;
    return ClipRRect(
      borderRadius: BorderRadius.circular(tokens.radiusM),
      child: SizedBox(
        width: size,
        height: size,
        child: url == null || url.isEmpty
            ? ColoredBox(
                color: tokens.surface2,
                child: Icon(icon, color: tokens.accent, size: size * 0.44),
              )
            : AppNetworkImage(url: url, fallbackIcon: icon),
      ),
    );
  }
}

class _MetaText extends StatelessWidget {
  const _MetaText({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 13, color: tokens.textTertiary),
        const SizedBox(width: 3),
        Text(
          text,
          style: Theme.of(context).textTheme.labelSmall
              ?.copyWith(color: tokens.textTertiary),
        ),
      ],
    );
  }
}

class _Tag extends StatelessWidget {
  const _Tag({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusS),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall
            ?.copyWith(color: tokens.textSecondary),
      ),
    );
  }
}

IconData _kindIcon(String kind) => switch (kind) {
  'shader' => Icons.flare_rounded,
  'resourcepack' => Icons.photo_library_rounded,
  'modpack' => Icons.inventory_2_rounded,
  _ => Icons.extension_rounded,
};

String _plainBody(String value) {
  final withoutImages = value.replaceAll(RegExp(r'!\[[^\]]*\]\([^)]*\)'), '');
  final links = withoutImages.replaceAllMapped(
    RegExp(r'\[([^\]]+)\]\([^)]*\)'),
    (match) => match.group(1) ?? '',
  );
  final withoutTags = links.replaceAll(RegExp(r'<[^>]+>'), '');
  final withoutHeadings = withoutTags.replaceAll(
    RegExp(r'^#{1,6}\s*', multiLine: true),
    '',
  );
  return withoutHeadings.replaceAll(RegExp(r'(\*\*|__|~~|`)'), '').trim();
}

String _formatCount(int value) {
  if (value >= 1000000) return '${(value / 1000000).toStringAsFixed(1)}M';
  if (value >= 1000) return '${(value / 1000).toStringAsFixed(1)}K';
  return '$value';
}

String _versionTypeLabel(AppLocalizations l10n, String type) =>
    switch (type.toLowerCase()) {
      'release' => l10n.resourceVersionRelease,
      'beta' => l10n.resourceVersionBeta,
      'alpha' => l10n.resourceVersionAlpha,
      _ => type,
    };

String _formatSize(int bytes) {
  if (bytes >= 1024 * 1024) {
    return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
  }
  if (bytes >= 1024) return '${(bytes / 1024).toStringAsFixed(0)} KB';
  return '$bytes B';
}
