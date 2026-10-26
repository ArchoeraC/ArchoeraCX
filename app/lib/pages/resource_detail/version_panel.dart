part of '../resource_detail_page.dart';

/// Compatible versions plus the install target.
///
/// The list lives on the page (not in a dialog) so the overview and the
/// version picker share the window instead of competing for a narrow pane.
/// Large projects can return hundreds of versions; only one page is built at a
/// time so opening the page stays responsive.
class _VersionPanel extends StatefulWidget {
  const _VersionPanel({
    required this.versions,
    required this.loading,
    required this.error,
    required this.preparing,
    required this.target,
    required this.onInstall,
    required this.onRetry,
  });

  final List<ResourceVersion>? versions;
  final bool loading;
  final String? error;
  final Set<String> preparing;

  /// Instance the install lands in, or the "select an instance" hint.
  final String target;
  final ValueChanged<ResourceVersion> onInstall;
  final VoidCallback onRetry;

  @override
  State<_VersionPanel> createState() => _VersionPanelState();
}

class _VersionPanelState extends State<_VersionPanel> {
  static const _pageSize = 40;

  int _visible = _pageSize;

  @override
  void didUpdateWidget(covariant _VersionPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!identical(oldWidget.versions, widget.versions)) {
      _visible = _pageSize;
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final items = widget.versions ?? const <ResourceVersion>[];
    final shown = items.length > _visible
        ? items.sublist(0, _visible)
        : items;

    return AppPanel(
      padding: const EdgeInsets.all(AppSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(AppIcons.download, size: 18, color: tokens.accent),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(
                  l10n.downloadSelectVersion,
                  style: textTheme.titleMedium,
                ),
              ),
              IconButton(
                onPressed: widget.loading ? null : widget.onRetry,
                tooltip: l10n.refresh,
                visualDensity: VisualDensity.compact,
                icon: const Icon(AppIcons.refresh, size: 18),
              ),
            ],
          ),
          Text(
            widget.target,
            style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
          ),
          const SizedBox(height: AppSpacing.md),
          if (widget.loading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: AppSpacing.xl),
              child: Center(
                child: SizedBox(
                  width: 24,
                  height: 24,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            )
          else if (widget.error != null)
            _VersionLoadError(message: widget.error!, onRetry: widget.onRetry)
          else if (items.isEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: AppSpacing.lg),
              child: Text(
                l10n.downloadNoVersions,
                textAlign: TextAlign.center,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
            )
          else ...[
            for (var index = 0; index < shown.length; index++) ...[
              if (index > 0) Divider(height: 1, color: tokens.border),
              _VersionRow(
                version: shown[index],
                preparing: widget.preparing.contains(shown[index].id),
                onInstall: widget.onInstall,
              ),
            ],
            if (items.length > shown.length) ...[
              Divider(height: 1, color: tokens.border),
              TextButton.icon(
                onPressed: () => setState(() => _visible += _pageSize),
                icon: const Icon(AppIcons.chevronDown, size: 18),
                label: Text(l10n.downloadLoadMore),
              ),
            ],
          ],
        ],
      ),
    );
  }
}

class _VersionRow extends StatelessWidget {
  const _VersionRow({
    required this.version,
    required this.preparing,
    required this.onInstall,
  });

  final ResourceVersion version;
  final bool preparing;
  final ValueChanged<ResourceVersion> onInstall;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final file = version.primaryFile;
    final details = <String>[
      if (version.versionNumber.isNotEmpty) version.versionNumber,
      resourceVersionTypeLabel(l10n, version.versionType),
      if (file != null) formatBytes(file.size),
    ];
    final tags = <String>[
      ...version.gameVersions.take(4),
      ...version.loaders.take(3),
    ];

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: preparing ? null : () => onInstall(version),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      version.name,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.titleSmall,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      details.join(' · '),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.textTertiary,
                      ),
                    ),
                    if (tags.isNotEmpty) ...[
                      const SizedBox(height: AppSpacing.xxs),
                      Wrap(
                        spacing: AppSpacing.xxs,
                        runSpacing: 2,
                        children: [
                          for (final tag in tags) ResourceTag(label: tag),
                        ],
                      ),
                    ],
                    if (version.dependencies.isNotEmpty) ...[
                      const SizedBox(height: AppSpacing.xxs),
                      Text(
                        l10n.downloadDependencies(version.dependencies.length),
                        style: textTheme.labelSmall?.copyWith(
                          color: tokens.textTertiary,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              if (preparing)
                const SizedBox(
                  width: 40,
                  height: 40,
                  child: Padding(
                    padding: EdgeInsets.all(11),
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                )
              else
                IconButton.filledTonal(
                  onPressed: () => onInstall(version),
                  tooltip: l10n.downloadInstall,
                  icon: const Icon(AppIcons.download, size: 18),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _VersionLoadError extends StatelessWidget {
  const _VersionLoadError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          l10n.versionLoadFailed,
          style: textTheme.bodySmall?.copyWith(color: tokens.danger),
        ),
        const SizedBox(height: 2),
        Text(
          message,
          style: textTheme.labelSmall?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(height: AppSpacing.xs),
        Align(
          alignment: Alignment.centerLeft,
          child: TextButton(onPressed: onRetry, child: Text(l10n.refresh)),
        ),
      ],
    );
  }
}
