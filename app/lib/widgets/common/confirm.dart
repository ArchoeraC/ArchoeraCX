import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';

/// Confirmation dialog for destructive actions.
///
/// A dozen call sites repeated the same three-button dialog shape
/// (warning icon + cancel + danger-filled confirm); this is the one shared
/// implementation. Returns `null` when dismissed, `false` on cancel and
/// `true` on confirm.
Future<bool?> confirmDestructive(
  BuildContext context,
  AppLocalizations l10n, {
  required String title,
  required String body,
  required String confirmLabel,
}) {
  final tokens = context.tokens;
  return showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      icon: Icon(AppIcons.warning, color: tokens.danger),
      title: Text(title),
      content: Text(body),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(false),
          child: Text(l10n.commonCancel),
        ),
        FilledButton(
          style: FilledButton.styleFrom(
            backgroundColor: tokens.danger,
            foregroundColor: tokens.onAccent,
          ),
          onPressed: () => Navigator.of(context).pop(true),
          child: Text(confirmLabel),
        ),
      ],
    ),
  );
}
