import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

/// End-to-end check for `wiki.feed` and `system.status`.
///
/// Requires the native core + C# module (`./tool/build-core.sh`,
/// `./tool/build-cs.sh`) and network access, so it stays opt-in:
/// `ARCHOERACX_WIKI_TESTS=1 flutter test test/wiki_integration_test.dart`.
void main() {
  final libraryName = Platform.isWindows
      ? 'archoera_core.dll'
      : Platform.isMacOS
      ? 'libarchoera_core.dylib'
      : 'libarchoera_core.so';
  final library = File('build/native/$libraryName');
  final enabled = Platform.environment['ARCHOERACX_WIKI_TESTS'] == '1';

  test(
    'wiki.feed returns normalized items for every source',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final feed = await client.getWikiFeed(force: true, limit: 3);
      expect(feed, isNotNull);
      expect(feed!.sources, hasLength(3));
      expect(feed.items, isNotEmpty);

      for (final item in feed.items) {
        expect(item.id, isNotEmpty);
        expect(item.title, isNotEmpty);
        expect(
          item.source,
          anyOf('mojang-news', 'mojang-java', 'minecraft-wiki'),
        );
        expect(item.publishedAt, isNotNull);
      }

      for (final item in feed.items.where((item) => item.url != null)) {
        expect(item.url, startsWith('https://'));
        expect(item.url, isNot(contains('OCID=')));
      }

      // A second call inside the cache window must not refetch.
      final cached = await client.getWikiFeed(limit: 3);
      expect(cached!.cached, isTrue);
    },
    skip: !enabled || !library.existsSync()
        ? 'Set ARCHOERACX_WIKI_TESTS=1 after building the native core.'
        : false,
    timeout: const Timeout(Duration(seconds: 90)),
  );

  test(
    'system.status reports load through the ABI',
    () async {
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final status = await client.getSystemStatus(path: Directory.current.path);
      expect(status, isNotNull);
      expect(status!.cores, greaterThan(0));
      expect(status.hasMemory, isTrue);
      expect(status.memoryTotalMb, greaterThan(0));
      expect(status.memoryPercent, inInclusiveRange(0, 100));
      if (status.cpuPercent != null) {
        expect(status.cpuPercent, inInclusiveRange(0, 100));
      }
      expect(status.diskPath, isNotNull);
      expect(status.launcherWorkingSetMb, greaterThan(0));
    },
    skip: !library.existsSync()
        ? 'Run the CMake native build to enable the FFI smoke test.'
        : false,
    timeout: const Timeout(Duration(seconds: 30)),
  );
}
