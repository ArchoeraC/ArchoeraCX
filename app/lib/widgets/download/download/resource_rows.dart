part of '../resource_search_panel.dart';

class _ResourceRow extends StatelessWidget {
  const _ResourceRow({
    required this.project,
    this.onTap,
    this.onInstall,
  });

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
              ResourceProjectThumb(project: project, size: 56),
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
                        ResourceMetaText(
                          icon: AppIcons.fileDownload,
                          text: formatResourceCount(project.downloads),
                        ),
                        ResourceMetaText(
                          icon: AppIcons.favorite,
                          text: formatResourceCount(project.follows),
                        ),
                        for (final category in project.categories.take(2))
                          ResourceTag(label: category),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(width: AppSpacing.xs),
              IconButton(
                onPressed: onInstall,
                tooltip: l10n.downloadInstall,
                icon: const Icon(AppIcons.download, size: 20),
              ),
              Icon(AppIcons.chevronRight, color: tokens.textTertiary),
            ],
          ),
        ),
      ),
    );
  }
}
