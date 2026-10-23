import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/services/core/system_status.dart';
import 'package:archoera_cx/services/core/wiki_feed.dart';
import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/pages/wiki_page.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/stores/wiki.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

class _FixedFeed extends WikiFeedNotifier {
  _FixedFeed(this.feed);

  final WikiFeed? feed;

  @override
  Future<WikiFeed?> build() async => feed;
}

class _FixedStatus extends SystemStatusNotifier {
  _FixedStatus(this.status);

  final SystemStatus? status;

  @override
  Future<SystemStatus?> build() async => status;
}

WikiFeed _feed() => WikiFeed(
  items: [
    WikiFeedItem(
      id: 'news:n1',
      source: 'mojang-news',
      kind: 'news',
      title: 'Enter the Marketplace, if you dare',
      summary: 'Explore seven frightfully fun Spooky Spotlight packs.',
      url: 'https://minecraft.net/article/a',
      publishedAt: DateTime.now().subtract(const Duration(minutes: 5)),
    ),
    WikiFeedItem(
      id: 'java:26-4-snapshot-5',
      source: 'mojang-java',
      kind: 'snapshot',
      title: 'Minecraft 26.4 Snapshot 5',
      summary: 'Packed Ice Bricks',
      url: 'https://minecraft.wiki/w/Java_Edition_26.4_Snapshot_5',
      publishedAt: DateTime.now().subtract(const Duration(hours: 3)),
    ),
    WikiFeedItem(
      id: 'wiki:2',
      source: 'minecraft-wiki',
      kind: 'edit',
      title: "Domino's",
      summary: '/* Videos */',
      url: 'https://minecraft.wiki/w/Domino',
      publishedAt: DateTime.now().subtract(const Duration(minutes: 1)),
      author: 'Yaya',
      tags: const ['edit', '+20 B'],
    ),
  ],
  sources: const [
    WikiSourceStatus(id: 'mojang-news', ok: true, count: 1),
    WikiSourceStatus(id: 'mojang-java', ok: true, count: 1),
    WikiSourceStatus(
      id: 'minecraft-wiki',
      ok: false,
      count: 0,
      error: 'HTTP 500',
    ),
  ],
  fetchedAt: DateTime.now(),
);

const _status = SystemStatus(
  cores: 16,
  platform: 'linux',
  os: 'Linux 6.8',
  cpuPercent: 42.4,
  loadAverage1m: 0.84,
  memoryTotalMb: 15974,
  memoryAvailableMb: 5964,
  memoryPercent: 62.4,
  diskPath: '/home/betastudio2',
  diskTotalBytes: 512110190592,
  diskFreeBytes: 148434069504,
  diskPercent: 71.0,
  launcherWorkingSetMb: 182,
  launcherManagedMb: 24,
  launcherUptimeSeconds: 15132,
  systemUptimeSeconds: 259332,
);

Future<void> _pumpWiki(
  WidgetTester tester, {
  WikiFeed? feed,
  SystemStatus? status,
  Size size = const Size(1440, 900),
}) async {
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        coreClientProvider.overrideWithValue(null),
        wikiFeedProvider.overrideWith(() => _FixedFeed(feed)),
        systemStatusProvider.overrideWith(() => _FixedStatus(status)),
        // Overriding the locale through the container keeps the real controller.
      ],
      child: const ArchoeraApp(),
    ),
  );
  await tester.pumpAndSettle();

  await tester.tap(find.byKey(const ValueKey('nav-/wiki')));
  await tester.pumpAndSettle();
}

