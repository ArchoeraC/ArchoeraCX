import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../services/core/resources/resource_models.dart';
import '../../stores/downloads.dart';
import '../../stores/instances.dart';
import '../../stores/providers.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/app_empty_state.dart';
import '../common/app_menu_button.dart';
import '../common/app_section.dart';
import '../common/toast.dart';
import 'resource_common.dart';

part 'download/search_header.dart';
part 'download/resource_rows.dart';

/// Real Modrinth search surface for one content tab.
///
/// Layout is a fixed header (query, target, sort) over an independently
/// scrolling result list, so the search stays reachable while browsing. Rows
/// open the project page; its install action opens the same page scrolled to
/// the compatible version list.
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
          onTap: () => _openProject(project),
          onInstall: () => _openProject(project, versions: true),
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

  /// Opens the full project page on the download branch's navigator.
  ///
  /// `push` keeps this panel (query, results and scroll position) mounted
  /// underneath, so returning from the project page lands exactly where the
  /// user left the catalogue.
  void _openProject(ResourceProject project, {bool versions = false}) {
    final location = Uri(
      path:
          '/download/project/${widget.kind}/${Uri.encodeComponent(project.id)}',
      queryParameters: versions ? const {'versions': '1'} : null,
    ).toString();
    unawaited(context.push(location, extra: project));
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
