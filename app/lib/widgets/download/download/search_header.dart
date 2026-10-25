part of '../resource_search_panel.dart';

/// Search field + target + sort. The target line explains which instance the
/// query is scoped to without adding another card.
class _SearchHeader extends StatelessWidget {
  const _SearchHeader({
    required this.l10n,
    required this.controller,
    required this.target,
    required this.busy,
    required this.installing,
    required this.total,
    required this.sort,
    required this.onQueryChanged,
    required this.onSearch,
    required this.onSortChanged,
  });

  final AppLocalizations l10n;
  final TextEditingController controller;
  final String target;
  final bool busy;
  final bool installing;
  final int total;
  final String sort;
  final ValueChanged<String> onQueryChanged;
  final VoidCallback onSearch;
  final ValueChanged<String> onSortChanged;

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
                onChanged: onQueryChanged,
                onSubmitted: (_) => onSearch(),
                decoration: InputDecoration(
                  hintText: l10n.searchDownload,
                  prefixIcon: const Icon(AppIcons.search, size: 20),
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
                Icon(AppIcons.filter, size: 14, color: tokens.textTertiary),
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
                if (total > 0) ...[
                  Text(
                    l10n.downloadResults(total),
                    style: textTheme.labelSmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                  const SizedBox(width: AppSpacing.xs),
                ],
                AppMenuButton<String>(
                  tooltip: l10n.downloadSort,
                  initialValue: sort,
                  items: [
                    AppMenuEntry(
                      value: 'relevance',
                      label: l10n.downloadSortRelevance,
                    ),
                    AppMenuEntry(
                      value: 'downloads',
                      label: l10n.downloadSortDownloads,
                    ),
                    AppMenuEntry(
                      value: 'follows',
                      label: l10n.downloadSortFollows,
                    ),
                    AppMenuEntry(
                      value: 'newest',
                      label: l10n.downloadSortNewest,
                    ),
                    AppMenuEntry(
                      value: 'updated',
                      label: l10n.downloadSortUpdated,
                    ),
                  ],
                  onSelected: onSortChanged,
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(
                        AppIcons.sort,
                        size: 14,
                        color: tokens.textSecondary,
                      ),
                      const SizedBox(width: 2),
                      Text(
                        _sortLabel(l10n, sort),
                        style: textTheme.labelSmall?.copyWith(
                          color: tokens.textSecondary,
                        ),
                      ),
                    ],
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

  String _sortLabel(AppLocalizations l10n, String value) => switch (value) {
    'downloads' => l10n.downloadSortDownloads,
    'follows' => l10n.downloadSortFollows,
    'newest' => l10n.downloadSortNewest,
    'updated' => l10n.downloadSortUpdated,
    _ => l10n.downloadSortRelevance,
  };
}
