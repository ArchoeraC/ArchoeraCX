import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/game_instance.dart';
import '../../services/core/resource_models.dart';
import '../../stores/instances.dart';
import '../../stores/providers.dart';
import '../../stores/settings.dart';
import '../../theme/tokens.dart';
import '../../utils/app_paths.dart';
import '../common/app_card.dart';
import '../common/app_empty_state.dart';

/// Real Modrinth search + install surface for one content tab.
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
    final tokens = context.tokens;
    final instance = ref.watch(selectedInstanceProvider);
    final target = _targetFor(instance);

    return ListView(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      children: [
        Row(
          children: [
            Expanded(
              child: TextField(
                controller: _queryController,
                textInputAction: TextInputAction.search,
                onSubmitted: (_) => _search(),
                decoration: InputDecoration(
                  hintText: l10n.searchDownload,
                  prefixIcon: const Icon(Icons.search_rounded, size: 20),
                ),
              ),
            ),
            const SizedBox(width: AppSpacing.xs),
            FilledButton(
              onPressed: _loading ? null : _search,
              child: Text(l10n.downloadSearchAction),
            ),
          ],
        ),
        const SizedBox(height: AppSpacing.xs),
        Text(
          target,
          style: Theme.of(context).textTheme.bodySmall
              ?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(height: AppSpacing.sm),
        if (_loading)
          const Padding(
            padding: EdgeInsets.all(AppSpacing.xl),
            child: Center(child: CircularProgressIndicator()),
          )
        else if (_error != null)
          AppEmptyState(
            icon: Icons.cloud_off_rounded,
            title: l10n.downloadSearchError,
            message: _error!,
          )
        else if (_items.isEmpty)
          AppEmptyState(
            icon: widget.icon,
            title: widget.title,
            message: l10n.downloadSearchEmpty,
          )
        else
          for (final project in _items)
            Padding(
              padding: const EdgeInsets.only(bottom: AppSpacing.xs),
              child: AppCard(
                compact: true,
                padding: const EdgeInsets.all(AppSpacing.sm),
                onTap: _installing ? null : () => _openVersions(project),
                child: Row(
                  children: [
                    _ProjectIcon(url: project.iconUrl),
                    const SizedBox(width: AppSpacing.sm),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Flexible(
                                child: Text(
                                  project.name,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: Theme.of(context).textTheme.titleSmall,
                                ),
                              ),
                              if (project.author != null) ...[
                                const SizedBox(width: AppSpacing.xs),
                                Text(
                                  project.author!,
                                  style: Theme.of(context).textTheme.bodySmall
                                      ?.copyWith(color: tokens.textTertiary),
                                ),
                              ],
                            ],
                          ),
                          const SizedBox(height: 2),
                          Text(
                            project.summary,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: Theme.of(context).textTheme.bodySmall
                                ?.copyWith(color: tokens.textSecondary),
                          ),
                          const SizedBox(height: 2),
                          Text(
                            '${_formatCount(project.downloads)} downloads',
                            style: Theme.of(context).textTheme.labelSmall
                                ?.copyWith(color: tokens.textTertiary),
                          ),
                        ],
                      ),
                    ),
                    if (_installing)
                      const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    else
                      Icon(Icons.download_rounded, color: tokens.textTertiary),
                  ],
                ),
              ),
            ),
      ],
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
      final settings = ref.read(appSettingsProvider).value;
      final downloadRoot = instanceRoot(
        explicitRoot: instance?.rootPath,
        instancesRoot: settings?.instancesRoot ?? fallbackInstancesRoot(),
        id: instance?.id ?? 'default',
      );
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

class _VersionSheet extends StatelessWidget {
  const _VersionSheet({required this.project, required this.versions});

  final ResourceProject project;
  final List<ResourceVersion> versions;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);

    return SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(
              AppSpacing.lg,
              0,
              AppSpacing.lg,
              AppSpacing.sm,
            ),
            child: Text(
              '${project.name} · ${l10n.downloadSelectVersion}',
              style: Theme.of(context).textTheme.titleMedium,
            ),
          ),
          Flexible(
            child: ListView.builder(
              shrinkWrap: true,
              itemCount: versions.length,
              itemBuilder: (context, index) {
                final version = versions[index];
                final file = version.primaryFile;
                return ListTile(
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
