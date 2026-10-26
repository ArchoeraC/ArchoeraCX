part of '../resource_detail_page.dart';

/// Project overview: identity, statistics, gallery and the full description.
///
/// Page-level grouping follows the design system's flat sections: no filled
/// card, no shadow, one hairline rule below. The project page uses the full
/// content width, so wrapping these blocks in beveled panels made it read like
/// a dialog floating inside the download page.
class _ProjectOverview extends StatelessWidget {
  const _ProjectOverview({
    required this.project,
    required this.error,
    required this.onRetry,
  });

  final ResourceProject project;

  /// Non-null when the full record could not be loaded; the row data is still
  /// shown so the page never collapses back to an empty state.
  final String? error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final author = project.author?.trim();
    final body = project.body?.trim() ?? '';
    final gallery = project.gallery;

    return AppSection(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              ResourceProjectThumb(project: project, size: 96),
              const SizedBox(width: AppSpacing.lg),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (author != null && author.isNotEmpty) ...[
                      Text(
                        l10n.downloadByAuthor(author),
                        style: textTheme.bodyMedium?.copyWith(
                          color: tokens.textSecondary,
                        ),
                      ),
                      const SizedBox(height: AppSpacing.xs),
                    ],
                    Wrap(
                      spacing: AppSpacing.sm,
                      runSpacing: AppSpacing.xs,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        ResourceMetaText(
                          icon: AppIcons.fileDownload,
                          text:
                              '${l10n.statDownloads}: '
                              '${formatResourceCount(project.downloads)}',
                        ),
                        ResourceMetaText(
                          icon: AppIcons.favorite,
                          text: formatResourceCount(project.follows),
                        ),
                        if (project.license?.trim().isNotEmpty == true)
                          ResourceTag(label: project.license!),
                        for (final loader in project.loaders.take(4))
                          ResourceTag(label: loader),
                      ],
                    ),
                    if (project.categories.isNotEmpty) ...[
                      const SizedBox(height: AppSpacing.xs),
                      Wrap(
                        spacing: AppSpacing.xxs,
                        runSpacing: AppSpacing.xxs,
                        children: [
                          for (final category in project.categories.take(8))
                            ResourceTag(label: category),
                        ],
                      ),
                    ],
                  ],
                ),
              ),
            ],
          ),
          if (error != null) ...[
            const SizedBox(height: AppSpacing.md),
            _OverviewError(message: error!, onRetry: onRetry),
          ],
          if (gallery.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.lg),
            Text(l10n.downloadScreenshots, style: textTheme.titleSmall),
            const SizedBox(height: AppSpacing.sm),
            SizedBox(
              height: 220,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: gallery.length,
                separatorBuilder: (_, _) =>
                    const SizedBox(width: AppSpacing.sm),
                itemBuilder: (context, index) {
                  final image = gallery[index];
                  return Semantics(
                    label: l10n.downloadImagePreview,
                    button: true,
                    child: InkWell(
                      borderRadius: BorderRadius.circular(tokens.radiusM),
                      onTap: () => showResourceImagePreview(context, image),
                      child: ClipRRect(
                        borderRadius: BorderRadius.circular(tokens.radiusM),
                        child: SizedBox(
                          width: 330,
                          height: 220,
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
          if (body.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.lg),
            Text(l10n.downloadDescription, style: textTheme.titleSmall),
            const SizedBox(height: AppSpacing.sm),
            SelectableText(
              resourcePlainBody(body),
              style: textTheme.bodyMedium?.copyWith(
                color: tokens.textSecondary,
                height: 1.55,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _OverviewError extends StatelessWidget {
  const _OverviewError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Container(
      padding: const EdgeInsets.all(AppSpacing.sm),
      decoration: BoxDecoration(
        color: tokens.danger.withValues(alpha: 0.08),
        border: Border.all(color: tokens.danger.withValues(alpha: 0.35)),
        borderRadius: BorderRadius.circular(tokens.radiusS),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(AppIcons.warning, size: 18, color: tokens.danger),
          const SizedBox(width: AppSpacing.xs),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  l10n.downloadProjectLoadFailed,
                  style: textTheme.bodySmall?.copyWith(color: tokens.danger),
                ),
                const SizedBox(height: 2),
                Text(
                  message,
                  style: textTheme.labelSmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              ],
            ),
          ),
          TextButton(onPressed: onRetry, child: Text(l10n.refresh)),
        ],
      ),
    );
  }
}
