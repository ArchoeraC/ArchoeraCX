import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../models/game_instance.dart';
import '../services/core/resources/resource_models.dart';
import '../stores/downloads.dart';
import '../stores/instances.dart';
import '../stores/providers.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../utils/format.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_network_image.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_panel.dart';
import '../widgets/common/app_progress_bar.dart';
import '../widgets/common/toast.dart';
import '../widgets/download/resource_common.dart';

part 'resource_detail/overview_panel.dart';
part 'resource_detail/version_panel.dart';

/// Loads the full provider record (body, gallery, license) for [projectId].
///
/// Search rows only carry the fields the list needs; the page asks for the
/// complete project so the description and gallery can be shown full width.
typedef ResourceProjectLoader =
    Future<ResourceProject> Function(String projectId);

/// Loads versions compatible with the page's kind and selected instance.
typedef ResourceVersionsLoader =
    Future<List<ResourceVersion>> Function(String projectId);

/// Full-page provider project: overview, gallery, description and the list of
/// compatible versions.
///
/// It is a route page rather than a side pane so the catalogue can keep its
/// search state underneath while the details use the whole content width.
class ResourceDetailPage extends ConsumerStatefulWidget {
  const ResourceDetailPage({
    super.key,
    required this.kind,
    required this.projectId,
    this.project,
    this.focusVersions = false,
    this.projectLoader,
    this.versionsLoader,
  });

  /// `mod`, `shader`, `resourcepack` or `modpack`.
  final String kind;
  final String projectId;

  /// Row that opened this page. Renders immediately while the full record
  /// loads; deep links start with `null` and show a loading state instead.
  final ResourceProject? project;

  /// Set by the row's install action: bring the version list into view once
  /// its layout exists.
  final bool focusVersions;

  /// Test seams, matching the Java page's injectable scan callback.
  final ResourceProjectLoader? projectLoader;
  final ResourceVersionsLoader? versionsLoader;

  @override
  ConsumerState<ResourceDetailPage> createState() => _ResourceDetailPageState();
}

class _ResourceDetailPageState extends ConsumerState<ResourceDetailPage> {
  final _versionsKey = GlobalKey();
  final _scroll = ScrollController();
  final Set<String> _preparing = <String>{};

  ResourceProject? _detail;
  bool _detailLoading = true;
  String? _detailError;

  List<ResourceVersion>? _versions;
  bool _versionsLoading = true;
  String? _versionsError;

