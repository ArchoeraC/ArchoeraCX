import 'package:flutter/services.dart';
import 'package:url_launcher/url_launcher.dart';

/// Scheme whitelist shared by all `launchUrl` call sites.
///
/// Only web addresses may leave the app. `file:` and arbitrary custom
/// schemes (e.g. `intent:`) are rejected the same way the C++ core gates
/// `system.openUrl`.
bool canOpenExternalUrl(Uri url) {
  return url.scheme == 'http' || url.scheme == 'https';
}

/// Parses and validates [raw] as a web address; returns null otherwise.
Uri? tryParseWebUrl(String raw) {
  final url = Uri.tryParse(raw);
  if (url == null || !canOpenExternalUrl(url)) return null;
  return url;
}

/// Opens a web address after enforcing the scheme whitelist.
Future<bool> launchWebUrl(
  String raw, {
  LaunchMode mode = LaunchMode.platformDefault,
}) async {
  final url = tryParseWebUrl(raw);
  if (url == null) return false;
  return launchUrl(url, mode: mode);
}

/// Copies verified text (e.g. a validated URL) to the clipboard.
void copyToClipboard(String text) {
  Clipboard.setData(ClipboardData(text: text));
}
