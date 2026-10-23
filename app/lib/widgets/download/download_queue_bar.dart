import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/task_update.dart';
import '../../stores/downloads.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../common/app_progress_bar.dart';
import '../common/task_center_button.dart';

/// Slim strip under the download page that keeps background transfers visible.
///
/// Downloads keep running while the user browses, so the page needs a permanent
/// (if tiny) readout: how many transfers are live, aggregate progress and
/// speed, plus a shortcut into the task center for per-task detail and cancel.
class DownloadQueueBar extends ConsumerWidget {
  const DownloadQueueBar({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tasks = ref.watch(activeDownloadsProvider);
    final tokens = context.tokens;

    return AnimatedSize(
      duration: tokens.normal,
      curve: tokens.curve,
      alignment: Alignment.topCenter,
      child: tasks.isEmpty
          ? const SizedBox(width: double.infinity)
          : _QueueBody(tasks: tasks),
    );
  }
}

class _QueueBody extends StatelessWidget {
  const _QueueBody({required this.tasks});

  final List<TaskUpdate> tasks;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    final total = tasks.fold<int>(0, (sum, task) => sum + task.total);
    final done = tasks.fold<int>(
      0,
      (sum, task) => sum + (task.total > 0 ? task.done : 0),
    );
    final speed = tasks.fold<int>(0, (sum, task) => sum + task.bytesPerSecond);
    final progress = total > 0 ? (done / total).clamp(0.0, 1.0) : null;
    final lead = tasks.first;

    return Container(
      decoration: BoxDecoration(
        border: Border(top: BorderSide(color: tokens.border)),
      ),
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.xxs,
        AppSpacing.xs,
        AppSpacing.xxs,
        AppSpacing.xs,
      ),
      child: LayoutBuilder(
        builder: (context, constraints) {
          final detailed = constraints.maxWidth >= 860;
          return Row(
            children: [
              Icon(AppIcons.running, size: 16, color: tokens.accent),
              const SizedBox(width: AppSpacing.xs),
              Flexible(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      l10n.downloadQueueActive(tasks.length),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.labelMedium?.copyWith(
                        color: tokens.textPrimary,
                      ),
                    ),
                    if (lead.label != null)
                      Text(
                        lead.label!,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: textTheme.labelSmall?.copyWith(
                          color: tokens.textTertiary,
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(width: AppSpacing.md),
              Expanded(child: AppProgressBar(value: progress, height: 4)),
              const SizedBox(width: AppSpacing.md),
              if (detailed && total > 0)
                Text(
                  l10n.downloadQueueProgress(
                    _formatBytes(done),
                    _formatBytes(total),
                  ),
                  style: textTheme.labelSmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              if (detailed && speed > 0) ...[
                const SizedBox(width: AppSpacing.sm),
                Text(
                  l10n.taskCenterSpeed(_formatSpeed(speed)),
                  style: textTheme.labelSmall?.copyWith(
                    color: tokens.textSecondary,
                  ),
                ),
              ],
              const SizedBox(width: AppSpacing.xs),
              TextButton(
                onPressed: () => openTaskCenterSidebar(context),
                style: TextButton.styleFrom(
                  visualDensity: VisualDensity.compact,
                  padding: const EdgeInsets.symmetric(
                    horizontal: AppSpacing.sm,
                  ),
                ),
                child: Text(l10n.downloadQueueOpen),
              ),
            ],
          );
        },
      ),
    );
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

String _formatSpeed(int bytesPerSecond) {
  if (bytesPerSecond >= 1024 * 1024) {
    return '${(bytesPerSecond / (1024 * 1024)).toStringAsFixed(1)} MB';
  }
  if (bytesPerSecond >= 1024) {
    return '${(bytesPerSecond / 1024).toStringAsFixed(0)} KB';
  }
  return '$bytesPerSecond B';
}
