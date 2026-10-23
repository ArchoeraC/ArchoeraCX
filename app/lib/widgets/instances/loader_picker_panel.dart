import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../services/core/loaders/loader_versions.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/loader_mark.dart';

/// Two-column loader chooser.
///
/// The left column lists the loader types (vanilla included) and the right
/// column lists the builds for the selected loader. Choosing vanilla retracts
/// the build column with a width animation, so no empty picker is shown when
/// there is nothing to install.
class LoaderPickerPanel extends StatefulWidget {
  const LoaderPickerPanel({
    super.key,
    required this.loaders,
    required this.selectedLoader,
    required this.onLoaderSelected,
    required this.versions,
    required this.selectedVersion,
    required this.onVersionSelected,
    this.loading = false,
    this.error,
    this.onRetry,
    this.gameVersion,
    this.width = 600,
    this.height = 300,
    this.expanded,
  });

  final List<GameLoader> loaders;
  final GameLoader selectedLoader;
  final ValueChanged<GameLoader> onLoaderSelected;

  final List<LoaderVersionInfo> versions;
  final String selectedVersion;
  final ValueChanged<String> onVersionSelected;

  final bool loading;
  final String? error;
  final VoidCallback? onRetry;
  final String? gameVersion;

  /// Total width of the panel. The caller animates this together with the
  /// dialog width so the whole window expands, not just one column.
  final double width;
  final double height;

  /// Whether the build column is shown. Defaults to "any loader but vanilla".
  final bool? expanded;

  @override
  State<LoaderPickerPanel> createState() => _LoaderPickerPanelState();
}

class _LoaderPickerPanelState extends State<LoaderPickerPanel> {
  // Left column + gap + right column fill the dialog content width.
  static const double _leftWidth = 168;
  static const double _gap = 16;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final expanded =
        widget.expanded ?? widget.selectedLoader != GameLoader.vanilla;
    final leftWidth = expanded ? _leftWidth : widget.width;
    final rightWidth = expanded
        ? (widget.width - _leftWidth - _gap).clamp(220.0, 600.0)
        : 0.0;

    return SizedBox(
      height: widget.height,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            width: leftWidth,
            child: _LoaderColumn(
              l10n: l10n,
              tokens: tokens,
              loaders: widget.loaders,
              selectedLoader: widget.selectedLoader,
              selectedVersion: widget.selectedVersion,
              showVanillaHint: !expanded,
              onSelected: widget.onLoaderSelected,
            ),
          ),
          if (expanded) ...[
            const SizedBox(width: _gap),
            SizedBox(
              width: rightWidth,
              child: LoaderVersionList(
                versions: widget.versions,
                selectedVersion: widget.selectedVersion,
                onSelected: widget.onVersionSelected,
                loading: widget.loading,
                error: widget.error,
                onRetry: widget.onRetry,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _LoaderColumn extends StatelessWidget {
  const _LoaderColumn({
    required this.l10n,
    required this.tokens,
    required this.loaders,
    required this.selectedLoader,
    required this.selectedVersion,
    required this.showVanillaHint,
    required this.onSelected,
  });

  final AppLocalizations l10n;
  final AppTokens tokens;
  final List<GameLoader> loaders;
  final GameLoader selectedLoader;
  final String selectedVersion;
  final bool showVanillaHint;
  final ValueChanged<GameLoader> onSelected;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.only(left: 2, bottom: AppSpacing.xs),
          child: Text(
            l10n.instanceLoaderLabel,
            style: textTheme.labelSmall?.copyWith(
              color: tokens.textSecondary,
              letterSpacing: 1.2,
            ),
          ),
        ),
        Expanded(
          child: ListView(
            padding: EdgeInsets.zero,
            children: [
              for (final loader in loaders)
                _LoaderTile(
                  loader: loader,
                  selected: loader == selectedLoader,
                  label: loader.labelFor(l10n),
                  version:
                      loader == selectedLoader && loader != GameLoader.vanilla
                      ? selectedVersion
                      : null,
                  onTap: () => onSelected(loader),
                ),
            ],
          ),
        ),
        if (showVanillaHint)
          Padding(
            padding: const EdgeInsets.only(left: 2, top: AppSpacing.xs),
            child: Text(
              l10n.loaderPickerVanillaHint,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
          ),
      ],
    );
  }
}

class _LoaderTile extends StatelessWidget {
  const _LoaderTile({
    required this.loader,
    required this.selected,
    required this.label,
    required this.version,
    required this.onTap,
  });

