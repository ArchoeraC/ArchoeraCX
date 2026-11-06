import '../core_transport.dart';

/// Statistics domain binding for `stats.get`.
///
/// C# owns the play-session log; this mixin only decodes the aggregated
/// snapshot the statistics page renders.
mixin CoreClientStats on CoreTransport {
  Future<PlayStats> getPlayStats({
    void Function(int done, int total)? onProgress,
  }) async {
    const methodName = 'stats.get';
    final jsonText = await runAsync(methodName, onProgress: onProgress);
    final decoded = decodeEnvelope(jsonText, fallbackMethod: methodName);
    final stats = PlayStats.tryParse(asMap(decoded['data'], methodName));
    if (stats == null) {
      throw CoreTransport.invalidEnvelope(methodName, 'invalid statistics');
    }
    return stats;
  }
}

/// One `stats.get` response.
class PlayStats {
  const PlayStats({
    required this.generatedAt,
    required this.summary,
    required this.daily,
    required this.hourly,
    required this.topInstances,
    required this.recentSessions,
    required this.loaders,
  });

  final DateTime generatedAt;
  final PlayStatsSummary summary;
  final List<DailyPlayStat> daily;
  final List<HourlyPlayStat> hourly;
  final List<InstancePlayStat> topInstances;
  final List<RecentPlaySession> recentSessions;
  final List<LoaderInstanceStat> loaders;

  static PlayStats? tryParse(Map<String, Object?> data) {
    final summary = _object(data['summary']);
    if (summary == null) return null;

    return PlayStats(
      generatedAt: _dateFromMs(_int(data['generatedAt'])) ?? DateTime.now(),
      summary: PlayStatsSummary(
        totalPlayMs: _int(summary['totalPlayMs']),
        todayPlayMs: _int(summary['todayPlayMs']),
        weekPlayMs: _int(summary['weekPlayMs']),
        lastWeekPlayMs: _int(summary['lastWeekPlayMs']),
        totalSessions: _int(summary['totalSessions']),
        weekSessions: _int(summary['weekSessions']),
        activeDays: _int(summary['activeDays']),
        streakDays: _int(summary['streakDays']),
        instanceCount: _int(summary['instanceCount']),
        modCount: _int(summary['modCount']),
        firstPlayedAt: _dateFromMs(_nullableInt(summary['firstPlayedAt'])),
        lastPlayedAt: _dateFromMs(_nullableInt(summary['lastPlayedAt'])),
        runningSessions: _int(summary['runningSessions']),
      ),
      daily: [
        for (final value in _list(data['daily']))
          if (_object(value) case final item?)
            if (_day(item['day']) case final day?)
              DailyPlayStat(
                day: day,
                playMs: _int(item['playMs']),
                sessions: _int(item['sessions']),
              ),
      ],
      hourly: _hourly(data['hourly']),
      topInstances: [
        for (final value in _list(data['topInstances']))
          if (_object(value) case final item?)
            InstancePlayStat(
              instanceId: _text(item['instanceId']),
              name: _text(item['name']),
              loader: _text(item['loader']),
              gameVersion: _text(item['gameVersion']),
              playMs: _int(item['playMs']),
              sessions: _int(item['sessions']),
              lastPlayedAt:
                  _dateFromMs(_nullableInt(item['lastPlayedAt'])) ??
                  DateTime.fromMillisecondsSinceEpoch(0),
              running: item['running'] == true,
            ),
      ],
      recentSessions: [
        for (final value in _list(data['recentSessions']))
          if (_object(value) case final item?)
            RecentPlaySession(
              instanceId: _text(item['instanceId']),
              name: _text(item['name']),
              loader: _text(item['loader']),
              gameVersion: _text(item['gameVersion']),
              startedAt:
                  _dateFromMs(_nullableInt(item['startedAt'])) ??
                  DateTime.fromMillisecondsSinceEpoch(0),
              endedAt: _dateFromMs(_nullableInt(item['endedAt'])),
              durationMs: _int(item['durationMs']),
              exitCode: _nullableInt(item['exitCode']),
            ),
      ],
      loaders: [
        for (final value in _list(data['loaders']))
          if (_object(value) case final item?)
            LoaderInstanceStat(
              loader: _text(item['loader']),
              instances: _int(item['instances']),
            ),
      ],
    );
  }
}

