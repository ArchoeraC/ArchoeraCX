import 'package:flutter/material.dart';

import '../l10n/generated/app_localizations.dart';

/// "Last played" relative caption shared by the dashboard and the
/// instances page (both pages carried a verbatim copy of this helper).
String lastPlayedLabel(
  BuildContext context,
  AppLocalizations l10n,
  DateTime? lastPlayed,
) {
  if (lastPlayed == null) return l10n.instanceLastPlayedNever;

  final now = DateTime.now();
  final today = DateTime(now.year, now.month, now.day);
  final date = DateTime(lastPlayed.year, lastPlayed.month, lastPlayed.day);
  if (!date.isBefore(today)) return l10n.lastPlayedToday;
  if (!date.isBefore(today.subtract(const Duration(days: 1)))) {
    return l10n.lastPlayedYesterday;
  }

  return MaterialLocalizations.of(context).formatShortDate(lastPlayed);
}
