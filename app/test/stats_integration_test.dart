import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final enabled = Platform.environment['ARCHOERACX_STATS_TESTS'] == '1';

  test(
    'stats.get crosses C++ and C# and returns one coherent snapshot',
    () async {
      final directory = await Directory.systemTemp.createTemp(
        'archoera-stats-',
      );
      addTearDown(() => directory.delete(recursive: true));
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final created = await client.createInstance(
        name: 'Stats Integration',
        gameVersion: '1.21.4',
        loader: 'fabric',
        loaderVersion: '0.16.10',
        rootPath: '${directory.path}/instance',
      );

      final stats = await client.getPlayStats();

      expect(stats.summary.instanceCount, greaterThanOrEqualTo(1));
      expect(stats.daily, hasLength(90));
      expect(stats.hourly, hasLength(24));
      expect(stats.daily.last.day.weekday, inInclusiveRange(1, 7));
      expect(
        stats.loaders.any((entry) => entry.loader == 'fabric'),
        isTrue,
        reason: 'the current instance mix must include the created instance',
      );
      expect(
        stats.topInstances.where((entry) => entry.instanceId == created.id),
        isEmpty,
        reason: 'a launched session is required before ranking an instance',
      );
    },
    skip: enabled ? false : 'set ARCHOERACX_STATS_TESTS=1 to run',
  );
}
