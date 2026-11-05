import 'package:archoera_cx/services/core/stats/stats.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('parses a stats snapshot and fills every hour', () {
    final stats = PlayStats.tryParse({
      'generatedAt': 1762344000000,
      'summary': {
        'totalPlayMs': 7200000,
        'todayPlayMs': 3600000,
        'weekPlayMs': 7200000,
        'lastWeekPlayMs': 0,
        'totalSessions': 3,
        'weekSessions': 3,
        'activeDays': 2,
        'streakDays': 2,
        'instanceCount': 1,
        'modCount': 4,
        'firstPlayedAt': 1762257600000,
        'lastPlayedAt': 1762344000000,
        'runningSessions': 0,
      },
      'daily': [
        {'day': '2026-11-05', 'playMs': 3600000, 'sessions': 2},
      ],
      'hourly': [
        {'hour': 21, 'playMs': 3600000, 'sessions': 2},
      ],
      'topInstances': [
        {
          'instanceId': 'a',
          'name': 'Pack',
          'loader': 'fabric',
          'gameVersion': '1.21.4',
          'playMs': 7200000,
          'sessions': 3,
          'lastPlayedAt': 1762344000000,
          'running': false,
        },
      ],
      'recentSessions': [
        {
          'instanceId': 'a',
          'name': 'Pack',
          'loader': 'fabric',
          'gameVersion': '1.21.4',
          'startedAt': 1762344000000,
          'endedAt': null,
          'durationMs': 0,
          'exitCode': null,
        },
      ],
      'loaders': [
        {'loader': 'fabric', 'instances': 1},
      ],
    });

    expect(stats, isNotNull);
    expect(stats!.summary.totalPlayMs, 7200000);
    expect(stats.daily.single.day, DateTime(2026, 11, 5));
    expect(stats.hourly, hasLength(24));
    expect(stats.hourly[21].playMs, 3600000);
    expect(stats.hourly[21].sessions, 2);
    expect(stats.hourly[0].playMs, 0);
    expect(stats.topInstances.single.name, 'Pack');
    expect(stats.recentSessions.single.endedAt, isNull);
    expect(stats.loaders.single.loader, 'fabric');
  });

  test('rejects a payload without a summary object', () {
    expect(PlayStats.tryParse(const <String, Object?>{}), isNull);
  });
}
