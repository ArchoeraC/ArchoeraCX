import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../l10n/l10n_labels.dart';
import '../models/game_instance.dart';
import '../services/core/resources/resource_models.dart';
import '../stores/downloads.dart';
import '../stores/instances.dart';
import '../stores/providers.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../utils/format.dart';
import '../widgets/common/app_dropdown.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_network_image.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_progress_bar.dart';
import '../widgets/common/app_section.dart';
import '../widgets/common/toast.dart';
import '../widgets/download/resource_common.dart';

part 'resource_detail/overview_panel.dart';
part 'resource_detail/version_panel.dart';

/// Loads the full provider record (body, gallery, license) for [projectId].
///
/// Search rows only carry the fields the list needs; the page asks for the
/// complete project so the description and gallery can be shown full width.
typedef ResourceProjectLoader = Future<ResourceProject> Function(
  String projectId,
);

/// Loads versions for the page's kind and the selected game version/loader.
///
/// Both facets are optional: resource packs and shaders fix the loaders at the
/// module boundary, while modpacks ignore the facets entirely. Passing them
/// explicitly lets the page offer its own target controls instead of silently
/// inheriting whichever instance happened to be selected when it opened.
typedef ResourceVersionsLoader = Future<List<ResourceVersion>> Function(
  String projectId, {
  String? gameVersion,
  String? loader,
});

