import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../theme/icons/app_icons.dart';
import 'app_navigator.dart';

/// Pack failures that mean "this archive tried to damage the machine", not
/// "the file is merely invalid". These get a blocking dialog instead of a
/// toast, because the user should decide whether to keep the source.
const _destructiveCodes = <String>{
  'PACK_ARCHIVE_BOMB',
  'PACK_PATH_ESCAPE',
  'PACK_SYMLINK_ENTRY',
  'PACK_TOO_LARGE',
};

bool isDestructivePackCode(String? code) => _destructiveCodes.contains(code);

bool _dialogVisible = false;

/// Shows the warning through the root navigator. Returns false when no
/// navigator is mounted (startup/shutdown/tests), so callers can fall back to
/// a toast.
Future<bool> showPackBlockedDialogFromRouter({
  required String code,
  String? detail,
}) async {
  final context = appNavigatorKey.currentContext;
  if (context == null) return false;
  await showPackBlockedDialog(context, code: code, detail: detail);
  return true;
}

/// Blocking warning for an archive that failed a destructive-content check.
Future<void> showPackBlockedDialog(
  BuildContext context, {
  required String code,
  String? detail,
}) async {
  if (_dialogVisible) return;
  _dialogVisible = true;
  final l10n = AppLocalizations.of(context);
  final scheme = Theme.of(context).colorScheme;
  try {
    await showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => AlertDialog(
        icon: Icon(AppIcons.security, color: scheme.error),
        title: Text(l10n.packBlockedTitle),
        content: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 460),
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(l10n.packBlockedBody),
                const SizedBox(height: 12),
                Text(
                  code,
                  style: Theme.of(context).textTheme.labelMedium
                      ?.copyWith(color: scheme.onSurfaceVariant),
                ),
                if (detail != null && detail.trim().isNotEmpty) ...[
                  const SizedBox(height: 6),
                  SelectableText(
                    detail.trim(),
                    style: Theme.of(context).textTheme.bodySmall
                        ?.copyWith(color: scheme.onSurfaceVariant),
                  ),
                ],
              ],
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: Text(l10n.commonClose),
          ),
        ],
      ),
    );
  } finally {
    _dialogVisible = false;
  }
}
