import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/loader_versions.dart';
import '../../theme/tokens.dart';
import 'loader_picker_panel.dart';

/// Full-screen style picker for an existing loader install.
///
/// The two-column panel is used while creating an instance (it also offers
/// vanilla); this dialog only lists builds for one known loader, but reuses the
/// same searchable list so filtering and selection behave identically.
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

class _LoaderVersionPickerDialog extends StatelessWidget {
  const _LoaderVersionPickerDialog({
    required this.versions,
    required this.selected,
    required this.gameVersion,
  });

  final List<LoaderVersionInfo> versions;
  final String selected;
  final String gameVersion;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final listHeight = (MediaQuery.sizeOf(context).height * 0.6).clamp(
      260.0,
      460.0,
    );

    return Dialog(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 520),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
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
                          'Minecraft $gameVersion',
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
              const SizedBox(height: AppSpacing.xs),
              Divider(height: 1, color: tokens.border),
              const SizedBox(height: AppSpacing.sm),
              SizedBox(
                height: listHeight,
                child: LoaderVersionList(
                  versions: versions,
                  selectedVersion: selected,
                  onSelected: (version) => Navigator.of(context).pop(version),
                  autofocusSearch: true,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
