/// Small formatting helpers shared across pages and dialogs.
///
/// Single source for byte formatting: every previous page kept its own
/// `_formatBytes` copy, which had drifted apart in tiers and decimals. The
/// canonical shape (pinned by the wiki and task-center tests): gigabytes
/// and up keep one decimal, MB stays whole once it reaches triple digits,
/// bytes and KB always stay whole.
// ignore_for_file: prefer_int_literals
library;

String formatBytes(int bytes) {
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  var value = bytes.toDouble();
  var unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  final digits = unit <= 1 || (unit < 3 && value >= 100) ? 0 : 1;
  return '${value.toStringAsFixed(digits)} ${units[unit]}';
}
