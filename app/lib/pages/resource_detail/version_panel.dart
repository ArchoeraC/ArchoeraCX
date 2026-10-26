part of '../resource_detail_page.dart';

/// Compatible versions plus the install target and facet filters.
///
/// The list lives on the page (not in a dialog) so the overview and the
/// version picker share the window instead of competing for a narrow pane.
/// Large projects can return hundreds of versions; only one page is built at a
/// time so opening the page stays responsive.
///
/// Picking a row only marks it; installation happens from the footer button.
/// That keeps a stray tap from queueing a build while the user is still
/// comparing versions, and gives the page one obvious primary action.
class _VersionPanel extends StatefulWidget {
  const _VersionPanel({
    required this.versions,
    required this.loading,
    required this.error,
    required this.preparing,
    required this.selected,
    required this.onSelect,
    required this.gameVersions,
    required this.gameVersion,
    required this.onGameVersionChanged,
    required this.loaders,
    required this.loader,
    required this.onLoaderChanged,
    required this.instances,
    required this.targetInstanceId,
    required this.onTargetChanged,
    required this.target,
    required this.installHint,
    required this.installing,
    required this.onInstall,
    required this.onRetry,
  });

  final List<ResourceVersion>? versions;
  final bool loading;
  final String? error;
  final Set<String> preparing;

  final ResourceVersion? selected;
  final ValueChanged<ResourceVersion> onSelect;

  /// Game versions offered to the filter; empty for modpacks.
  final List<String> gameVersions;
  final String? gameVersion;
  final ValueChanged<String> onGameVersionChanged;

  /// Loader families offered to the filter; only mods use this facet.
  final List<String> loaders;
  final String? loader;
  final ValueChanged<String> onLoaderChanged;

  /// Instances the install can target; empty for modpacks.
  final List<GameInstance> instances;
  final String? targetInstanceId;
  final ValueChanged<String> onTargetChanged;

  /// Human-readable target, or the "select an instance" hint.
  final String target;
  final String? installHint;
  final bool installing;
  final VoidCallback? onInstall;
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
    final shown = items.length > _visible ? items.sublist(0, _visible) : items;

    return AppSection(
      padding: const EdgeInsets.only(bottom: AppSpacing.lg),
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
          if (widget.instances.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.sm),
            _TargetField(
              key: const Key('versionTargetField'),
              l10n: l10n,
              instances: widget.instances,
              value: widget.targetInstanceId,
              onChanged: widget.onTargetChanged,
            ),
          ],
          if (widget.gameVersions.isNotEmpty || widget.loaders.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.sm),
            Wrap(
              spacing: AppSpacing.sm,
              runSpacing: AppSpacing.xs,
              children: [
                if (widget.gameVersions.isNotEmpty)
                  _FilterControl(
                    key: const Key('versionGameFilter'),
                    label: l10n.instanceGameVersionLabel,
                    value: widget.gameVersion,
                    values: widget.gameVersions,
                    labelFor: (value) => value,
                    onChanged: widget.onGameVersionChanged,
                  ),
                if (widget.loaders.isNotEmpty)
                  _FilterControl(
                    key: const Key('versionLoaderFilter'),
                    label: l10n.loaderKindLabel,
                    value: widget.loader,
                    values: widget.loaders,
                    labelFor: (value) => _loaderLabel(l10n, value),
                    onChanged: widget.onLoaderChanged,
                  ),
              ],
            ),
          ],
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
                key: ValueKey('version-${shown[index].id}'),
                version: shown[index],
                selected: shown[index].id == widget.selected?.id,
                preparing: widget.preparing.contains(shown[index].id),
                onSelect: widget.onSelect,
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
            const SizedBox(height: AppSpacing.md),
            _InstallFooter(
              l10n: l10n,
              selected: widget.selected,
              hint: widget.installHint,
              installing: widget.installing,
              onInstall: widget.onInstall,
            ),
          ],
        ],
      ),
    );
  }
}

/// Instance picker: the version facets follow this target, and the install
/// plan is built against it.
class _TargetField extends StatelessWidget {
  const _TargetField({
    super.key,
    required this.l10n,
    required this.instances,
    required this.value,
    required this.onChanged,
  });

