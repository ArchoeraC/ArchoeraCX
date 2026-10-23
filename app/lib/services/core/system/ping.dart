import 'dart:convert';

import '../core_transport.dart';

/// The result of a successful `system.pingAsync` call.
final class PingResult {
  const PingResult({
    required this.pong,
    required this.method,
    required this.rawJson,
    this.data = const <String, Object?>{},
    this.schemaVersion,
    this.requestId,
  });

  /// Value of `data.pong` (or top-level `pong` when the envelope is flat).
  final Object? pong;

  final String method;

  /// The complete decoded result envelope, kept for diagnostics.
  final Map<String, Object?> rawJson;

  final Map<String, Object?> data;
  final int? schemaVersion;
  final String? requestId;

  /// A short, locale-independent representation of [pong].
  String get summary {
    final value = pong;
    if (value == null) return method;
    if (value is String) return value;
    if (value is num || value is bool) return value.toString();
    return jsonEncode(value);
  }
}

/// `system.ping*` domain binding for the native core.
mixin CoreClientPing on CoreTransport {
  /// Submit `system.pingAsync` and wait for its terminal task-state event.
  ///
  /// The future is completed by [EventPump] when the native core emits the
  /// completed, failed or cancelled state. Progress events are forwarded to
  /// [onProgress]. Nothing here schedules a timer or retries in a loop.
  Future<PingResult> pingAsync({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'system.pingAsync';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    return _parsePing(this, jsonText, fallbackMethod: methodName);
  }
}

PingResult _parsePing(
  CoreTransport transport,
  String jsonText, {
  required String fallbackMethod,
}) {
  final decoded = transport.decodeEnvelope(
    jsonText,
    fallbackMethod: fallbackMethod,
  );

  final dataValue = decoded['data'];
  final data = dataValue is Map<String, Object?>
      ? dataValue
      : const <String, Object?>{};
  final metaValue = decoded['meta'];
  final meta = metaValue is Map<String, Object?>
      ? metaValue
      : const <String, Object?>{};

  final pong = data.containsKey('pong') ? data['pong'] : decoded['pong'];
  final method = (data['method'] ?? decoded['method'] ?? fallbackMethod)
      .toString();
  final schemaVersion = meta['schemaVersion'];

  return PingResult(
    pong: pong,
    method: method,
    rawJson: decoded,
    data: data,
    schemaVersion: schemaVersion is int ? schemaVersion : null,
    requestId: meta['requestId']?.toString(),
  );
}
