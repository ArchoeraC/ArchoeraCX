part of '../java_install_page.dart';

/// Target-platform row.
///
/// The catalogue is always queried for one OS/architecture triple. Users who
/// want a JDK for another machine can still pick it here; the page then warns
/// that the downloaded build cannot run on this device.
class _PlatformSelector extends StatelessWidget {
  const _PlatformSelector({
    required this.platforms,
    required this.host,
    required this.selected,
    required this.enabled,
    required this.onSelected,
    required this.l10n,
  });

  final List<JavaDistributionPlatformInfo> platforms;
  final JavaDistributionPlatformInfo host;
  final JavaDistributionPlatformInfo? selected;
  final bool enabled;
  final ValueChanged<JavaDistributionPlatformInfo> onSelected;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final target = selected ?? host;
    final foreign = !target.current;

    return AppSection(
      showTop: true,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(AppIcons.world, size: 17, color: tokens.accent),
              const SizedBox(width: AppSpacing.xxs),
              Text(
                l10n.javaInstallTargetPlatform,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Text(
                  l10n.javaInstallCurrentDevice(host.os, host.arch),
                  textAlign: TextAlign.end,
                  style: textTheme.labelSmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              ),
              if (foreign) ...[
                const SizedBox(width: AppSpacing.xs),
                _Badge(
                  label: l10n.javaInstallPlatformBadge,
                  color: tokens.danger,
                ),
              ],
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          Wrap(
            spacing: AppSpacing.xs,
            runSpacing: AppSpacing.xs,
            children: [
              for (final platform in platforms)
                _SelectChip(
                  label: '${platform.os} · ${platform.arch}',
                  selected:
                      platform.os == target.os &&
                      platform.arch == target.arch &&
                      platform.archive == target.archive,
                  enabled: enabled,
                  onTap: enabled ? () => onSelected(platform) : null,
                ),
            ],
          ),
          if (foreign) ...[
            const SizedBox(height: AppSpacing.sm),
            Container(
              padding: const EdgeInsets.symmetric(
                horizontal: AppSpacing.xs,
                vertical: AppSpacing.xs,
              ),
              decoration: BoxDecoration(
                color: tokens.danger.withValues(alpha: 0.08),
                border: Border(
                  left: BorderSide(color: tokens.danger, width: 2),
                ),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(AppIcons.warning, size: 16, color: tokens.danger),
                  const SizedBox(width: AppSpacing.xxs),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          l10n.javaInstallPlatformIncompatible,
                          style: textTheme.bodySmall?.copyWith(
                            color: tokens.danger,
                          ),
                        ),
                        Text(
                          l10n.javaInstallPlatformIncompatibleHint,
                          style: textTheme.bodySmall?.copyWith(
                            color: tokens.textTertiary,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// Vendor row: product name, publisher and the target triple the catalogue
/// was queried for.
class _ProviderSelector extends StatelessWidget {
  const _ProviderSelector({
    required this.catalog,
    required this.platform,
    required this.selected,
    required this.enabled,
    required this.onSelected,
    required this.l10n,
  });

  final JavaDistributionCatalog catalog;
  final JavaDistributionPlatformInfo? platform;
  final String? selected;
  final bool enabled;
  final ValueChanged<String> onSelected;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final target = platform ?? catalog.platform;

    return AppSection(
      showTop: true,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(AppIcons.java, size: 17, color: tokens.accent),
              const SizedBox(width: AppSpacing.xxs),
              Text(
                l10n.javaInstallProviderLabel,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
              const Spacer(),
              Text(
                l10n.javaInstallPlatform(
                  target.os,
                  target.arch,
                  target.archive,
                ),
                style: textTheme.labelSmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          Wrap(
            spacing: AppSpacing.xs,
            runSpacing: AppSpacing.xs,
            children: [
              for (final provider in catalog.providers)
                Tooltip(
                  message: '${provider.name} · ${provider.vendor}',
                  child: _SelectChip(
                    label: provider.shortName,
                    selected: selected == provider.id,
                    enabled: enabled,
                    onTap: enabled ? () => onSelected(provider.id) : null,
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

/// Feature-release row. The recommended major is marked in the label and
/// selected by default after the vendor catalogue loads.
class _MajorSelector extends StatelessWidget {
  const _MajorSelector({
    required this.majors,
    required this.loading,
    required this.error,
    required this.selected,
    required this.enabled,
    required this.onSelected,
    required this.onRetry,
    required this.l10n,
  });

  final JavaDistributionMajors? majors;
  final bool loading;
  final String? error;
  final int? selected;
  final bool enabled;
  final ValueChanged<int> onSelected;
  final VoidCallback onRetry;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final values = majors?.majors ?? const <int>[];
    final recommended = majors?.recommendedMajor;

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(AppIcons.version, size: 17, color: tokens.accent),
              const SizedBox(width: AppSpacing.xxs),
              Text(
                l10n.javaInstallMajorLabel,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
              const Spacer(),
              if (loading)
                const SizedBox(
                  width: 14,
                  height: 14,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          if (error != null)
            _InlineError(
              message: l10n.javaInstallMajorsFailed,
              detail: error!,
              onRetry: onRetry,
            )
          else
            Wrap(
              spacing: AppSpacing.xs,
              runSpacing: AppSpacing.xs,
              children: [
                for (final major in values)
                  _SelectChip(
                    label: major == recommended
                        ? 'Java $major · ${l10n.javaInstallRecommended}'
                        : 'Java $major',
                    selected: selected == major,
                    enabled: enabled,
                    onTap: enabled ? () => onSelected(major) : null,
                  ),
              ],
            ),
        ],
      ),
    );
  }
}

/// Build list. Rows are the install selector; the whole list is rendered on
/// the page so long-running LTS majors remain enumerable without a dialog.
class _VersionPanel extends StatelessWidget {
  const _VersionPanel({
    required this.provider,
    required this.versions,
    required this.loading,
    required this.error,
    required this.selectedId,
    required this.enabled,
    required this.onSelected,
    required this.onRetry,
    required this.l10n,
  });

  final JavaDistributionProviderInfo? provider;
  final JavaDistributionVersions? versions;
  final bool loading;
  final String? error;
  final String? selectedId;
  final bool enabled;
  final ValueChanged<String> onSelected;
  final VoidCallback onRetry;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final items = versions?.versions ?? const <JavaDistributionVersion>[];

    return AppSection(
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              Icon(AppIcons.download, size: 17, color: tokens.accent),
              const SizedBox(width: AppSpacing.xxs),
              Text(
                l10n.javaInstallVersionsLabel,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
              if (provider != null && !provider!.listsAllVersions) ...[
                const SizedBox(width: AppSpacing.sm),
                Expanded(
                  child: Text(
                    l10n.javaInstallLatestOnly,
                    textAlign: TextAlign.end,
                    style: textTheme.labelSmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                ),
              ],
            ],
          ),
          if (!loading &&
              error == null &&
              items.any((item) => !item.latestPatch)) ...[
            const SizedBox(height: AppSpacing.xs),
            Text(
              l10n.javaInstallSecurityHint,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
          ],
          const SizedBox(height: AppSpacing.sm),
          if (loading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: AppSpacing.lg),
              child: Center(
                child: SizedBox(
                  width: 20,
                  height: 20,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              ),
            )
          else if (error != null)
            _InlineError(
              message: l10n.javaInstallVersionsFailed,
              detail: error!,
              onRetry: onRetry,
            )
          else if (items.isEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    l10n.javaInstallEmptyVersions,
                    style: textTheme.bodyMedium,
                  ),
                  const SizedBox(height: AppSpacing.xxs),
                  Text(
                    l10n.javaInstallEmptyVersionsHint,
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                ],
              ),
            )
          else
            ListView.separated(
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              itemCount: items.length,
              separatorBuilder: (_, _) =>
                  Divider(height: 1, color: tokens.border),
              itemBuilder: (context, index) {
                final version = items[index];
                return _VersionRow(
                  version: version,
                  selected: version.id == selectedId,
                  enabled: enabled,
                  l10n: l10n,
                  onTap: enabled ? () => onSelected(version.id) : null,
                );
              },
            ),
        ],
      ),
    );
  }
}

class _VersionRow extends StatelessWidget {
  const _VersionRow({
    required this.version,
    required this.selected,
    required this.enabled,
    required this.l10n,
    required this.onTap,
  });

  final JavaDistributionVersion version;
  final bool selected;
  final bool enabled;
  final AppLocalizations l10n;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final size = version.size > 0 ? formatBytes(version.size) : '—';

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        child: AnimatedContainer(
          duration: tokens.fast,
          curve: tokens.curve,
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.xs,
            vertical: AppSpacing.sm,
          ),
          decoration: BoxDecoration(
            color: selected ? tokens.accentSoft : Colors.transparent,
            border: Border(
              left: BorderSide(
                width: 2,
                color: selected ? tokens.accent : Colors.transparent,
              ),
            ),
          ),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Flexible(
                          child: Text(
                            version.version,
                            style: textTheme.titleSmall?.copyWith(
                              color: enabled
                                  ? tokens.textPrimary
                                  : tokens.textTertiary,
                            ),
                          ),
                        ),
                        if (version.recommended) ...[
                          const SizedBox(width: AppSpacing.xs),
                          _Badge(
                            label: l10n.javaInstallRecommended,
                            color: tokens.accent,
                          ),
                        ],
                        if (!version.latestPatch) ...[
                          const SizedBox(width: AppSpacing.xxs),
                          _Badge(
                            label: l10n.javaInstallOutdated,
                            color: tokens.warning,
                          ),
                        ],
                        if (version.lts) ...[
                          const SizedBox(width: AppSpacing.xxs),
                          _Badge(
                            label: l10n.javaInstallLts,
                            color: tokens.textSecondary,
                          ),
                        ],
                      ],
                    ),
                    const SizedBox(height: 2),
                    Text(
                      l10n.javaInstallArchiveMeta(version.archive, size),
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.textTertiary,
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: AppSpacing.sm),
              Container(
                width: 18,
                height: 18,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: selected ? tokens.accent : Colors.transparent,
                  border: Border.all(
                    color: selected ? tokens.accent : tokens.border,
                  ),
                ),
                child: selected
                    ? Icon(AppIcons.check, size: 12, color: tokens.onAccent)
                    : null,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
