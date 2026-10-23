import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../services/core/resources/resource_models.dart';
import '../../stores/downloads.dart';
import '../../stores/instances.dart';
import '../../stores/providers.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../../utils/format.dart';
import '../common/app_progress_bar.dart';
import '../common/app_empty_state.dart';
import '../common/app_network_image.dart';
import '../common/app_section.dart';
import '../common/toast.dart';

part 'download/search_header.dart';
part 'download/resource_rows.dart';
part 'download/project_pane.dart';
part 'download/version_pane.dart';
part 'download/search_utils.dart';

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

  /// Project shown in the side pane (wide layouts) or detail view (narrow).
  ResourceProject? _selected;

  /// The pane switches from the project overview to its version list in place;
  /// installing never opens a dialog.
  bool _showVersions = false;
  List<ResourceVersion>? _versions;
  bool _versionsLoading = false;

  /// Versions currently being planned (planning hits the network before the
  /// background task exists).
  final Set<String> _preparing = <String>{};
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
    final tokens = context.tokens;
    final instance = ref.watch(selectedInstanceProvider);
    final target = _targetFor(instance, l10n);

    final list = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _SearchHeader(
          l10n: l10n,
          controller: _queryController,
          target: target,
          busy: _loading,
          installing: _preparing.isNotEmpty,
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

    return LayoutBuilder(
      builder: (context, constraints) {
        final pane = _buildPane(context);
        if (pane == null) return list;
        // Side-by-side browsing is the point of the pane; below this width the
        // pane takes over the tab instead of squeezing the list.
        if (constraints.maxWidth < 1120) return pane;
        return Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Expanded(child: list),
            Container(width: 1, color: tokens.border),
            SizedBox(width: 380, child: pane),
          ],
        );
      },
    );
  }

  Widget? _buildPane(BuildContext context) {
    final project = _selected;
    if (project == null) return null;

    if (_showVersions) {
      return _VersionPane(
        project: project,
        versions: _versions,
        loading: _versionsLoading,
        preparingVersionId: _preparing.isEmpty ? null : _preparing.first,
        onBack: () => setState(() {
          _showVersions = false;
          _versions = null;
          _preparing.clear();
        }),
        onClose: _closePane,
        onInstall: _installVersion,
      );
    }

    return _ProjectPane(
      key: ValueKey(project.id),
      project: project,
      onClose: _closePane,
      onInstall: _openVersions,
    );
  }

  void _closePane() {
    setState(() {
      _selected = null;
      _showVersions = false;
      _versions = null;
      _preparing.clear();
    });
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
            icon: AppIcons.offline,
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
                      icon: const Icon(AppIcons.chevronDown, size: 18),
                      label: Text(l10n.downloadLoadMore),
                    ),
            ),
          );
        }

        final project = _items[index];
        return _ResourceRow(
          project: project,
          selected: _selected?.id == project.id,
          onTap: () => _selectProject(project),
          onInstall: () => _selectProject(project, openVersions: true),
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

  void _selectProject(ResourceProject project, {bool openVersions = false}) {
    if (_selected?.id == project.id && !openVersions) {
      _closePane();
      return;
    }
    setState(() {
      _selected = project;
      _showVersions = false;
      _versions = null;
      _versionsLoading = false;
      _preparing.clear();
    });
    if (openVersions) {
      _openVersions();
    }
  }

  /// Loads the compatible versions into the pane. No modal sheet: the list
  /// replaces the project overview and a back action returns to it.
  Future<void> _openVersions() async {
    final project = _selected;
    if (project == null) return;
    final l10n = AppLocalizations.of(context);
    final client = ref.read(coreClientProvider);
    final instance = ref.read(selectedInstanceProvider);

    if (client == null) {
      _notify(l10n.commonCoreNotConnected, type: ToastType.error);
      return;
    }
    if (widget.kind != 'modpack') {
      if (instance == null) {
        _notify(l10n.selectInstance, type: ToastType.warning);
        return;
      }
      if (widget.kind == 'mod' && instance.loader == GameLoader.vanilla) {
        _notify(l10n.resourceNeedsLoader, type: ToastType.warning);
        return;
      }
    }

    setState(() {
      _showVersions = true;
      _versionsLoading = true;
      _versions = null;
    });

    try {
      // A modpack brings its own game version and loader, so it is listed
      // without instance facets; mods and shaders must match the target.
      final pack = widget.kind == 'modpack';
      final versions = await client.modrinthVersions(
        projectId: project.id,
        kind: widget.kind,
        gameVersion: pack ? null : instance?.gameVersion,
        loader: pack || instance == null
            ? null
            : loaderSlug(instance.loader),
        shaderLoader: widget.kind == 'shader' ? 'iris' : null,
      );
      if (!mounted || _selected?.id != project.id) return;
      setState(() {
        _versions = versions;
        _versionsLoading = false;
      });
      if (versions.isEmpty) {
        _notify(l10n.downloadNoVersions, type: ToastType.warning);
      }
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _versionsLoading = false;
        _versions = const [];
      });
      _notify(error.toString(), type: ToastType.error);
    }
  }

  /// Starts the install as a background task and closes the pane.
  Future<void> _installVersion(ResourceVersion version) async {
    final project = _selected;
    if (project == null) return;
    final l10n = AppLocalizations.of(context);
    final instance = ref.read(selectedInstanceProvider);

    if (widget.kind != 'modpack') {
      if (instance == null) {
        _notify(l10n.selectInstance, type: ToastType.warning);
        return;
      }
      if (widget.kind == 'mod' && instance.loader == GameLoader.vanilla) {
        _notify(l10n.resourceNeedsLoader, type: ToastType.warning);
        return;
      }
      if (instance.rootPath.isEmpty) {
        _notify(l10n.selectInstance, type: ToastType.warning);
        return;
      }
    }

    setState(() => _preparing.add(version.id));
    try {
      final downloads = ref.read(downloadsProvider.notifier);
      if (widget.kind == 'modpack') {
        await downloads.installModpackFromModrinth(
          projectId: project.id,
          versionId: version.id,
          label: project.name,
          successMessage: l10n.packInstallDone,
          failureMessage: l10n.packInstallFailed,
          cancelledMessage: l10n.downloadCancelled(project.name),
        );
      } else {
        await downloads.installResource(
          project: project,
          version: version,
          instance: instance!,
          kind: widget.kind,
          successMessage: l10n.downloadFinished(project.name),
          failureMessage: l10n.downloadFailed(project.name),
          cancelledMessage: l10n.downloadCancelled(project.name),
        );
      }
      if (!mounted) return;
      _notify(l10n.downloadQueued(project.name), type: ToastType.success);
      _closePane();
    } catch (error) {
      _notify(error.toString(), type: ToastType.error);
    } finally {
      if (mounted) setState(() => _preparing.remove(version.id));
    }
  }

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
        : (instance == null ? 'fabric' : loaderSlug(instance.loader));
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

  void _notify(String message, {ToastType type = ToastType.info}) {
    toast(message, type: type);
  }
}