class PlayStatsSummary {
  const PlayStatsSummary({
    required this.totalPlayMs,
    required this.todayPlayMs,
    required this.weekPlayMs,
    required this.lastWeekPlayMs,
    required this.totalSessions,
    required this.weekSessions,
    required this.activeDays,
    required this.streakDays,
    required this.instanceCount,
    required this.modCount,
    required this.firstPlayedAt,
    required this.lastPlayedAt,
    required this.runningSessions,
  });

  final int totalPlayMs;
  final int todayPlayMs;
  final int weekPlayMs;
  final int lastWeekPlayMs;
  final int totalSessions;
  final int weekSessions;
  final int activeDays;
  final int streakDays;
  final int instanceCount;
  final int modCount;
  final DateTime? firstPlayedAt;
  final DateTime? lastPlayedAt;
  final int runningSessions;
}

class DailyPlayStat {
  const DailyPlayStat({
    required this.day,
    required this.playMs,
    required this.sessions,
  });

  /// Local calendar day (midnight).
  final DateTime day;
  final int playMs;
  final int sessions;
}

class HourlyPlayStat {
  const HourlyPlayStat({
    required this.hour,
    required this.playMs,
    required this.sessions,
  });

  final int hour;
  final int playMs;
  final int sessions;
}

class InstancePlayStat {
  const InstancePlayStat({
    required this.instanceId,
    required this.name,
    required this.loader,
    required this.gameVersion,
    required this.playMs,
    required this.sessions,
    required this.lastPlayedAt,
    required this.running,
  });

  final String instanceId;
  final String name;
  final String loader;
  final String gameVersion;
  final int playMs;
  final int sessions;
  final DateTime lastPlayedAt;
  final bool running;
}

class RecentPlaySession {
  const RecentPlaySession({
    required this.instanceId,
    required this.name,
    required this.loader,
    required this.gameVersion,
    required this.startedAt,
    required this.endedAt,
    required this.durationMs,
    required this.exitCode,
  });

  final String instanceId;
  final String name;
  final String loader;
  final String gameVersion;
  final DateTime startedAt;
  final DateTime? endedAt;
  final int durationMs;
  final int? exitCode;
}

class LoaderInstanceStat {
  const LoaderInstanceStat({required this.loader, required this.instances});

  final String loader;
  final int instances;
}

List<HourlyPlayStat> _hourly(Object? value) {
  final byHour = <int, HourlyPlayStat>{};
  for (final item in _list(
    value,
  ).map(_object).whereType<Map<String, Object?>>()) {
    final hour = _int(item['hour']);
    if (hour < 0 || hour >= 24) continue;
    byHour[hour] = HourlyPlayStat(
      hour: hour,
      playMs: _int(item['playMs']),
      sessions: _int(item['sessions']),
    );
  }
  return [
    for (var hour = 0; hour < 24; hour++)
      byHour[hour] ?? HourlyPlayStat(hour: hour, playMs: 0, sessions: 0),
  ];
}

Map<String, Object?>? _object(Object? value) {
  if (value is Map<String, Object?>) return value;
  if (value is Map) {
    return value.map((key, item) => MapEntry(key.toString(), item));
  }
  return null;
}

List<Object?> _list(Object? value) =>
    value is List ? value.cast<Object?>() : const <Object?>[];

int _int(Object? value) => _nullableInt(value) ?? 0;

int? _nullableInt(Object? value) {
  if (value is int) return value;
  if (value is num) return value.toInt();
  if (value is String) return int.tryParse(value.trim());
  return null;
}

String _text(Object? value) => value?.toString().trim() ?? '';

DateTime? _dateFromMs(int? value) => value == null
    ? null
    : DateTime.fromMillisecondsSinceEpoch(value, isUtc: true).toLocal();

DateTime? _day(Object? value) {
  if (value is! String) return null;
  final parsed = DateTime.tryParse(value);
  if (parsed == null) return null;
  return DateTime(parsed.year, parsed.month, parsed.day);
}
