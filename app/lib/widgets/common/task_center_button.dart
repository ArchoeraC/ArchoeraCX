import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/task_update.dart';
import '../../stores/providers.dart';
import '../../stores/tasks.dart';
import '../../theme/tokens.dart';

class TaskCenterButton extends ConsumerWidget {
  const TaskCenterButton({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tasks = ref.watch(taskCenterProvider);
    final active = tasks.where((task) => !task.isTerminal).length;
    final tokens = context.tokens;

    return Tooltip(
      message: AppLocalizations.of(context).taskCenterTitle,
      child: IconButton(
        onPressed: () => _open(context),
        icon: Badge(
          isLabelVisible: active > 0,
          label: Text('$active'),
          child: Icon(
            active > 0 ? Icons.downloading_rounded : Icons.task_alt_rounded,
            color: active > 0 ? tokens.accent : tokens.textSecondary,
          ),
        ),
      ),
    );
  }

  void _open(BuildContext context) {
    showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      constraints: BoxConstraints(
        maxHeight: MediaQuery.sizeOf(context).height * 0.72,
      ),
      builder: (_) => const _TaskCenterPanel(),
    );
  }
}

class _TaskCenterPanel extends ConsumerWidget {
  const _TaskCenterPanel();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final tasks = ref.watch(taskCenterProvider);
    final hasCompleted = tasks.any((task) => task.isTerminal);

    return SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(
              AppSpacing.lg,
              0,
              AppSpacing.sm,
              AppSpacing.sm,
            ),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    l10n.taskCenterTitle,
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ),
                if (hasCompleted)
                  TextButton(
                    onPressed: () =>
                        ref.read(taskCenterProvider.notifier).clearCompleted(),
                    child: Text(l10n.taskCenterClear),
                  ),
              ],
            ),
          ),
          Divider(height: 1, color: tokens.border),
          if (tasks.isEmpty)
            Padding(
              padding: const EdgeInsets.all(AppSpacing.xl),
              child: Text(
                l10n.taskCenterEmpty,
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodyMedium
                    ?.copyWith(color: tokens.textSecondary),
              ),
            )
          else
            Flexible(
              child: ListView.separated(
                shrinkWrap: true,
                padding: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
                itemCount: tasks.length,
                separatorBuilder: (_, _) =>
                    Divider(height: 1, color: tokens.border),
                itemBuilder: (context, index) => _TaskRow(task: tasks[index]),
              ),
            ),
        ],
      ),
    );
  }
}

class _TaskRow extends ConsumerWidget {
  const _TaskRow({required this.task});

  final TaskUpdate task;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final active = !task.isTerminal;
    final progress = task.progress;
    final stateLabel = switch (task.state) {
      TaskUpdateState.completed => l10n.taskCenterCompleted,
      TaskUpdateState.failed => l10n.taskCenterFailed,
      TaskUpdateState.cancelled => l10n.taskCenterCancelled,
      TaskUpdateState.running => null,
    };

    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.lg,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            switch (task.state) {
              TaskUpdateState.completed => Icons.check_circle_outline_rounded,
              TaskUpdateState.failed => Icons.error_outline_rounded,
              TaskUpdateState.cancelled => Icons.block_rounded,
              TaskUpdateState.running => Icons.downloading_rounded,
            },
            size: 20,
            color: switch (task.state) {
              TaskUpdateState.completed => tokens.success,
              TaskUpdateState.failed => tokens.danger,
              TaskUpdateState.cancelled => tokens.textTertiary,
              TaskUpdateState.running => tokens.accent,
            },
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        _taskLabel(l10n, task.method),
                        style: Theme.of(context).textTheme.titleSmall,
                      ),
                    ),
                    if (stateLabel != null)
                      Text(
                        stateLabel,
                        style: Theme.of(context).textTheme.labelSmall?.copyWith(
                          color: task.state == TaskUpdateState.failed
                              ? tokens.danger
                              : tokens.textTertiary,
                        ),
                      ),
                  ],
                ),
                if (active) ...[
                  const SizedBox(height: AppSpacing.xs),
                  LinearProgressIndicator(value: progress),
                  const SizedBox(height: AppSpacing.xxs),
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          _progressText(task, l10n),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.labelSmall
                              ?.copyWith(color: tokens.textTertiary),
                        ),
                      ),
                      if (task.bytesPerSecond > 0)
                        Text(
                          l10n.taskCenterSpeed(
                            _formatBytes(task.bytesPerSecond),
                          ),
                          style: Theme.of(context).textTheme.labelSmall
                              ?.copyWith(color: tokens.textTertiary),
                        ),
                    ],
                  ),
                ],
                if (active && task.message?.isNotEmpty == true)
                  Text(
                    task.message!,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.labelSmall
                        ?.copyWith(color: tokens.textTertiary),
                  ),
              ],
            ),
          ),
          if (active) ...[
            const SizedBox(width: AppSpacing.xs),
            TextButton(
              onPressed: () =>
                  ref.read(coreClientProvider)?.cancel(task.taskId),
              child: Text(l10n.taskCenterCancel),
            ),
          ],
        ],
      ),
    );
  }

  String _taskLabel(AppLocalizations l10n, String method) => switch (method) {
    'download.execute' => l10n.taskCenterDownload,
    'version.plan' || 'version.commit' => l10n.taskCenterGameFiles,
    'loader.plan' ||
    'loader.commit' ||
    'loader.repair' ||
    'loader.versions' => l10n.taskCenterLoader,
    'java.scan' => l10n.taskCenterJavaScan,
    _ => l10n.taskCenterTask,
  };

  String _progressText(TaskUpdate task, AppLocalizations l10n) {
    if (task.total > 0) {
      return '${_formatBytes(task.done)} / ${_formatBytes(task.total)}';
    }
    if (task.done > 0) return _formatBytes(task.done);
    return l10n.taskCenterWorking;
  }
}

String _formatBytes(int bytes) {
  if (bytes >= 1024 * 1024 * 1024) {
    return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
  }
  if (bytes >= 1024 * 1024) {
    return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
  }
  if (bytes >= 1024) return '${(bytes / 1024).toStringAsFixed(0)} KB';
  return '$bytes B';
}
