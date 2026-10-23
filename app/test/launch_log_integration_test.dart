import 'dart:io';

import 'package:archoera_cx/services/core/core_client.dart';
import 'package:flutter_test/flutter_test.dart';

/// End-to-end check for the launch log viewer path:
/// Dart -> C++ method registry -> C# `launch.log*` -> log file on disk.
void main() {
  final enabled = Platform.environment['ARCHOERACX_LOG_TESTS'] == '1';
  final core = File(
    Platform.isWindows
        ? 'build/native/archoera_core.dll'
        : Platform.isMacOS
        ? 'build/native/libarchoera_core.dylib'
        : 'build/native/libarchoera_core.so',
  );
  final module = File(
    Platform.isWindows
        ? 'build/native/archoera_cs.dll'
        : Platform.isMacOS
        ? 'build/native/libarchoera_cs.dylib'
        : 'build/native/archoera_cs.so',
  );

  test(
    'launch.logs/read cross C++ and C# and return the log tail',
    () async {
      final directory = await Directory.systemTemp.createTemp('archoera-log-');
      addTearDown(() => directory.delete(recursive: true));
      final client = CoreClient.openDefault();
      addTearDown(client.close);

      final root = '${directory.path}/logged instance';
      final instance = await client.createInstance(
        name: 'Logged Instance',
        gameVersion: '1.21.4',
        loader: 'fabric',
        loaderVersion: '0.16.10',
        rootPath: root,
      );

      final empty = await client.listLaunchLogs(instance.id);
      expect(empty.instanceId, instance.id);
      expect(empty.running, isFalse);
      expect(empty.logs, isEmpty);

      final logDirectory = Directory('$root/.archoera/logs');
      logDirectory.createSync(recursive: true);
      final older = File('${logDirectory.path}/launch-20260101-120000.log')
        ..writeAsStringSync('[12:00:00] [main/INFO] old run\n');
      final newer = File('${logDirectory.path}/launch-20260102-120000.log')
        ..writeAsStringSync(
          List.generate(
            30,
            (index) => '[12:00:0$index] [main/INFO] line $index',
          ).join('\n'),
        );
      older.setLastModifiedSync(DateTime.utc(2026, 1, 1, 12));
      newer.setLastModifiedSync(DateTime.utc(2026, 1, 2, 12));

      final list = await client.listLaunchLogs(instance.id);
      expect(list.logs.length, 2);
      expect(list.logs.first.fileName, 'launch-20260102-120000.log');
      expect(list.logs.first.active, isFalse);

      final snapshot = await client.readLaunchLog(
        instanceId: instance.id,
        lines: 5,
      );
      expect(snapshot.fileName, 'launch-20260102-120000.log');
      expect(snapshot.lines.length, 5);
      expect(snapshot.lines.last, contains('line 29'));
      expect(snapshot.running, isFalse);

      final explicit = await client.readLaunchLog(
        instanceId: instance.id,
        fileName: 'launch-20260101-120000.log',
      );
      expect(explicit.lines.single, contains('old run'));

      // The streaming path also ends on its own when no game process is
      // registered, so the viewer can follow a finished log safely.
      final tail = await client.tailLaunchLog(instanceId: instance.id);
      final chunks = <String>[];
      final subscription = tail.chunks.listen(chunks.add);
      await tail.done;
      await subscription.cancel();
      final streamed = chunks.join('\n');
      expect(streamed, contains('line 29'));

      // Force-stop is harmless when no game is running: the module reports
      // stopped=false instead of inventing a pid to kill.
      final stopped = await client.stopLaunch(instance.id);
      expect(stopped.instanceId, instance.id);
      expect(stopped.stopped, isFalse);
      expect(stopped.pid, isNull);

      await client.deleteInstance(instance.id);
    },
    skip: enabled && core.existsSync() && module.existsSync()
        ? false
        : 'Set ARCHOERACX_LOG_TESTS=1 and build core/module to run.',
    timeout: const Timeout(Duration(seconds: 20)),
  );
}
