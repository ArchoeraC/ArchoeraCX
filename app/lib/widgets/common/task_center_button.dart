import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/task_update.dart';
import '../../stores/providers.dart';
import '../../stores/tasks.dart';
import '../../theme/tokens.dart';
import '../common/app_progress_bar.dart';

/// Title-bar entry point for the task center.
///
/// Tasks open as a right-hand sidebar that slides in over the content instead
/// of a bottom sheet: the list is tall enough to keep several downloads
/// readable and does not cover the command bar.
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
        onPressed: () => openTaskCenterSidebar(context),
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
}

/// Shows the task center as a modal sidebar pinned to the right edge.
Future<void> openTaskCenterSidebar(BuildContext context) {
  final tokens = context.tokens;
  final l10n = AppLocalizations.of(context);

  return showGeneralDialog<void>(
    context: context,
    barrierDismissible: true,
    barrierLabel: l10n.taskCenterTitle,
    barrierColor: tokens.overlay.withValues(alpha: 0.55),
    transitionDuration: tokens.slow,
    pageBuilder: (_, _, _) => const Align(
      alignment: Alignment.centerRight,
      child: _TaskCenterSidebar(),
    ),
    transitionBuilder: (context, animation, _, child) {
      final curved = CurvedAnimation(
        parent: animation,
        curve: tokens.curve,
        reverseCurve: Curves.easeInCubic,
      );
      return SlideTransition(
        position: Tween<Offset>(
          begin: const Offset(1, 0),
          end: Offset.zero,
        ).animate(curved),
        child: child,
      );
    },
  );
}

class _TaskCenterSidebar extends ConsumerWidget {
  const _TaskCenterSidebar();

  /// Wide enough for progress text and speed, capped on small windows.
  static const double _width = 380;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final tasks = ref.watch(taskCenterProvider);
    final active = tasks.where((task) => !task.isTerminal).length;
    final hasCompleted = tasks.length > active;
    final screenWidth = MediaQuery.sizeOf(context).width;
    final panelWidth = screenWidth < _width + AppSpacing.xl
        ? screenWidth - AppSpacing.xl
        : _width;

    final shape = BeveledRectangleBorder(
      side: BorderSide(color: tokens.border),
      borderRadius: BorderRadius.only(topLeft: Radius.circular(tokens.bevelL)),
    );

    return CallbackShortcuts(
      bindings: {
        const SingleActivator(LogicalKeyboardKey.escape): () =>
            Navigator.of(context).maybePop(),
      },
      child: Focus(
        autofocus: true,
        child: SizedBox(
          width: panelWidth.clamp(0.0, _width),
          height: double.infinity,
          child: Material(
            color: Colors.transparent,
            child: ClipPath(
              clipper: ShapeBorderClipper(shape: shape),
              child: DecoratedBox(
                decoration: ShapeDecoration(
                  color: tokens.surface1,
                  shape: shape,
                ),
                child: SafeArea(
                  left: false,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _SidebarHeader(
                        active: active,
                        hasCompleted: hasCompleted,
                        onClear: () => ref
                            .read(taskCenterProvider.notifier)
                            .clearCompleted(),
                      ),
                      Divider(height: 1, color: tokens.border),
                      Expanded(
                        child: tasks.isEmpty
                            ? Center(
                                child: Padding(
                                  padding: const EdgeInsets.all(AppSpacing.xl),
                                  child: Text(
                                    l10n.taskCenterEmpty,
                                    textAlign: TextAlign.center,
                                    style: Theme.of(context)
                                        .textTheme
                                        .bodyMedium
                                        ?.copyWith(color: tokens.textSecondary),
                                  ),
                                ),
                              )
                            : ListView.separated(
                                padding: const EdgeInsets.symmetric(
                                  vertical: AppSpacing.xs,
                                ),
                                itemCount: tasks.length,
                                separatorBuilder: (_, _) =>
                                    Divider(height: 1, color: tokens.border),
                                itemBuilder: (context, index) =>
                                    _TaskRow(task: tasks[index]),
                              ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _SidebarHeader extends StatelessWidget {
  const _SidebarHeader({
    required this.active,
    required this.hasCompleted,
    required this.onClear,
  });

  final int active;
  final bool hasCompleted;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return Padding(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.lg,
        AppSpacing.md,
        AppSpacing.xs,
        AppSpacing.sm,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(l10n.taskCenterTitle, style: textTheme.titleLarge),
                if (active > 0)
                  Text(
                    l10n.taskCenterActive(active),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.labelSmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
              ],
            ),
          ),
          if (hasCompleted)
            TextButton(
              onPressed: onClear,
              style: TextButton.styleFrom(
                visualDensity: VisualDensity.compact,
                padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xs),
              ),
              child: Text(l10n.taskCenterClear),
            ),
          IconButton(
            tooltip: l10n.close,
            onPressed: () => Navigator.of(context).maybePop(),
            visualDensity: VisualDensity.compact,
            icon: const Icon(Icons.close_rounded, size: 18),
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
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.lg,
        AppSpacing.sm,
        AppSpacing.xs,
        AppSpacing.sm,
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
                        _title(l10n, task),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
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
                  AppProgressBar(value: progress),
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
                ] else if (task.done > 0)
                  Text(
                    _formatBytes(task.done),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.labelSmall
                        ?.copyWith(color: tokens.textTertiary),
                  ),
                if (task.message?.isNotEmpty == true)
                  Text(
                    task.message!,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.labelSmall?.copyWith(
                      color: task.state == TaskUpdateState.failed
                          ? tokens.danger
                          : tokens.textTertiary,
                    ),
                  ),
              ],
            ),
          ),
          if (active)
            IconButton(
              tooltip: l10n.taskCenterCancel,
              onPressed: () =>
                  ref.read(coreClientProvider)?.cancel(task.taskId),
              visualDensity: VisualDensity.compact,
              iconSize: 18,
              icon: Icon(Icons.close_rounded, color: tokens.textTertiary),
            ),
        ],
      ),
    );
  }

  /// Row title: the module category plus the specific target when the caller
  /// supplied one, e.g. "Modpack · AutumnTown" instead of a bare "Task".
  String _title(AppLocalizations l10n, TaskUpdate task) {
    final category = _taskLabel(l10n, task.method);
    final target = task.label?.trim();
    if (target == null || target.isEmpty) return category;
    return l10n.taskCenterTaskWithTarget(category, target);
  }

  String _taskLabel(AppLocalizations l10n, String method) => switch (method) {
    'download.execute' => l10n.taskCenterDownload,
    'version.plan' || 'version.commit' => l10n.taskCenterGameFiles,
    'loader.plan' ||
    'loader.commit' ||
    'loader.repair' ||
    'loader.autoRepair' ||
    'loader.versions' => l10n.taskCenterLoader,
    'modpack.install' ||
    'modpack.installLocal' ||
    'modpack.inspect' => l10n.taskCenterModpack,
    'launch.start' || 'launch.prepare' => l10n.taskCenterLaunch,
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
