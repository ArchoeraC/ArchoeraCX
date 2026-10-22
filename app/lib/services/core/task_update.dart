/// UI-facing snapshot of one native async task.
enum TaskUpdateState { running, completed, failed, cancelled }

final class TaskUpdate {
  const TaskUpdate({
    required this.taskId,
    required this.method,
    required this.state,
    this.label,
    this.done = 0,
    this.total = 0,
    this.bytesPerSecond = 0,
    this.message,
  });

  final int taskId;
  final String method;
  final TaskUpdateState state;

  /// Specific target of the task as shown to the user, e.g. a modpack name,
  /// a game version or an instance name. Supplied by the caller that started
  /// the task so the task center can say what is being worked on instead of
  /// only which module method ran.
  final String? label;

  final int done;
  final int total;
  final int bytesPerSecond;
  final String? message;

  bool get isTerminal =>
      state == TaskUpdateState.completed ||
      state == TaskUpdateState.failed ||
      state == TaskUpdateState.cancelled;

  double? get progress => total > 0 ? (done / total).clamp(0.0, 1.0) : null;

  /// Copy that keeps the last known progress and label.
  ///
  /// Terminal events carry no counters, so a completed row would otherwise
  /// forget how much it transferred.
  TaskUpdate carryOver(TaskUpdate previous) => TaskUpdate(
    taskId: taskId,
    method: method,
    state: state,
    label: label ?? previous.label,
    done: done > 0 ? done : previous.done,
    total: total > 0 ? total : previous.total,
    bytesPerSecond: state == TaskUpdateState.running ? bytesPerSecond : 0,
    message: message ?? previous.message,
  );
}
