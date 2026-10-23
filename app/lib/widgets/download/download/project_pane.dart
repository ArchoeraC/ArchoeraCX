part of '../resource_search_panel.dart';

class _ProjectPane extends ConsumerStatefulWidget {
  const _ProjectPane({
    super.key,
    required this.project,
    required this.onClose,
    required this.onInstall,
  });

  final ResourceProject project;
  final VoidCallback onClose;
  final VoidCallback onInstall;

  @override
  ConsumerState<_ProjectPane> createState() => _ProjectPaneState();
}

class _ProjectPaneState extends ConsumerState<_ProjectPane> {
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

    return DecoratedBox(
      decoration: BoxDecoration(color: tokens.surface1),
      child: ConstrainedBox(
        // The pane shares the tab's height; the list scrolls on its own.
        constraints: const BoxConstraints(),
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
                  _ProjectThumb(project: project, size: 56),
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
                    onPressed: widget.onClose,
                    tooltip: l10n.commonClose,
                    icon: const Icon(AppIcons.close),
                  ),
                ],
              ),
            ),
            if (_loading) const AppProgressBar(height: 3),
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
                          icon: AppIcons.fileDownload,
                          text:
                              '${l10n.statDownloads}: ${_formatCount(project.downloads)}',
                        ),
                        _MetaText(
                          icon: AppIcons.favorite,
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
                                      fallbackIcon: AppIcons.imagePlaceholder,
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
                    onPressed: widget.onInstall,
                    icon: const Icon(AppIcons.download, size: 18),
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
                      fallbackIcon: AppIcons.imagePlaceholder,
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
                  icon: const Icon(AppIcons.close, color: Colors.white),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