  bool _started = false;
  bool _focusedVersions = false;
  String _versionsTarget = 'none';
  int _versionsRequest = 0;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    _versionsTarget = _targetKey(ref.read(selectedInstanceProvider));
    _loadProject();
    _loadVersions();
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _loadProject() async {
    setState(() {
      _detailLoading = true;
      _detailError = null;
    });
    try {
      final loader = widget.projectLoader ?? _defaultProjectLoader;
      final detail = await loader(widget.projectId);
      if (!mounted) return;
      setState(() {
        _detail = detail;
        _detailLoading = false;
      });
      _focusVersionsIfNeeded();
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _detailLoading = false;
        _detailError = error.toString();
      });
    }
  }

  Future<ResourceProject> _defaultProjectLoader(String projectId) {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      throw StateError(AppLocalizations.of(context).commonCoreNotConnected);
    }
    return client.modrinthProject(projectId: projectId);
  }

  Future<void> _loadVersions() async {
    final request = ++_versionsRequest;
    setState(() {
      _versionsLoading = true;
      _versionsError = null;
      _versions = null;
    });
    try {
      final loader = widget.versionsLoader ?? _defaultVersionsLoader;
      final versions = await loader(widget.projectId);
      if (!mounted || request != _versionsRequest) return;
      setState(() {
        _versions = versions;
        _versionsLoading = false;
      });
      _focusVersionsIfNeeded();
    } catch (error) {
      if (!mounted || request != _versionsRequest) return;
      setState(() {
        _versions = const [];
        _versionsLoading = false;
        _versionsError = error.toString();
      });
      _focusVersionsIfNeeded();
    }
  }

  Future<List<ResourceVersion>> _defaultVersionsLoader(String projectId) {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      throw StateError(AppLocalizations.of(context).commonCoreNotConnected);
    }
    final instance = ref.read(selectedInstanceProvider);
    final pack = widget.kind == 'modpack';
    return client.modrinthVersions(
      projectId: projectId,
      kind: widget.kind,
      gameVersion: pack ? null : instance?.gameVersion,
      loader: pack || instance == null ? null : loaderSlug(instance.loader),
      shaderLoader: widget.kind == 'shader' ? 'iris' : null,
    );
  }

  String _targetKey(GameInstance? instance) {
    if (widget.kind == 'modpack' || instance == null) return 'none';
    return '${instance.gameVersion}|${instance.loader.name}';
  }

  void _focusVersionsIfNeeded() {
    if (!widget.focusVersions || _focusedVersions) return;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || _focusedVersions) return;
      final target = _versionsKey.currentContext;
      if (target == null) return;
      _focusedVersions = true;
      Scrollable.ensureVisible(
        target,
        duration: const Duration(milliseconds: 300),
        curve: Curves.easeOutCubic,
      );
    });
  }

  Future<void> _install(ResourceVersion version) async {
    final project = _detail ?? widget.project;
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
    } catch (error) {
      if (!mounted) return;
      _notify(error.toString(), type: ToastType.error);
    } finally {
      if (mounted) setState(() => _preparing.remove(version.id));
    }
  }

  void _goBack() {
    final router = GoRouter.of(context);
    if (router.canPop()) {
      router.pop();
    } else {
      router.go('/download');
    }
  }

  void _notify(String message, {ToastType type = ToastType.info}) {
    toast(message, type: type);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final project = _detail ?? widget.project;
    final summary = project?.summary.trim();
    final instance = ref.watch(selectedInstanceProvider);

    // A different instance changes the compatible version facets; reload the
    // list instead of installing a build the new target cannot run.
    ref.listen<GameInstance?>(selectedInstanceProvider, (_, next) {
      final key = _targetKey(next);
      if (key == _versionsTarget) return;
      _versionsTarget = key;
      _loadVersions();
    });

    return SingleChildScrollView(
      controller: _scroll,
      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppPageHeader(
            leading: IconButton.filledTonal(
              onPressed: _goBack,
              tooltip: l10n.back,
              icon: const Icon(AppIcons.back, size: 18),
            ),
            title: project?.name ?? l10n.downloadTitle,
            subtitle: summary == null || summary.isEmpty ? null : summary,
          ),
          if (_detailLoading || _versionsLoading) ...[
            const SizedBox(height: AppSpacing.md),
            const AppProgressBar(height: 3),
          ],
          const SizedBox(height: AppSpacing.lg),
          if (project == null)
            Padding(
              padding: const EdgeInsets.only(top: AppSpacing.xl),
              child: AppEmptyState(
                icon: AppIcons.offline,
                title: l10n.downloadProjectLoadFailed,
                message: _detailError ?? l10n.commonCoreNotConnected,
                primaryAction: FilledButton(
                  onPressed: _loadProject,
                  child: Text(l10n.refresh),
                ),
              ),
            )
          else
            LayoutBuilder(
              builder: (context, constraints) {
                final overview = _ProjectOverview(
                  project: project,
                  error: _detailError,
                  onRetry: _loadProject,
                );
                final versions = KeyedSubtree(
                  key: _versionsKey,
                  child: _VersionPanel(
                    versions: _versions,
                    loading: _versionsLoading,
                    error: _versionsError,
                    preparing: _preparing,
                    target: resourceInstallTarget(
                      l10n,
                      kind: widget.kind,
                      instance: instance,
                    ),
                    onInstall: _install,
                    onRetry: _loadVersions,
                  ),
                );

                if (constraints.maxWidth < 1040) {
                  return Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      overview,
                      const SizedBox(height: AppSpacing.lg),
                      versions,
                    ],
                  );
                }
                return Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(child: overview),
                    const SizedBox(width: AppSpacing.lg),
                    SizedBox(width: 420, child: versions),
                  ],
                );
              },
            ),
        ],
      ),
    );
  }
}