void main() {
  group('feed models', () {
    test('parses items and per-source status', () {
      final feed = WikiFeed.tryParse({
        'fetchedAt': '2026-10-22T05:10:00.000Z',
        'cached': true,
        'items': [
          {
            'id': 'wiki:1',
            'source': 'minecraft-wiki',
            'kind': 'new',
            'title': 'Copper Golem',
            'summary': 'New page',
            'url': 'https://minecraft.wiki/w/Copper_Golem',
            'publishedAt': '2026-10-22T05:03:56Z',
            'author': 'Templero316',
            'tags': ['new', '+500 B'],
          },
          {'id': 'broken'},
        ],
        'sources': [
          {'id': 'minecraft-wiki', 'ok': true, 'count': 1, 'error': null},
          {'id': 'mojang-news', 'ok': false, 'count': 0, 'error': 'HTTP 500'},
        ],
      });

      expect(feed, isNotNull);
      expect(feed!.items, hasLength(1));
      expect(feed.cached, isTrue);
      expect(feed.itemsFor('minecraft-wiki'), hasLength(1));
      expect(feed.itemsFor('mojang-news'), isEmpty);
      expect(feed.statusFor('mojang-news')!.ok, isFalse);
      expect(feed.statusFor('mojang-news')!.error, 'HTTP 500');
      expect(feed.items.first.tags, ['new', '+500 B']);
      expect(feed.items.first.publishedAt!.toUtc().hour, 5);
    });

    test('rejects non-map payloads', () {
      expect(WikiFeed.tryParse(null), isNull);
      expect(WikiFeed.tryParse('nope'), isNull);
    });
  });

  group('system status model', () {
    test('reads every metric and tolerates nulls', () {
      final status = SystemStatus.tryParse({
        'platform': 'linux',
        'cores': 8,
        'cpu': {'usagePercent': 12.5, 'loadAverage1m': null},
        'memory': {'totalMb': 16384, 'availableMb': 8192, 'usagePercent': 50},
        'disk': {'path': '/', 'totalBytes': null, 'freeBytes': null},
        'process': {'workingSetMb': 120, 'managedMb': 18, 'uptimeSeconds': 60},
        'system': {'uptimeSeconds': null},
      });

      expect(status, isNotNull);
      expect(status!.cores, 8);
      expect(status.cpuPercent, 12.5);
      expect(status.loadAverage1m, isNull);
      expect(status.hasMemory, isTrue);
      expect(status.memoryPercent, 50);
      expect(status.hasDisk, isFalse);
      expect(status.diskPercent, isNull);
      expect(status.launcherWorkingSetMb, 120);
      expect(status.systemUptimeSeconds, isNull);
    });
  });

  testWidgets('renders system load and the merged feed', (tester) async {
    await _pumpWiki(tester, feed: _feed(), status: _status);

    // Machine load block.
    expect(find.text('System load'), findsOneWidget);
    expect(find.text('CPU'), findsOneWidget);
    expect(find.text('42%'), findsOneWidget);
    expect(find.text('16 threads · load 0.84'), findsOneWidget);
    expect(find.text('62%'), findsOneWidget);
    expect(find.text('9.8 / 15.6 GB'), findsOneWidget);
    expect(find.text('71%'), findsOneWidget);
    expect(find.text('138.2 GB free'), findsOneWidget);
    expect(find.text('182 MB'), findsOneWidget);
    expect(find.text('up 4h 12m'), findsOneWidget);
    expect(find.text('System up 3d 00h · Linux 6.8'), findsOneWidget);

    // Community feed, including per-source health.
    expect(find.text('Community'), findsOneWidget);
    expect(find.text('Official news · 1'), findsOneWidget);
    expect(find.text('Java versions · 1'), findsOneWidget);
    expect(find.text('Wiki edits · Unavailable'), findsOneWidget);
    expect(find.text('Enter the Marketplace, if you dare'), findsOneWidget);
    expect(find.text('Minecraft 26.4 Snapshot 5'), findsOneWidget);
    expect(find.text("Domino's"), findsOneWidget);
    expect(find.textContaining('@Yaya'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('keeps the layout intact on a narrow window', (tester) async {
    await _pumpWiki(
      tester,
      feed: _feed(),
      status: _status,
      size: const Size(720, 640),
    );

    expect(find.text('System load'), findsOneWidget);
    expect(find.text('Minecraft 26.4 Snapshot 5'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('localizes the page and survives a 520px window', (tester) async {
    tester.view.physicalSize = const Size(520, 720);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          coreClientProvider.overrideWithValue(null),
          wikiFeedProvider.overrideWith(() => _FixedFeed(_feed())),
          systemStatusProvider.overrideWith(() => _FixedStatus(_status)),
        ],
        child: MaterialApp(
          theme: AppTheme.light,
          locale: const Locale('zh'),
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: const Scaffold(
            body: SingleChildScrollView(child: WikiPage()),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('系统负载'), findsOneWidget);
    expect(find.text('社区动态'), findsOneWidget);
    expect(find.text('官方新闻 · 1'), findsOneWidget);
    expect(find.text('Wiki 编辑 · 不可用'), findsOneWidget);
    expect(find.text('Minecraft 26.4 Snapshot 5'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('shows an empty state when every source fails', (tester) async {
    await _pumpWiki(
      tester,
      feed: const WikiFeed(
        items: [],
        sources: [
          WikiSourceStatus(
            id: 'mojang-news',
            ok: false,
            count: 0,
            error: 'timed out',
          ),
        ],
      ),
      status: _status,
    );

    expect(find.text('Nothing to show right now.'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
