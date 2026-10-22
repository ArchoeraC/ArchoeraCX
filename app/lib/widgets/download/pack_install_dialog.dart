import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/core_client.dart';
import '../../services/core/pack_install_result.dart';
import '../../stores/instances.dart';
import '../../stores/tasks.dart';
import '../../stores/providers.dart';
import '../../theme/tokens.dart';
import '../common/app_progress_bar.dart';

Future<PackInstallResult?> showPackInstallDialog(
  BuildContext context, {
  String? localPath,
  String? projectId,
  String? versionId,
  String? title,
  String? taskLabel,
}) {
  return showDialog<PackInstallResult>(
    context: context,
    barrierDismissible: false,
    builder: (_) => PackInstallDialog(
      localPath: localPath,
      projectId: projectId,
      versionId: versionId,
      title: title,
      taskLabel: taskLabel,
    ),
  );
}

/// Progress dialog shared by the local import flow and Modrinth modpack tab.
/// The backend creates the instance and loads the pack; this widget only
/// reports phases and refreshes the instance mirror on success.
class PackInstallDialog extends ConsumerStatefulWidget {
  const PackInstallDialog({
    super.key,
    this.localPath,
    this.projectId,
    this.versionId,
    this.title,
    this.taskLabel,
  });

  final String? localPath;
  final String? projectId;
  final String? versionId;
  final String? title;

  /// Human name of the pack shown in the task center sidebar. Falls back to
  /// the inspected pack name or the file name.
  final String? taskLabel;

  @override
  ConsumerState<PackInstallDialog> createState() => _PackInstallDialogState();
}

