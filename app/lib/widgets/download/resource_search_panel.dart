import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/game_instance.dart';
import '../../services/core/resource_models.dart';
import '../../stores/instances.dart';
import '../../stores/providers.dart';
import '../../theme/tokens.dart';
import '../common/app_section.dart';
import '../common/app_empty_state.dart';

/// Real Modrinth search + install surface for one content tab.
///
/// Layout is a fixed header (query + target) over an independently scrolling
/// result list, so the search stays reachable while browsing.
class ResourceSearchPanel extends ConsumerStatefulWidget {
  const ResourceSearchPanel({
    super.key,
    required this.kind,
    required this.title,
    required this.icon,
  });

  /// `mod`, `shader` or `resourcepack`.
  final String kind;
  final String title;
  final IconData icon;

  @override
  ConsumerState<ResourceSearchPanel> createState() =>
      _ResourceSearchPanelState();
}

class _ResourceSearchPanelState extends ConsumerState<ResourceSearchPanel> {
  final _queryController = TextEditingController();
  List<ResourceProject> _items = const [];
  bool _loading = false;
  bool _installing = false;
  String? _error;

  @override
  void dispose() {
    _queryController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final instance = ref.watch(selectedInstanceProvider);
    final target = _targetFor(instance);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _SearchHeader(
          l10n: l10n,
          controller: _queryController,
          target: target,
          busy: _loading,
          installing: _installing,
          onSearch: _search,
        ),
        const SizedBox(height: AppSpacing.sm),
        Expanded(child: _buildResults(l10n)),
      ],
    );
  }

  Widget _buildResults(AppLocalizations l10n) {
    if (_loading) {
      return const Center(child: CircularProgressIndicator());
    }

    if (_error != null) {
      return ListView(
        padding: const EdgeInsets.only(bottom: AppSpacing.lg),
        children: [
          AppEmptyState(
            icon: Icons.cloud_off_rounded,
            title: l10n.downloadSearchError,
            message: _error!,
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

    return ListView.separated(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      itemCount: _items.length,
      separatorBuilder: (_, _) => const SizedBox(height: AppSpacing.xs),
      itemBuilder: (context, index) {
        final project = _items[index];
        return _ResourceRow(
          project: project,
          onTap: _installing ? null : () => _openVersions(project),
        );
      },
    );
  }

  String _targetFor(GameInstance? instance) {
    final game = instance?.gameVersion ?? '1.21.1';
    final loader = switch (instance?.loader) {
      GameLoader.fabric => 'Fabric',
      GameLoader.forge => 'Forge',
      GameLoader.neoforge => 'NeoForge',
      GameLoader.quilt => 'Quilt',
      _ => 'Fabric',
    };
    return switch (widget.kind) {
      'shader' => 'Minecraft $game · Iris',
      'resourcepack' => 'Minecraft $game',
      _ => 'Minecraft $game · $loader',
    };
  }

  Future<void> _search() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(() => _error = 'Native core is not available.');
      return;
    }

    final instance = ref.read(selectedInstanceProvider);
    final game = instance?.gameVersion ?? '1.21.1';
    final loader = switch (instance?.loader) {
      GameLoader.fabric => 'fabric',
      GameLoader.forge => 'forge',
      GameLoader.neoforge => 'neoforge',
      GameLoader.quilt => 'quilt',
      _ => 'fabric',
    };

    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final result = await client.searchModrinth(
        query: _queryController.text,
        kind: widget.kind,
        gameVersion: game,
        loader: widget.kind == 'mod' ? loader : null,
        shaderLoader: widget.kind == 'shader' ? 'iris' : null,
      );
      if (!mounted) return;
      setState(() => _items = result.items);
    } catch (error) {
      if (!mounted) return;
      setState(() => _error = error.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _openVersions(ResourceProject project) async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      _notify('Native core is not available.');
      return;
    }

    final instance = ref.read(selectedInstanceProvider);
    final game = instance?.gameVersion ?? '1.21.1';
    final loader = switch (instance?.loader) {
      GameLoader.fabric => 'fabric',
      GameLoader.forge => 'forge',
      GameLoader.neoforge => 'neoforge',
      GameLoader.quilt => 'quilt',
      _ => 'fabric',
    };

    setState(() => _installing = true);
    try {
      final versions = await client.modrinthVersions(
        projectId: project.id,
        kind: widget.kind,
        gameVersion: game,
        loader: widget.kind == 'mod' ? loader : null,
        shaderLoader: widget.kind == 'shader' ? 'iris' : null,
      );
      if (!mounted) return;
      if (versions.isEmpty) {
        _notify(AppLocalizations.of(context).downloadNoVersions);
        return;
      }

      final selected = await showModalBottomSheet<ResourceVersion>(
        context: context,
        showDragHandle: true,
        builder: (context) =>
            _VersionSheet(project: project, versions: versions),
      );
      if (selected != null && mounted) {
        await _install(project, selected);
      }
    } catch (error) {
      if (mounted) _notify(error.toString());
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
    final game = instance?.gameVersion ?? '1.21.1';
    final loader = switch (instance?.loader) {
      GameLoader.fabric => 'fabric',
      GameLoader.forge => 'forge',
      GameLoader.neoforge => 'neoforge',
      GameLoader.quilt => 'quilt',
      _ => 'fabric',
    };

    setState(() => _installing = true);
    try {
      final downloadRoot = instance?.rootPath;
      if (downloadRoot == null || downloadRoot.isEmpty) {
        _notify(AppLocalizations.of(context).selectInstance);
        return;
      }
      final plan = await client.planModrinthDownload(
        projectId: project.id,
        versionId: version.id,
        kind: widget.kind,
        gameVersion: game,
        loader: widget.kind == 'mod' ? loader : null,
        shaderLoader: widget.kind == 'shader' ? 'iris' : null,
        root: downloadRoot,
      );
      if (!mounted) return;
      final result = await client.executeDownload(plan);
      if (!mounted) return;
      _notify(AppLocalizations.of(context).downloadResult(result.status));
    } catch (error) {
      if (mounted) _notify(error.toString());
    } finally {
      if (mounted) setState(() => _installing = false);
    }
  }

  void _notify(String message) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          behavior: SnackBarBehavior.floating,
          content: Text(message),
          duration: const Duration(seconds: 3),
        ),
      );
  }
}

