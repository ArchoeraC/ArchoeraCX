import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/launch/launch_progress.dart';

/// Localized label for a launch stage number. Unknown values fall back to the
/// generic starting text so a newer module can add stages without breaking the
/// checklist.
String launchStageLabel(AppLocalizations l10n, int step) => switch (step) {
  1 => l10n.launchStageInstance,
  2 => l10n.launchStageAccount,
  3 => l10n.launchStageVersion,
  4 => l10n.launchStageJava,
  5 => l10n.launchStageFiles,
  6 => l10n.launchStageRepair,
  7 => l10n.launchStagePlan,
  8 => l10n.launchStageProcess,
  _ => l10n.launchStarting,
};

/// Localized label for the stable stage token carried by launch error details.
String launchStageTokenLabel(AppLocalizations l10n, String? token) =>
    switch (token) {
      'instance' => l10n.launchStageInstance,
      'account' => l10n.launchStageAccount,
      'version' => l10n.launchStageVersion,
      'java' => l10n.launchStageJava,
      'files' => l10n.launchStageFiles,
      'repair' => l10n.launchStageRepair,
      'plan' => l10n.launchStagePlan,
      'process' => l10n.launchStageProcess,
      _ => l10n.launchStarting,
    };

/// Modal that explains what the launcher is currently doing. It is driven by
/// the C# stage reports; without reports it degrades to an indeterminate bar.
///
/// The dialog owns the controller and disposes it when the route is gone. The
/// user may dismiss it (barrier tap, back or the background button) while the
/// launch keeps running; the controller then ignores late reports.
class LaunchProgressDialog extends StatefulWidget {
  const LaunchProgressDialog({
    super.key,
    required this.instanceName,
    required this.progress,
  });

  final String instanceName;
  final LaunchProgressController progress;

  @override
  State<LaunchProgressDialog> createState() => _LaunchProgressDialogState();
}

class _LaunchProgressDialogState extends State<LaunchProgressDialog> {
  @override
  void dispose() {
    widget.progress.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final theme = Theme.of(context);
    final progress = widget.progress;
    return AlertDialog(
      title: Text(l10n.launchProgressTitle(widget.instanceName)),
      content: SizedBox(
        width: 360,
        child: AnimatedBuilder(
          animation: progress,
          builder: (context, _) {
            final furthest = progress.furthest;
            // Reports mark completed stages, so the stage being worked on is
            // the next one. Once the last stage is reached nothing spins.
            final currentStep = progress.hasReports && furthest < progress.total
                ? furthest + 1
                : 0;
            return Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                LinearProgressIndicator(
                  value: progress.hasReports ? furthest / progress.total : null,
                ),
                const SizedBox(height: 16),
                if (!progress.hasReports)
                  Text(l10n.launchStarting, style: theme.textTheme.bodyMedium)
                else
                  ...List<Widget>.generate(progress.total, (index) {
                    final step = index + 1;
                    return _StageRow(
                      label: launchStageLabel(l10n, step),
                      state: step == currentStep
                          ? _StageState.current
                          : progress.reached.contains(step)
                          ? _StageState.done
                          : progress.skipped.contains(step)
                          ? _StageState.skipped
                          : _StageState.pending,
                      skippedLabel: l10n.launchStageSkipped,
                    );
                  }),
              ],
            );
          },
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.launchProgressBackground),
        ),
      ],
    );
  }
}

enum _StageState { done, current, pending, skipped }

class _StageRow extends StatelessWidget {
  const _StageRow({
    required this.label,
    required this.state,
    required this.skippedLabel,
  });

  final String label;
  final _StageState state;
  final String skippedLabel;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final color = switch (state) {
      _StageState.done || _StageState.current => theme.colorScheme.primary,
      _ => theme.colorScheme.outline,
    };
    final icon = switch (state) {
      _StageState.done => Icon(
        Icons.check_circle_outline,
        size: 18,
        color: color,
      ),
      _StageState.current => SizedBox(
        width: 16,
        height: 16,
        child: CircularProgressIndicator(strokeWidth: 2, color: color),
      ),
      _StageState.skipped => Icon(
        Icons.remove_circle_outline,
        size: 18,
        color: color,
      ),
      _StageState.pending => Icon(
        Icons.radio_button_unchecked,
        size: 18,
        color: color,
      ),
    };
    final textStyle = state == _StageState.current
        ? theme.textTheme.bodyMedium?.copyWith(
            color: theme.colorScheme.onSurface,
            fontWeight: FontWeight.w600,
          )
        : theme.textTheme.bodyMedium?.copyWith(
            color: state == _StageState.pending
                ? theme.colorScheme.outline
                : theme.colorScheme.onSurfaceVariant,
          );
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Row(
        children: [
          icon,
          const SizedBox(width: 10),
          Expanded(child: Text(label, style: textStyle)),
          if (state == _StageState.skipped)
            Text(
              skippedLabel,
              style: theme.textTheme.labelSmall?.copyWith(
                color: theme.colorScheme.outline,
              ),
            ),
        ],
      ),
    );
  }
}
