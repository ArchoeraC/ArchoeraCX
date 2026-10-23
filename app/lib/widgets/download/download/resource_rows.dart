part of '../resource_search_panel.dart';

class _ResourceRow extends StatelessWidget {
  const _ResourceRow({
    required this.project,
    this.selected = false,
    this.onTap,
    this.onInstall,
  });

  final ResourceProject project;
  final bool selected;
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
            color: selected ? tokens.accentSoft : null,
            border: Border(
              left: BorderSide(
                color: selected ? tokens.accent : Colors.transparent,
                width: 2,
              ),
              bottom: BorderSide(color: tokens.border),
            ),
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
                          icon: AppIcons.fileDownload,
                          text: _formatCount(project.downloads),
                        ),
                        _MetaText(
                          icon: AppIcons.favorite,
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