  final GameLoader loader;
  final bool selected;
  final String label;
  final String? version;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final versionText = version;

    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(tokens.radiusM),
          child: AnimatedContainer(
            duration: tokens.fast,
            curve: tokens.curve,
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.sm,
              vertical: 7,
            ),
            decoration: BoxDecoration(
              color: selected ? tokens.accentSoft : tokens.surface1,
              borderRadius: BorderRadius.circular(tokens.radiusM),
              border: Border.all(
                color: selected
                    ? tokens.accent.withValues(alpha: 0.55)
                    : tokens.border,
              ),
            ),
            child: Row(
              children: [
                LoaderMark(loader: loader, size: 20),
                const SizedBox(width: AppSpacing.xs),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        label,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.labelLarge?.copyWith(
                          color: selected ? tokens.accent : tokens.textPrimary,
                          fontWeight: selected
                              ? FontWeight.w600
                              : FontWeight.w500,
                        ),
                      ),
                      if (versionText != null && versionText.isNotEmpty)
                        Text(
                          versionText,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: textTheme.labelSmall?.copyWith(
                            color: tokens.textTertiary,
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Searchable, virtualized loader build list.
///
/// Shared by the two-column panel and the full-screen style picker dialog so
/// both use the same filtering, row height and selected-version scroll.
class LoaderVersionList extends StatefulWidget {
  const LoaderVersionList({
    super.key,
    required this.versions,
    required this.selectedVersion,
    required this.onSelected,
    this.loading = false,
    this.error,
    this.onRetry,
    this.autofocusSearch = false,
  });

  final List<LoaderVersionInfo> versions;
  final String selectedVersion;
  final ValueChanged<String> onSelected;
  final bool loading;
  final String? error;
  final VoidCallback? onRetry;
  final bool autofocusSearch;

  @override
  State<LoaderVersionList> createState() => _LoaderVersionListState();
}

class _LoaderVersionListState extends State<LoaderVersionList> {
  static const double _rowHeight = 40;

  final _search = TextEditingController();
  final _scroll = ScrollController();
  String _query = '';
  bool _jumpToSelected = true;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _scrollToSelected());
  }

  @override
  void didUpdateWidget(covariant LoaderVersionList oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.versions != widget.versions) {
      if (_query.isNotEmpty) {
        _query = '';
        _search.clear();
      }
      _jumpToSelected = true;
      WidgetsBinding.instance.addPostFrameCallback((_) => _scrollToSelected());
    }
  }

  @override
  void dispose() {
    _search.dispose();
    _scroll.dispose();
    super.dispose();
  }

  List<LoaderVersionInfo> get _filtered {
    final query = _query.trim().toLowerCase();
    if (query.isEmpty) return widget.versions;
    return widget.versions
        .where((item) => item.version.toLowerCase().contains(query))
        .toList(growable: false);
  }

  void _scrollToSelected() {
    if (!mounted || !_jumpToSelected) return;
    _jumpToSelected = false;
    if (!_scroll.hasClients) return;
    final items = _filtered;
    final index = items.indexWhere(
      (item) => item.version == widget.selectedVersion,
    );
    if (index < 0) return;
    final offset = (index * _rowHeight - 60).clamp(
      0.0,
      _scroll.position.maxScrollExtent,
    );
    if (offset > 0) _scroll.jumpTo(offset);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final items = _filtered;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextField(
          controller: _search,
          autofocus: widget.autofocusSearch,
          textInputAction: TextInputAction.search,
          style: textTheme.bodySmall,
          onChanged: (value) => setState(() => _query = value),
          decoration: InputDecoration(
            isDense: true,
            hintText: l10n.loaderVersionSearch,
            prefixIcon: Icon(
              AppIcons.search,
              size: 18,
              color: tokens.textTertiary,
            ),
            suffixIcon: _query.isEmpty
                ? null
                : IconButton(
                    tooltip: l10n.commonClose,
                    onPressed: () {
                      _search.clear();
                      setState(() => _query = '');
                    },
                    icon: const Icon(AppIcons.close, size: 16),
                  ),
          ),
        ),
        const SizedBox(height: AppSpacing.xs),
        Row(
          children: [
            Text(
              l10n.loaderVersionCount(items.length),
              style: textTheme.labelSmall?.copyWith(color: tokens.textTertiary),
            ),
            const Spacer(),
            if (widget.error != null && widget.onRetry != null)
              TextButton(
                onPressed: widget.onRetry,
                child: Text(l10n.refresh),
              ),
          ],
        ),
        const SizedBox(height: AppSpacing.xxs),
        Expanded(
          child: widget.loading
              ? const Center(child: CircularProgressIndicator())
              : widget.error != null
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.symmetric(
                      horizontal: AppSpacing.sm,
                    ),
                    child: Text(
                      widget.error!,
                      textAlign: TextAlign.center,
                      maxLines: 3,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.warning,
                      ),
                    ),
                  ),
                )
              : items.isEmpty
              ? Center(
                  child: Text(
                    _query.trim().isEmpty
                        ? l10n.loaderVersionEmpty
                        : l10n.loaderVersionNoMatch,
                    textAlign: TextAlign.center,
                    style: textTheme.bodySmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                )
              : ListView.builder(
                  controller: _scroll,
                  padding: EdgeInsets.zero,
                  itemExtent: _rowHeight,
                  itemCount: items.length,
                  itemBuilder: (context, index) {
                    final item = items[index];
                    return _VersionRow(
                      item: item,
                      selected: item.version == widget.selectedVersion,
                      onTap: () => widget.onSelected(item.version),
                    );
                  },
                ),
        ),
      ],
    );
  }
}

