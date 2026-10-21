/// UI-facing snapshot of one native async task.
enum TaskUpdateState { running, completed, failed, cancelled }

final class TaskUpdate {
  const TaskUpdate({
    required this.taskId,
    required this.method,
    required this.state,
    this.done = 0,
    this.total = 0,
    this.bytesPerSecond = 0,
    this.message,
  });

  final int taskId;
  final String method;
  final TaskUpdateState state;
  final int done;
  final int total;
  final int bytesPerSecond;
  final String? message;

  bool get isTerminal =>
      state == TaskUpdateState.completed ||
      state == TaskUpdateState.failed ||
      state == TaskUpdateState.cancelled;

  double? get progress => total > 0 ? (done / total).clamp(0.0, 1.0) : null;
}
