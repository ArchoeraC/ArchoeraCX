import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../models/game_instance.dart';
import '../../stores/instances.dart';
import '../../theme/tokens.dart';
import '../common/app_progress_bar.dart';

Future<void> showVanillaInstallDialog(
  BuildContext context,
  GameInstance instance,
) {
  return showDialog<void>(
    context: context,
    barrierDismissible: false,
    builder: (_) => VanillaInstallDialog(instance: instance),
  );
}

class VanillaInstallDialog extends ConsumerStatefulWidget {
  const VanillaInstallDialog({super.key, required this.instance});

  final GameInstance instance;

  @override
  ConsumerState<VanillaInstallDialog> createState() =>
      _VanillaInstallDialogState();
}

class _VanillaInstallDialogState extends ConsumerState<VanillaInstallDialog> {
  bool _running = false;
  bool _done = false;
  int _doneBytes = 0;
  int _totalBytes = 0;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _install();
    });
  }

  Future<void> _install() async {
    if (_running) return;
    setState(() {
      _running = true;
      _done = false;
      _error = null;
      _doneBytes = 0;
      _totalBytes = 0;
    });
    try {
      await ref
          .read(instancesProvider.notifier)
          .installVanilla(
            widget.instance,
            onProgress: (done, total) {
              if (!mounted) return;
              setState(() {
                _doneBytes = done;
                _totalBytes = total;
              });
            },
          );
      if (mounted) {
        setState(() {
          _running = false;
          _done = true;
        });
      }
    } catch (error) {
      if (mounted) {
        setState(() {
          _running = false;
          _error = error.toString();
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final progress = _totalBytes > 0
        ? (_doneBytes / _totalBytes).clamp(0.0, 1.0)
        : null;

    return AlertDialog(
      title: Text(l10n.instanceInstallGameTitle),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              '${widget.instance.name} · Minecraft ${widget.instance.gameVersion}',
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(color: tokens.textTertiary),
            ),
            const SizedBox(height: AppSpacing.md),
            Text(
              _error ??
                  (_done
                      ? l10n.instanceInstallGameDone
                      : l10n.instanceInstallGameHint),
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                color: _error == null ? tokens.textSecondary : tokens.danger,
              ),
            ),
            if (_running) ...[
              const SizedBox(height: AppSpacing.md),
              AppProgressBar(value: progress),
              const SizedBox(height: AppSpacing.xs),
              Row(
                children: [
                  Expanded(
                    child: Text(
                      progress == null
                          ? l10n.instanceInstallGameRunning
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
        if (_error != null)
          TextButton(
            onPressed: _running ? null : _install,
            child: Text(l10n.commonRetry),
          ),
        FilledButton(
          onPressed: _running ? null : () => Navigator.of(context).pop(),
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
