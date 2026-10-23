import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/downloads/task_update.dart';
import 'preferences.dart';
import 'providers.dart';

/// Methods whose work is long enough to belong in the task center.
///
/// Every module call is an async native task, so the event stream also carries
/// short bookkeeping calls (`settings.get`, `instance.list`, `resource.search`
/// …). Showing those would fill the panel with rows that have no progress and
/// finish before the user can read them, which is why only these are tracked.
const _trackedMethods = <String>{
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
  'instance.duplicate',
  'launch.prepare',
  'launch.start',
};

bool isTrackedTaskMethod(String method) => _trackedMethods.contains(method);

/// Live task snapshots for the right-hand task center sidebar.
final taskCenterProvider =
    NotifierProvider<TaskCenterNotifier, List<TaskUpdate>>(
      TaskCenterNotifier.new,
    );

class TaskCenterNotifier extends Notifier<List<TaskUpdate>> {
  StreamSubscription<TaskUpdate>? _subscription;

  @override
  List<TaskUpdate> build() {
    _subscription?.cancel();
    final client = ref.watch(coreClientProvider);
    if (client != null) {
      _subscription = client.taskUpdates.listen(_onUpdate);
    }
    ref.onDispose(() => _subscription?.cancel());
    return const [];
  }

  int get activeCount => state.where((task) => !task.isTerminal).length;

  void _onUpdate(TaskUpdate update) {
    if (!isTrackedTaskMethod(update.method)) return;

    final next = [...state];
    final index = next.indexWhere((task) => task.taskId == update.taskId);
    if (index >= 0) {
      next[index] = update.carryOver(next[index]);
    } else {
      next.add(update);
    }

    // Keep the panel useful without growing forever; preserve running tasks.
    // The cap is a user preference (Tasks & logs in Settings).
    final limit = ref.read(preferencesProvider).taskHistoryLimit;
    final terminal = next.where((task) => task.isTerminal).toList();
    if (terminal.length > limit) {
      final remove = terminal
          .take(terminal.length - limit)
          .map((t) => t.taskId)
          .toSet();
      next.removeWhere((task) => remove.contains(task.taskId));
    }

    state = List.unmodifiable([
      ...next.where((task) => !task.isTerminal),
      ...next.where((task) => task.isTerminal),
    ]);
  }

  void clearCompleted() {
    state = List.unmodifiable(state.where((task) => !task.isTerminal));
  }
}