/// Loader slugs the download target can actually install into.
const _installableLoaders = <String>{'fabric', 'forge', 'neoforge', 'quilt'};

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

  /// Version the user picked. Empty until the first page of versions lands;
  /// the newest listed build is selected then so the primary action is usable
  /// without a second tap.
  String? _selectedVersionId;

  /// Facets sent to `resource.versions`. They follow the target instance by
  /// default but stay editable so the page can list another game version.
  String? _gameVersion;
  String? _loader;

  bool _started = false;
  bool _versionsStarted = false;
  bool _focusedVersions = false;
  String _versionsTarget = 'none';
  int _versionsRequest = 0;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    final instance = ref.read(selectedInstanceProvider);
    _versionsTarget = _targetKey(instance);
    _gameVersion = _defaultGameVersion(instance);
    _loader = _defaultLoader(instance);
    _loadProject();
    _maybeLoadVersions();
  }

  /// Deep links start without the row's project, so they may not know a game
  /// version until `resource.project` lands. Wait for that instead of sending
  /// a request the module would reject for a missing `gameVersion`.
  void _maybeLoadVersions() {
    if (_versionsStarted) return;
    if (widget.kind != 'modpack' && _gameVersion == null) return;
    _versionsStarted = true;
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
        // A deep link starts without the row's facets. Fill them from the
        // resolved project once it arrives and its version list is reloaded.
        _gameVersion ??= _defaultGameVersion(
          ref.read(selectedInstanceProvider),
        );
        _loader ??= _defaultLoader(ref.read(selectedInstanceProvider));
      });
      _maybeLoadVersions();
      if (!_versionsStarted) {
        // The project lists no game versions and no instance is selected, so
        // there is no valid facet to query. Settle the panel on its empty
        // state instead of leaving a spinner that can never finish.
        setState(() {
          _versionsStarted = true;
          _versions = const [];
          _versionsLoading = false;
        });
      }
      _focusVersionsIfNeeded();
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _detailLoading = false;
        _detailError = error.toString();
      });
      // Without the project there is nothing to filter on; stop the panel's
      // spinner instead of leaving it waiting for a request that never starts.
      if (!_versionsStarted) {
        setState(() {
          _versionsStarted = true;
          _versionsLoading = false;
          _versionsError = error.toString();
        });
      }
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
    _versionsStarted = true;
    final request = ++_versionsRequest;
    setState(() {
      _versionsLoading = true;
      _versionsError = null;
    });
    try {
      final loader = widget.versionsLoader ?? _defaultVersionsLoader;
      final versions = await loader(
        widget.projectId,
        gameVersion: _gameVersion,
        loader: _loader,
      );
      if (!mounted || request != _versionsRequest) return;
      setState(() {
        _versions = versions;
        _versionsLoading = false;
        _selectedVersionId = _pickSelectedVersion(versions);
      });
      _focusVersionsIfNeeded();
    } catch (error) {
      if (!mounted || request != _versionsRequest) return;
      setState(() {
        _versions = const [];
        _versionsLoading = false;
        _versionsError = error.toString();
        _selectedVersionId = null;
      });
      _focusVersionsIfNeeded();
    }
  }

  Future<List<ResourceVersion>> _defaultVersionsLoader(
    String projectId, {
    String? gameVersion,
    String? loader,
  }) {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      throw StateError(AppLocalizations.of(context).commonCoreNotConnected);
    }
    if (widget.kind == 'modpack') {
      return client.modrinthVersions(projectId: projectId, kind: widget.kind);
    }
    return client.modrinthVersions(
      projectId: projectId,
      kind: widget.kind,
      gameVersion: gameVersion,
      loader: widget.kind == 'mod' ? loader : null,
      shaderLoader: widget.kind == 'shader' ? 'iris' : null,
    );
  }

  String? _defaultGameVersion(GameInstance? instance) {
    final target = instance?.gameVersion.trim();
    if (target != null && target.isNotEmpty) return target;
    final project = _detail ?? widget.project;
    final versions = project?.gameVersions ?? const <String>[];
    if (versions.isNotEmpty) {
      return versions.reduce((a, b) => _compareGameVersions(a, b) >= 0 ? a : b);
    }
    return null;
  }

  String? _defaultLoader(GameInstance? instance) {
    if (widget.kind != 'mod') return null;
    if (instance != null && instance.loader != GameLoader.vanilla) {
      return loaderSlug(instance.loader);
    }
    final project = _detail ?? widget.project;
    final loaders = project?.loaders ?? const <String>[];
    for (final loader in loaders) {
      final slug = loader.trim().toLowerCase();
      if (_installableLoaders.contains(slug)) return slug;
    }
    return 'fabric';
  }

  String _pickSelectedVersion(List<ResourceVersion> versions) {
    final current = _selectedVersionId;
    if (current != null && versions.any((version) => version.id == current)) {
      return current;
    }
    return versions.isEmpty ? '' : versions.first.id;
  }

  void _selectGameVersion(String value) {
    if (value == _gameVersion) return;
    setState(() {
      _gameVersion = value;
      _selectedVersionId = null;
    });
    _loadVersions();
  }

  void _selectLoader(String value) {
    if (value == _loader) return;
    setState(() {
      _loader = value;
      _selectedVersionId = null;
    });
    _loadVersions();
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

  ResourceVersion? _selectedVersion() {
    final versions = _versions;
    final id = _selectedVersionId;
    if (versions == null || id == null || id.isEmpty) return null;
    for (final version in versions) {
      if (version.id == id) return version;
    }
    return null;
  }

  /// True when [version] can be written into [instance].
  ///
  /// The facet controls above the list may point at another game version than
  /// the target instance; installing that build would either be rejected by
  /// the C# plan builder or land in a mismatched instance. Check the same
  /// provider fields here so the footer can explain the problem before a task
  /// is submitted.
  bool _versionMatchesTarget(ResourceVersion version, GameInstance instance) {
    if (widget.kind == 'modpack') return true;

    final game = instance.gameVersion.trim();
    if (game.isNotEmpty &&
        version.gameVersions.isNotEmpty &&
        !version.gameVersions.contains(game)) {
      return false;
    }

    switch (widget.kind) {
      case 'mod':
        if (instance.loader == GameLoader.vanilla) return false;
        final slug = loaderSlug(instance.loader);
        return version.loaders.isEmpty ||
            version.loaders.any((loader) => loader.toLowerCase() == slug);
      case 'shader':
        return version.loaders.isEmpty ||
            version.loaders.any(
              (loader) =>
                  loader.toLowerCase() == 'iris' ||
                  loader.toLowerCase() == 'optifine',
            );
      default:
        return true;
    }
  }

  String? _installHint(
    AppLocalizations l10n,
    GameInstance? instance,
    ResourceVersion? version,
  ) {
    if (widget.kind == 'modpack' || instance == null || version == null) {
      return null;
    }
    if (widget.kind == 'mod' && instance.loader == GameLoader.vanilla) {
      return l10n.resourceNeedsLoader;
    }
    if (!_versionMatchesTarget(version, instance)) {
      return l10n.downloadVersionIncompatible;
    }
    return null;
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
      if (!_versionMatchesTarget(version, instance)) {
        _notify(l10n.downloadVersionIncompatible, type: ToastType.warning);
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

  /// Game versions offered by the filter, newest first, including the target
  /// instance's version even when the provider facets omit it.
  List<String> _gameVersionOptions(
    ResourceProject? project,
    GameInstance? instance,
  ) {
    final values = <String>[];
    void add(String? candidate) {
      final value = candidate?.trim();
      if (value == null || value.isEmpty || values.contains(value)) return;
      values.add(value);
    }

    add(instance?.gameVersion);
    for (final version in project?.gameVersions ?? const <String>[]) {
      add(version);
    }
    add(_gameVersion);
    values.sort((a, b) => _compareGameVersions(b, a));
    return values;
  }

  /// Loaders offered by the filter. Only loader families the downloader can
  /// target are listed; provider-only labels such as "iris" stay out of the
  /// mod facet and are fixed by the shader path instead.
  List<String> _loaderOptions(
    ResourceProject? project,
    GameInstance? instance,
  ) {
    final values = <String>[];
    void add(String? candidate) {
      final value = candidate?.trim().toLowerCase();
      if (value == null ||
          value.isEmpty ||
          !_installableLoaders.contains(value) ||
          values.contains(value)) {
        return;
      }
      values.add(value);
    }

    if (instance != null && instance.loader != GameLoader.vanilla) {
      add(loaderSlug(instance.loader));
    }
    for (final loader in project?.loaders ?? const <String>[]) {
      add(loader);
    }
    add(_loader);
    return values;
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final project = _detail ?? widget.project;
    final summary = project?.summary.trim();
    final instance = ref.watch(selectedInstanceProvider);
    final loadedInstances =
        ref.watch(instancesProvider).value ?? const <GameInstance>[];
    // The selected instance can arrive before the list finishes loading (or
    // be injected by tests); keep it in the picker so its id always resolves.
    final instances = [
      if (instance != null &&
          !loadedInstances.any((item) => item.id == instance.id))
        instance,
      ...loadedInstances,
    ];
    final selected = _selectedVersion();

    // A different instance changes the install target and the compatible
    // facets; follow it unless the page explicitly picked another target.
    ref.listen<GameInstance?>(selectedInstanceProvider, (_, next) {
      final key = _targetKey(next);
      if (key == _versionsTarget) return;
      _versionsTarget = key;
      _gameVersion = _defaultGameVersion(next);
      _loader = _defaultLoader(next);
      _selectedVersionId = null;
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
                    selected: selected,
                    onSelect: (version) {
                      setState(() => _selectedVersionId = version.id);
                    },
                    gameVersions: widget.kind == 'modpack'
                        ? const <String>[]
                        : _gameVersionOptions(project, instance),
                    gameVersion: _gameVersion,
                    onGameVersionChanged: _selectGameVersion,
                    loaders: widget.kind == 'mod'
                        ? _loaderOptions(project, instance)
                        : const <String>[],
                    loader: _loader,
                    onLoaderChanged: _selectLoader,
                    instances: widget.kind == 'modpack'
                        ? const <GameInstance>[]
                        : instances,
                    targetInstanceId: instance?.id,
                    onTargetChanged: (id) {
                      ref.read(selectedInstanceIdProvider.notifier).select(id);
                    },
                    target: resourceInstallTarget(
                      l10n,
                      kind: widget.kind,
                      instance: instance,
                    ),
                    installHint: _installHint(l10n, instance, selected),
                    installing:
                        selected != null && _preparing.contains(selected.id),
                    onInstall: selected == null
                        ? null
                        : () => _install(selected),
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
                    const SizedBox(width: AppSpacing.xl),
                    Container(width: 1, color: context.tokens.border),
                    const SizedBox(width: AppSpacing.xl),
                    SizedBox(width: 440, child: versions),
                  ],
                );
              },
            ),
        ],
      ),
    );
  }
}

/// Natural comparison for Minecraft version ids ("1.21.1" > "1.9").
///
/// Provider facets arrive in no guaranteed order; the page uses this to pick
/// the newest default and to keep the filter list readable. Snapshot ids still
/// compare deterministically, they simply do not carry release semantics.
int _compareGameVersions(String left, String right) {
  final pattern = RegExp(r'\d+|\D+');
  final a = pattern
      .allMatches(left.toLowerCase())
      .map((match) => match.group(0)!)
      .toList(growable: false);
  final b = pattern
      .allMatches(right.toLowerCase())
      .map((match) => match.group(0)!)
      .toList(growable: false);

  for (var index = 0; index < a.length && index < b.length; index++) {
    final leftPart = a[index];
    final rightPart = b[index];
    final leftNumber = int.tryParse(leftPart);
    final rightNumber = int.tryParse(rightPart);
    final compared = leftNumber != null && rightNumber != null
        ? leftNumber.compareTo(rightNumber)
        : leftPart.compareTo(rightPart);
    if (compared != 0) return compared;
  }
  return a.length.compareTo(b.length);
}