class _VersionRow extends StatelessWidget {
  const _VersionRow({
    required this.item,
    required this.selected,
    required this.onTap,
  });

  final LoaderVersionInfo item;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Material(
      color: selected ? tokens.accentSoft : Colors.transparent,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs),
          child: Row(
            children: [
              Container(
                width: 14,
                height: 14,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  border: Border.all(
                    color: selected ? tokens.accent : tokens.borderStrong,
                    width: selected ? 4 : 1,
                  ),
                ),
              ),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(
                  item.version,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.labelLarge?.copyWith(
                    color: selected ? tokens.accent : tokens.textPrimary,
                    fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
                  ),
                ),
              ),
              if (item.latest) ...[
                _VersionBadge(label: l10n.loaderVersionLatest, accent: true),
                const SizedBox(width: 4),
              ],
              if (item.recommended) ...[
                _VersionBadge(label: l10n.loaderVersionRecommended),
                const SizedBox(width: 4),
              ],
              if (!item.stable)
                _VersionBadge(label: l10n.loaderVersionBeta),
            ],
          ),
        ),
      ),
    );
  }
}

class _VersionBadge extends StatelessWidget {
  const _VersionBadge({required this.label, this.accent = false});

  final String label;
  final bool accent;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 1),
      decoration: BoxDecoration(
        color: accent ? tokens.accentSoft : tokens.surface2,
        borderRadius: BorderRadius.circular(tokens.radiusS),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall
            ?.copyWith(color: accent ? tokens.accent : tokens.textTertiary),
      ),
    );
  }
}
