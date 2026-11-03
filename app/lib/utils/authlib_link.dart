/// Extracts a Yggdrasil API URL from the link format used by skin sites.
///
/// Third-party platforms expose buttons like
/// `authlib-injector:yggdrasil-server:https%3A%2F%2Flittleskin.cn%2Fapi%2Fyggdrasil`;
/// accepting that link lets users add a server without retyping the URL.
String? parseAuthlibServerLink(String raw) {
  final text = raw.trim();
  if (text.isEmpty) return null;

  const prefix = 'authlib-injector:yggdrasil-server:';
  if (text.startsWith(prefix)) {
    try {
      final decoded = Uri.decodeComponent(text.substring(prefix.length)).trim();
      return _validServer(decoded);
    } catch (_) {
      return null;
    }
  }

  return _validServer(text);
}

String? _validServer(String value) {
  final uri = Uri.tryParse(value);
  if (uri == null ||
      !uri.hasAuthority ||
      (uri.scheme != 'https' &&
          !(uri.scheme == 'http' &&
              (uri.host == 'localhost' || uri.host == '127.0.0.1')))) {
    return null;
  }

  return value;
}
