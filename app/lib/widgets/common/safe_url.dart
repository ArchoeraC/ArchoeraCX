import 'dart:io' show InternetAddress, InternetAddressType;

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

/// Image loading is the one UI path allowed to fetch directly. Keep it on the
/// public web: reject non-web schemes, userinfo, and loopback/private address
/// literals so remote metadata cannot point the client at a local service.
bool isSafeImageUrl(String raw) {
  final url = Uri.tryParse(raw);
  if (url == null || !url.hasAuthority) return false;
  if (url.scheme != 'http' && url.scheme != 'https') return false;
  if (url.userInfo.isNotEmpty) return false;
  return !_isPrivateHost(url.host);
}

bool _isPrivateHost(String host) {
  var name = host.toLowerCase();
  while (name.endsWith('.')) {
    name = name.substring(0, name.length - 1);
  }
  if (name == 'localhost' ||
      name.endsWith('.localhost') ||
      name.endsWith('.local')) {
    return true;
  }

  final literal = name.replaceAll('[', '').replaceAll(']', '');
  final address = InternetAddress.tryParse(literal);
  if (address == null) return false;

  final bytes = address.rawAddress;
  if (address.type == InternetAddressType.IPv4) {
    return _isPrivateV4(bytes);
  }
  if (address.type != InternetAddressType.IPv6) return false;

  // IPv4-mapped IPv6 (::ffff:a.b.c.d) is normalised to hex by Uri, so decode
  // the last 32 bits instead of matching the textual form.
  if (bytes.sublist(0, 10).every((byte) => byte == 0) &&
      bytes[10] == 0xff &&
      bytes[11] == 0xff) {
    return _isPrivateV4(bytes.sublist(12));
  }
  if (bytes.every((byte) => byte == 0)) return true; // ::
  if (bytes.sublist(0, 15).every((byte) => byte == 0) && bytes[15] == 1) {
    return true; // ::1
  }
  if ((bytes[0] & 0xfe) == 0xfc) return true; // fc00::/7 (unique local)
  if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) {
    return true; // fe80::/10 (link local)
  }
  return false;
}

bool _isPrivateV4(List<int> bytes) {
  final a = bytes[0];
  final b = bytes[1];
  return a == 0 ||
      a == 10 ||
      a == 127 ||
      (a == 169 && b == 254) ||
      (a == 172 && b >= 16 && b <= 31) ||
      (a == 192 && b == 168);
}
