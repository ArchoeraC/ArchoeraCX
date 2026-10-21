import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/loader_versions.dart';
import '../../theme/tokens.dart';

Future<String?> showLoaderVersionPicker(
  BuildContext context, {
  required List<LoaderVersionInfo> versions,
  required String selected,
  required String gameVersion,
}) {
  return showDialog<String>(
    context: context,
    builder: (_) => _LoaderVersionPickerDialog(
      versions: versions,
      selected: selected,
      gameVersion: gameVersion,
    ),
  );
}

class _LoaderVersionPickerDialog extends StatefulWidget {
  const _LoaderVersionPickerDialog({
    required this.versions,
    required this.selected,
    required this.gameVersion,
  });

  final List<LoaderVersionInfo> versions;
  final String selected;
  final String gameVersion;

  @override
  State<_LoaderVersionPickerDialog> createState() =>
      _LoaderVersionPickerDialogState();
}

class _LoaderVersionPickerDialogState
    extends State<_LoaderVersionPickerDialog> {
  final _query = TextEditingController();

  @override
  void initState() {
    super.initState();
    _query.addListener(_onQueryChanged);
  }

  @override
  void dispose() {
    _query
      ..removeListener(_onQueryChanged)
      ..dispose();
    super.dispose();
  }

  void _onQueryChanged() => setState(() {});

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final query = _query.text.trim().toLowerCase();
    final filtered = widget.versions
        .where(
          (item) => query.isEmpty || item.version.toLowerCase().contains(query),
        )
        .toList(growable: false);

    return Dialog(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 520, maxHeight: 620),
        child: Column(
          mainAxisSize: MainAxisSize.min,
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
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          l10n.loaderVersionSelect,
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        const SizedBox(height: 2),
                        Text(
                          'Minecraft ${widget.gameVersion}',
                          style: Theme.of(context).textTheme.bodySmall
                              ?.copyWith(color: tokens.textTertiary),
                        ),
                      ],
                    ),
                  ),
                  IconButton(
                    onPressed: () => Navigator.of(context).pop(),
                    tooltip: l10n.commonClose,
                    icon: const Icon(Icons.close_rounded),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.lg,
                AppSpacing.sm,
                AppSpacing.lg,
                AppSpacing.sm,
              ),
              child: TextField(
                controller: _query,
                decoration: InputDecoration(
                  hintText: l10n.loaderVersionSearch,
                  prefixIcon: const Icon(Icons.search_rounded, size: 19),
                  isDense: true,
                ),
              ),
            ),
            Divider(height: 1, color: tokens.border),
            Flexible(
              child: filtered.isEmpty
                  ? Padding(
                      padding: const EdgeInsets.all(AppSpacing.xl),
                      child: Text(
                        l10n.loaderVersionEmpty,
                        textAlign: TextAlign.center,
                        style: Theme.of(context).textTheme.bodyMedium
                            ?.copyWith(color: tokens.textSecondary),
                      ),
                    )
                  : ListView.separated(
                      shrinkWrap: true,
                      padding: const EdgeInsets.symmetric(
                        vertical: AppSpacing.xs,
                      ),
                      itemCount: filtered.length,
                      separatorBuilder: (_, _) => Divider(
                        height: 1,
                        indent: AppSpacing.lg,
                        endIndent: AppSpacing.lg,
                        color: tokens.border,
                      ),
                      itemBuilder: (context, index) {
                        final item = filtered[index];
                        final selected = item.version == widget.selected;
                        return ListTile(
                          contentPadding: const EdgeInsets.symmetric(
                            horizontal: AppSpacing.lg,
                          ),
                          selected: selected,
                          title: Row(
                            children: [
                              Flexible(
                                child: Text(
                                  item.version,
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                              if (item.latest) ...[
                                const SizedBox(width: AppSpacing.xs),
                                _VersionBadge(
                                  label: l10n.loaderVersionLatest,
                                  accent: true,
                                ),
                              ],
                              if (item.recommended) ...[
                                const SizedBox(width: AppSpacing.xxs),
                                _VersionBadge(
                                  label: l10n.loaderVersionRecommended,
                                ),
                              ],
                              if (!item.stable) ...[
                                const SizedBox(width: AppSpacing.xxs),
                                _VersionBadge(label: l10n.loaderVersionBeta),
                              ],
                            ],
                          ),
                          trailing: selected
                              ? Icon(
                                  Icons.check_rounded,
                                  color: tokens.accent,
                                  size: 20,
                                )
                              : null,
                          onTap: () => Navigator.of(context).pop(item.version),
                        );
                      },
                    ),
            ),
          ],
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
