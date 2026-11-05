import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/pages/stats_page.dart';
import 'package:archoera_cx/services/core/stats/stats.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/stores/stats.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  final now = DateTime(2026, 11, 5, 20);

  PlayStats stats({int sessions = 4}) {
    return PlayStats(
      generatedAt: now,
      summary: PlayStatsSummary(
        totalPlayMs: sessions * 3600000,
        todayPlayMs: 1800000,
        weekPlayMs: 7200000,
        lastWeekPlayMs: 3600000,
        totalSessions: sessions,
        weekSessions: 2,
        activeDays: 3,
        streakDays: 2,
        instanceCount: 2,
        modCount: 12,
        firstPlayedAt: now.subtract(const Duration(days: 20)),
        lastPlayedAt: now,
        runningSessions: 0,
      ),
      daily: [
        for (var index = 0; index < 90; index++)
          DailyPlayStat(
            day: DateTime(
              now.year,
              now.month,
              now.day,
            ).subtract(Duration(days: 89 - index)),
            playMs: index % 4 == 0 ? 0 : index * 60000,
            sessions: index % 4,
          ),
      ],
      hourly: [
        for (var hour = 0; hour < 24; hour++)
          HourlyPlayStat(
            hour: hour,
            playMs: hour >= 18 ? hour * 60000 : 0,
            sessions: hour >= 18 ? 1 : 0,
          ),
      ],
      topInstances: [
        InstancePlayStat(
          instanceId: 'a',
          name: 'My Pack',
          loader: 'fabric',
          gameVersion: '1.21.4',
          playMs: 7200000,
          sessions: 3,
          lastPlayedAt: now,
          running: false,
        ),
        InstancePlayStat(
          instanceId: 'b',
          name: 'Vanilla',
          loader: 'vanilla',
          gameVersion: '1.20.1',
          playMs: 3600000,
          sessions: 1,
          lastPlayedAt: now.subtract(const Duration(days: 2)),
          running: false,
        ),
      ],
      recentSessions: [
        RecentPlaySession(
          instanceId: 'a',
          name: 'My Pack',
          loader: 'fabric',
          gameVersion: '1.21.4',
          startedAt: now,
          endedAt: now.add(const Duration(hours: 1)),
          durationMs: 3600000,
          exitCode: 0,
        ),
      ],
      loaders: const [
        LoaderInstanceStat(loader: 'fabric', instances: 1),
        LoaderInstanceStat(loader: 'vanilla', instances: 1),
      ],
    );
  }

  Future<void> pumpStats(
    WidgetTester tester, {
    required PlayStats value,
  }) async {
    SharedPreferences.setMockInitialValues(<String, Object>{});
    tester.view.physicalSize = const Size(1440, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          coreClientProvider.overrideWithValue(null),
          playStatsProvider.overrideWith((ref) async => value),
        ],
        child: const ArchoeraApp(),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const ValueKey('nav-/stats')));
    await tester.pumpAndSettle();
  }

  testWidgets('renders the overview, activity and ranking cards', (
    tester,
  ) async {
    await pumpStats(tester, value: stats());

    expect(find.byType(StatsPage), findsOneWidget);
    expect(find.text('Statistics'), findsWidgets);
    expect(find.text('Most played'), findsOneWidget);
    expect(find.text('Play activity'), findsOneWidget);
    expect(find.text('Instance mix'), findsOneWidget);
    expect(find.text('My Pack'), findsWidgets);
    expect(tester.takeException(), isNull);

    // Month labels overflow above the grid instead of widening their week
    // column: every column keeps the same 13 px cell width and the same 3 px
    // gap, so the heatmap stays one rectangle.
    double? previousRight;
    for (var index = 0; ; index++) {
      final column = find.byKey(ValueKey('stats-week-$index'));
      if (column.evaluate().isEmpty) break;
      final rect = tester.getRect(column);
      expect(rect.width, 13);
      if (previousRight != null) {
        expect(rect.left - previousRight, 3);
      }
      previousRight = rect.right;
    }
  });

  testWidgets('an empty history still renders the page with a hint', (
    tester,
  ) async {
    await pumpStats(
      tester,
      value: PlayStats(
        generatedAt: now,
        summary: PlayStatsSummary(
          totalPlayMs: 0,
          todayPlayMs: 0,
          weekPlayMs: 0,
          lastWeekPlayMs: 0,
          totalSessions: 0,
          weekSessions: 0,
          activeDays: 0,
          streakDays: 0,
          instanceCount: 0,
          modCount: 0,
          firstPlayedAt: null,
          lastPlayedAt: null,
          runningSessions: 0,
        ),
        daily: [
          for (var index = 0; index < 90; index++)
            DailyPlayStat(
              day: DateTime(
                now.year,
                now.month,
                now.day,
              ).subtract(Duration(days: 89 - index)),
              playMs: 0,
              sessions: 0,
            ),
        ],
        hourly: [
          for (var hour = 0; hour < 24; hour++)
            HourlyPlayStat(hour: hour, playMs: 0, sessions: 0),
        ],
        topInstances: const [],
        recentSessions: const [],
        loaders: const [],
      ),
    );

    expect(
      find.textContaining('No play sessions yet', findRichText: true),
      findsOneWidget,
    );
    expect(find.text('No instances yet'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