  final AppLocalizations l10n;
  final List<GameInstance> instances;
  final String? value;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Row(
      children: [
        Icon(AppIcons.instances, size: 16, color: tokens.textTertiary),
        const SizedBox(width: AppSpacing.xs),
        Text(
          l10n.downloadInstallTarget,
          style: textTheme.labelMedium?.copyWith(color: tokens.textSecondary),
        ),
        const SizedBox(width: AppSpacing.sm),
        Expanded(
          child: AppDropdown<String>(
            value: value,
            isExpanded: true,
            hint: Text(l10n.selectInstance),
            items: [
              for (final instance in instances)
                DropdownMenuItem(
                  value: instance.id,
                  child: Text(
                    '${instance.name} · ${instance.gameVersion} · '
                    '${instance.loaderLabelFor(l10n)}',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
            ],
            onChanged: (id) {
              if (id != null) onChanged(id);
            },
          ),
        ),
      ],
    );
  }
}

class _FilterControl extends StatelessWidget {
  const _FilterControl({
    super.key,
    required this.label,
    required this.value,
    required this.values,
    required this.labelFor,
    required this.onChanged,
  });

  final String label;
  final String? value;
  final List<String> values;
  final String Function(String value) labelFor;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          '$label:',
          style: textTheme.labelMedium?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(width: AppSpacing.xxs),
        AppDropdown<String>(
          value: value,
          items: [
            for (final item in values)
              DropdownMenuItem(value: item, child: Text(labelFor(item))),
          ],
          onChanged: values.length > 1
              ? (item) {
                  if (item != null) onChanged(item);
                }
              : null,
        ),
      ],
    );
  }
}

class _VersionRow extends StatelessWidget {
  const _VersionRow({
    super.key,
    required this.version,
    required this.selected,
    required this.preparing,
    required this.onSelect,
  });

  final ResourceVersion version;
  final bool selected;
  final bool preparing;
  final ValueChanged<ResourceVersion> onSelect;

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
        onTap: preparing ? null : () => onSelect(version),
        child: AnimatedContainer(
          duration: tokens.fast,
          curve: tokens.curve,
          decoration: BoxDecoration(
            color: selected ? tokens.accentSoft : Colors.transparent,
            border: Border(
              left: BorderSide(
                color: selected ? tokens.accent : Colors.transparent,
                width: 2,
              ),
            ),
          ),
          padding: const EdgeInsets.fromLTRB(
            AppSpacing.sm,
            AppSpacing.sm,
            AppSpacing.sm,
            AppSpacing.sm,
          ),
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
                  width: 24,
                  height: 24,
                  child: Padding(
                    padding: EdgeInsets.all(4),
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                )
              else
                SizedBox(
                  width: 24,
                  height: 24,
                  child: selected
                      ? Icon(AppIcons.check, size: 18, color: tokens.accent)
                      : null,
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Selected build, compatibility hint and the one primary install action.
class _InstallFooter extends StatelessWidget {
  const _InstallFooter({
    required this.l10n,
    required this.selected,
    required this.hint,
    required this.installing,
    required this.onInstall,
  });

  final AppLocalizations l10n;
  final ResourceVersion? selected;
  final String? hint;
  final bool installing;
  final VoidCallback? onInstall;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final version = selected;

    final info = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          version == null
              ? l10n.downloadVersionPick
              : l10n.downloadSelectedVersion(version.name),
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: textTheme.bodyMedium?.copyWith(
            color: version == null ? tokens.textSecondary : tokens.textPrimary,
          ),
        ),
        if (hint != null) ...[
          const SizedBox(height: 2),
          Text(
            hint!,
            style: textTheme.labelSmall?.copyWith(color: tokens.warning),
          ),
        ],
      ],
    );

    final button = FilledButton.icon(
      key: const Key('installSelectedVersion'),
      onPressed: installing ? null : onInstall,
      icon: installing
          ? const SizedBox(
              width: 16,
              height: 16,
              child: CircularProgressIndicator(strokeWidth: 2),
            )
          : const Icon(AppIcons.download, size: 18),
      label: Text(
        installing ? l10n.downloadInstalling : l10n.downloadInstallSelected,
      ),
    );

    return Container(
      padding: const EdgeInsets.only(top: AppSpacing.md),
      decoration: BoxDecoration(
        border: Border(top: BorderSide(color: tokens.border)),
      ),
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (constraints.maxWidth < 460) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                info,
                const SizedBox(height: AppSpacing.sm),
                button,
              ],
            );
          }
          return Row(
            children: [
              Expanded(child: info),
              const SizedBox(width: AppSpacing.md),
              button,
            ],
          );
        },
      ),
    );
  }
}

String _loaderLabel(AppLocalizations l10n, String slug) {
  return switch (slug.trim().toLowerCase()) {
    'fabric' => l10n.loaderFabric,
    'quilt' => l10n.loaderQuilt,
    'forge' => l10n.loaderForge,
    'neoforge' => l10n.loaderNeoForge,
    _ => slug,
  };
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