/// Search field + install target. The target line explains which instance the
/// query is scoped to without adding another card.
class _SearchHeader extends StatelessWidget {
  const _SearchHeader({
    required this.l10n,
    required this.controller,
    required this.target,
    required this.busy,
    required this.installing,
    required this.onSearch,
  });

  final AppLocalizations l10n;
  final TextEditingController controller;
  final String target;
  final bool busy;
  final bool installing;
  final VoidCallback onSearch;

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
}

class _ResourceRow extends StatelessWidget {
  const _ResourceRow({required this.project, this.onTap});

  final ResourceProject project;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
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
              _ProjectIcon(url: project.iconUrl),
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
                              text: '  $author',
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
                    Row(
                      children: [
                        Icon(
                          Icons.file_download_outlined,
                          size: 13,
                          color: tokens.textTertiary,
                        ),
                        const SizedBox(width: AppSpacing.xxs),
                        Text(
                          _formatCount(project.downloads),
                          style: textTheme.labelSmall?.copyWith(
                            color: tokens.textTertiary,
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(width: AppSpacing.xs),
              Icon(Icons.chevron_right_rounded, color: tokens.textTertiary),
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
                return ListTile(
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: AppSpacing.lg,
                  ),
                  title: Text(version.name),
                  subtitle: Text(
                    '${version.versionNumber} · ${version.versionType}'
                    '${file == null ? '' : ' · ${_formatSize(file.size)}'}',
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

class _ProjectIcon extends StatelessWidget {
  const _ProjectIcon({required this.url});

  final String? url;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return ClipRRect(
      borderRadius: BorderRadius.circular(tokens.radiusM),
      child: SizedBox(
        width: 44,
        height: 44,
        child: url == null
            ? ColoredBox(
                color: tokens.surface2,
                child: Icon(Icons.extension_rounded, color: tokens.accent),
              )
            : Image.network(
                url!,
                fit: BoxFit.cover,
                errorBuilder: (_, _, _) => ColoredBox(
                  color: tokens.surface2,
                  child: Icon(Icons.extension_rounded, color: tokens.accent),
                ),
              ),
      ),
    );
  }
}

String _formatCount(int value) {
  if (value >= 1000000) return '${(value / 1000000).toStringAsFixed(1)}M';
  if (value >= 1000) return '${(value / 1000).toStringAsFixed(1)}K';
  return '$value';
}

String _formatSize(int bytes) {
  if (bytes >= 1024 * 1024) {
    return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
  }
  if (bytes >= 1024) return '${(bytes / 1024).toStringAsFixed(0)} KB';
  return '$bytes B';
}
