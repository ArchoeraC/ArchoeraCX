import 'package:archoera_cx/app/app.dart';
import 'package:archoera_cx/services/core/task_update.dart';
import 'package:archoera_cx/stores/providers.dart';
import 'package:archoera_cx/stores/tasks.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

class _FixedTasks extends TaskCenterNotifier {
  _FixedTasks(this.initial);

  final List<TaskUpdate> initial;

  @override
  List<TaskUpdate> build() => initial;

  @override
  int get activeCount => state.where((task) => !task.isTerminal).length;

  @override
  void clearCompleted() {
    state = List.unmodifiable(state.where((task) => !task.isTerminal));
  }
}

void main() {
  group('task tracking', () {
    test('only long-running module methods are tracked', () {
      for (final method in [
        'download.execute',
        'version.plan',
        'version.commit',
        'loader.plan',
        'loader.commit',
        'loader.repair',
        'loader.autoRepair',
        'modpack.inspect',
        'modpack.install',
        'modpack.installLocal',
        'launch.prepare',
        'launch.start',
      ]) {
        expect(isTrackedTaskMethod(method), isTrue, reason: method);
      }

      for (final method in [
        'settings.get',
        'settings.set',
        'instance.list',
        'instance.sync',
        'account.list',
        'resource.search',
        'resource.versions',
        'system.memory',
        'java.scan',
        'launch.logs',
        'launch.log.read',
        'launch.log.tail',
      ]) {
        expect(isTrackedTaskMethod(method), isFalse, reason: method);
      }
    });

    test('terminal updates keep the last known progress', () {
      const running = TaskUpdate(
        taskId: 7,
        method: 'download.execute',
        state: TaskUpdateState.running,
        label: 'Sodium',
        done: 1200,
        total: 4000,
        bytesPerSecond: 900,
        message: 'assets/objects/ab/abcdef',
      );
      const completed = TaskUpdate(
        taskId: 7,
        method: 'download.execute',
        state: TaskUpdateState.completed,
      );

      final merged = completed.carryOver(running);

      expect(merged.state, TaskUpdateState.completed);
      expect(merged.label, 'Sodium');
      expect(merged.done, 1200);
      expect(merged.total, 4000);
      expect(merged.bytesPerSecond, 0);
      expect(merged.message, 'assets/objects/ab/abcdef');
      expect(merged.progress, closeTo(0.3, 0.0001));
    });
  });

  testWidgets('sidebar lists the specific target of each task', (tester) async {
    tester.view.physicalSize = const Size(1440, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          coreClientProvider.overrideWithValue(null),
          taskCenterProvider.overrideWith(
            () => _FixedTasks(const [
              TaskUpdate(
                taskId: 1,
                method: 'modpack.install',
                state: TaskUpdateState.running,
                label: 'AutumnTown',
                done: 2048,
                total: 4096,
                bytesPerSecond: 1024,
                message: 'mods/create-1.21.1.jar',
              ),
              TaskUpdate(
                taskId: 2,
                method: 'download.execute',
                state: TaskUpdateState.completed,
                label: 'Sodium',
                done: 1048576,
                total: 1048576,
              ),
            ]),
          ),
        ],
        child: const ArchoeraApp(),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byIcon(Icons.downloading_rounded));
    await tester.pumpAndSettle();

    expect(find.text('Modpack · AutumnTown'), findsOneWidget);
    expect(find.text('2 KB / 4 KB'), findsOneWidget);
    expect(find.text('mods/create-1.21.1.jar'), findsOneWidget);
    expect(find.text('Download · Sodium'), findsOneWidget);
    expect(find.text('1.0 MB'), findsOneWidget);
    expect(find.text('Done'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}
