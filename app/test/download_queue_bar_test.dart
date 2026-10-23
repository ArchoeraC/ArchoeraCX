import 'package:archoera_cx/l10n/generated/app_localizations.dart';
import 'package:archoera_cx/services/core/downloads/task_update.dart';
import 'package:archoera_cx/stores/tasks.dart';
import 'package:archoera_cx/theme/app_theme.dart';
import 'package:archoera_cx/widgets/common/app_progress_bar.dart';
import 'package:archoera_cx/widgets/download/download_queue_bar.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';

/// Task state without a native core: the queue strip is a pure projection of
/// [taskCenterProvider], so a fixed list is enough.
class _FixedTasks extends TaskCenterNotifier {
  _FixedTasks(this._tasks);

  final List<TaskUpdate> _tasks;

  @override
  List<TaskUpdate> build() => _tasks;
}

TaskUpdate _task(int id, {int done = 0, int total = 0, int speed = 0}) =>
    TaskUpdate(
      taskId: id,
      method: 'download.execute',
      state: TaskUpdateState.running,
      label: 'Sodium',
      done: done,
      total: total,
      bytesPerSecond: speed,
    );

Widget _wrap(List<TaskUpdate> tasks, {double width = 1000}) => ProviderScope(
  overrides: [taskCenterProvider.overrideWith(() => _FixedTasks(tasks))],
  child: MaterialApp(
    theme: AppTheme.light,
    locale: const Locale('en'),
    localizationsDelegates: AppLocalizations.localizationsDelegates,
    supportedLocales: AppLocalizations.supportedLocales,
    home: Builder(
      builder: (context) => Scaffold(
        body: Align(
          alignment: Alignment.bottomCenter,
          child: SizedBox(width: width, child: const DownloadQueueBar()),
        ),
      ),
    ),
  ),
);

void main() {
  testWidgets('queue bar hides when nothing is downloading', (tester) async {
    await tester.pumpWidget(_wrap(const []));
    await tester.pumpAndSettle();

    expect(find.byType(AppProgressBar), findsNothing);
    expect(find.text('View tasks'), findsNothing);
  });

  testWidgets('queue bar summarises running downloads', (tester) async {
    tester.view.physicalSize = const Size(1400, 800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      _wrap([
        _task(1, done: 5 << 20, total: 10 << 20, speed: 1 << 20),
        _task(2, done: 1 << 20, total: 4 << 20, speed: 512 << 10),
      ]),
    );
    await tester.pumpAndSettle();

    expect(find.text('2 downloads in progress'), findsOneWidget);
    expect(find.text('Sodium'), findsOneWidget);
    // Aggregate bytes and speed across both tasks.
    expect(find.text('6.0 MB / 14.0 MB'), findsOneWidget);
    expect(find.text('1.5 MB/s'), findsOneWidget);
    expect(find.byType(AppProgressBar), findsOneWidget);
    expect(find.text('View tasks'), findsOneWidget);
  });

  testWidgets('queue bar stays compact on narrow windows', (tester) async {
    tester.view.physicalSize = const Size(700, 600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);

    await tester.pumpWidget(
      _wrap([
        _task(1, done: 5 << 20, total: 10 << 20, speed: 1 << 20),
      ], width: 660),
    );
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(find.text('1 download in progress'), findsOneWidget);
    expect(find.text('1.0 MB/s'), findsNothing);
  });
}
