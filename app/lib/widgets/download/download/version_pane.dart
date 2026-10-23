part of '../resource_search_panel.dart';

class _VersionPane extends StatelessWidget {
  const _VersionPane({
    required this.project,
    required this.versions,
    required this.loading,
    required this.preparingVersionId,
    required this.onBack,
    required this.onClose,
    required this.onInstall,
  });

  final ResourceProject project;
  final List<ResourceVersion>? versions;
  final bool loading;
  final String? preparingVersionId;
  final VoidCallback onBack;
  final VoidCallback onClose;
  final ValueChanged<ResourceVersion> onInstall;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final items = versions ?? const <ResourceVersion>[];

    return DecoratedBox(
      decoration: BoxDecoration(color: tokens.surface1),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(
              AppSpacing.xxs,
              AppSpacing.xs,
              AppSpacing.xxs,
              AppSpacing.xs,
            ),
            child: Row(
              children: [
                IconButton(
                  onPressed: onBack,
                  tooltip: MaterialLocalizations.of(context).backButtonTooltip,
                  visualDensity: VisualDensity.compact,
                  icon: const Icon(AppIcons.back, size: 18),
                ),
                const SizedBox(width: AppSpacing.xxs),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        project.name,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.titleSmall,
                      ),
                      Text(
                        l10n.downloadSelectVersion,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.labelSmall?.copyWith(
                          color: tokens.textTertiary,
                        ),
                      ),
                    ],
                  ),
                ),
                IconButton(
                  onPressed: onClose,
                  tooltip: l10n.commonClose,
                  visualDensity: VisualDensity.compact,
                  icon: const Icon(AppIcons.close, size: 18),
                ),
              ],
            ),
          ),
          Divider(height: 1, color: tokens.border),
          Expanded(
            child: loading
                ? const Center(child: CircularProgressIndicator())
                : items.isEmpty
                ? Center(
                    child: Padding(
                      padding: const EdgeInsets.all(AppSpacing.lg),
                      child: Text(
                        l10n.downloadNoVersions,
                        textAlign: TextAlign.center,
                        style: textTheme.bodySmall?.copyWith(
                          color: tokens.textSecondary,
                        ),
                      ),
                    ),
                  )
                : ListView.separated(
                    padding: const EdgeInsets.only(bottom: AppSpacing.md),
                    itemCount: items.length,
                    separatorBuilder: (_, _) =>
                        Divider(height: 1, color: tokens.border),
                    itemBuilder: (context, index) {
                      final version = items[index];
                      final file = version.primaryFile;
                      final preparing = preparingVersionId == version.id;
                      return ListTile(
                        contentPadding: const EdgeInsets.symmetric(
                          horizontal: AppSpacing.sm,
                        ),
                        title: Text(
                          version.name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: textTheme.titleSmall,
                        ),
                        subtitle: Text(
                          '${version.versionNumber} · '
                          '${_versionTypeLabel(l10n, version.versionType)}'
                          '${file == null ? '' : ' · ${formatBytes(file.size)}'}',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        trailing: preparing
                            ? const SizedBox(
                                width: 16,
                                height: 16,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(AppIcons.download, size: 18),
                        onTap: preparing ? null : () => onInstall(version),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}