class _PackInstallDialogState extends ConsumerState<PackInstallDialog> {
  PackInspection? _inspection;
  PackInstallResult? _result;
  String? _error;
  String? _phase;
  bool _busy = false;
  bool _started = false;
  int _doneBytes = 0;
  int _totalBytes = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _run();
    });
  }

  Future<void> _run() async {
    if (_busy || _started && _result != null) return;
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(
        () => _error = AppLocalizations.of(context).commonCoreNotConnected,
      );
      return;
    }

    setState(() {
      _busy = true;
      _started = true;
      _error = null;
      _result = null;
      _doneBytes = 0;
      _totalBytes = 0;
      _phase = AppLocalizations.of(context).packInstallRunning;
    });

    try {
      final localPath = widget.localPath;
      if (localPath != null && localPath.isNotEmpty) {
        if (_inspection == null) {
          try {
            final inspection = await client.inspectModpack(localPath);
            if (!mounted) return;
            setState(() => _inspection = inspection);
          } catch (error) {
            if (mounted) {
              setState(() {
                _busy = false;
                _error = AppLocalizations.of(context).packInspectFailed;
              });
            }
            return;
          }
        }

        final result = await client.installModpackLocal(
          path: localPath,
          label:
              widget.taskLabel ??
              _inspection?.name ??
              localPath.split('/').last,
          onDetailedProgress: _onProgress,
        );
        await _complete(result);
        return;
      }

      final projectId = widget.projectId;
      final versionId = widget.versionId;
      if (projectId == null || versionId == null) {
        throw StateError('No pack source was provided.');
      }

      final result = await client.installModpackFromModrinth(
        projectId: projectId,
        versionId: versionId,
        label: widget.taskLabel,
        onDetailedProgress: _onProgress,
      );
      await _complete(result);
    } catch (error) {
      if (!mounted) return;
      final l10n = AppLocalizations.of(context);
      setState(() {
        _busy = false;
        _error = switch (error) {
          CoreException(code: 'E_CANCELLED') => l10n.packCancelled,
          CoreException(code: 'PACK_LOADER_UNSUPPORTED') =>
            l10n.packLoaderUnsupported,
          CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
            l10n.packJavaMissing,
          _ => error.toString(),
        };
      });
    }
  }

  void _cancel() {
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    for (final task in ref.read(taskCenterProvider)) {
      if (task.isTerminal || !task.method.startsWith('modpack.')) continue;
      try {
        client.cancel(task.taskId);
        setState(() => _phase = AppLocalizations.of(context).packCancelling);
      } catch (error) {
        if (mounted) setState(() => _error = error.toString());
      }

      return;
    }
  }

  void _onProgress(int done, int total, String? message) {
    if (!mounted) return;
    setState(() {
      _doneBytes = done;
      _totalBytes = total;
      if (message != null && message.isNotEmpty) _phase = message;
    });
  }

  Future<void> _complete(PackInstallResult result) async {
    try {
      // Re-scan once so the row picks up the final mod count and any
      // metadata written by the pack install transaction.
      await ref.read(instancesProvider.notifier).sync();
      ref.read(selectedInstanceIdProvider.notifier).select(result.instance.id);
    } catch (_) {
      // The install succeeded; a failed refresh must not hide that.
    }

    if (!mounted) return;
    setState(() {
      _busy = false;
      _result = result;
      _phase = AppLocalizations.of(context).packInstallDone;
    });
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final progress = _totalBytes > 0
        ? (_doneBytes / _totalBytes).clamp(0.0, 1.0)
        : null;
    final inspection = _inspection;
    final result = _result;

    return AlertDialog(
      title: Text(
        widget.title ??
            (widget.localPath == null
                ? l10n.packInstallTitle
                : l10n.packImportTitle),
      ),
      content: SizedBox(
        width: 440,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (inspection != null) ...[
              Text(
                inspection.name,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: AppSpacing.xxs),
              Text(
                l10n.packInspectMeta(
                  inspection.gameVersion,
                  inspection.loader,
                  inspection.loaderVersion,
                ),
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
              const SizedBox(height: AppSpacing.xxs),
              Text(
                l10n.packInspectFiles(
                  inspection.fileCount,
                  inspection.overrideFiles,
                ),
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
              const SizedBox(height: AppSpacing.md),
            ] else if (_busy) ...[
              Text(
                l10n.packInstallHint,
                style: Theme.of(context).textTheme.bodyMedium
                    ?.copyWith(color: tokens.textSecondary),
              ),
              const SizedBox(height: AppSpacing.md),
            ],
            if (result != null) ...[
              Text(
                l10n.packResultSummary(
                  result.downloadedFiles,
                  result.extractedFiles,
                ),
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
              const SizedBox(height: AppSpacing.sm),
            ],
            Text(
              _error ??
                  (_result != null
                      ? l10n.packInstallDone
                      : (_phase ?? l10n.packInstallRunning)),
              maxLines: 3,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: _error == null ? tokens.textSecondary : tokens.danger,
              ),
            ),
            if (_busy) ...[
              const SizedBox(height: AppSpacing.md),
              AppProgressBar(value: progress),
              const SizedBox(height: AppSpacing.xs),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      progress == null
                          ? l10n.packInstallRunning
                          : '${_formatBytes(_doneBytes)} / ${_formatBytes(_totalBytes)}',
                      style: Theme.of(context).textTheme.bodySmall
                          ?.copyWith(color: tokens.textTertiary),
                    ),
                  ),
                  if (progress != null)
                    Text(
                      '${(progress * 100).round()}%',
                      style: Theme.of(context).textTheme.labelMedium
                          ?.copyWith(color: tokens.accent),
                    ),
                ],
              ),
            ],
          ],
        ),
      ),
      actions: [
        if (_busy)
          OutlinedButton(onPressed: _cancel, child: Text(l10n.commonCancel)),
        if (_error != null)
          TextButton(
            onPressed: _busy ? null : _run,
            child: Text(l10n.commonRetry),
          ),
        FilledButton(
          onPressed: _busy ? null : () => Navigator.of(context).pop(_result),
          child: Text(l10n.commonClose),
        ),
      ],
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
