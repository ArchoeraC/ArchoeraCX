import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../services/core/task_update.dart';
import 'providers.dart';

/// Live task snapshots for the top-right task center.
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
    final next = [...state];
    final index = next.indexWhere((task) => task.taskId == update.taskId);
    if (index >= 0) {
      next[index] = update;
    } else {
      next.add(update);
    }

    // Keep the panel useful without growing forever; preserve running tasks.
    final terminal = next.where((task) => task.isTerminal).toList();
    if (terminal.length > 12) {
      final remove = terminal
          .take(terminal.length - 12)
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
