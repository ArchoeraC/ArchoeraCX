part of '../instances_page.dart';

/// Typed view over the loader-check envelope.
///
/// Replaces the dialog's former hand-rolled nested `is Map` unwrapping;
/// field-by-field it degrades exactly like that code did — a missing or
/// wrongly typed `data`/`verification`/`launchFiles` falls back to the same
/// empty map (counts 0, launch ok, no error), and only an envelope that is not
/// a map at all fails.
class _LoaderCheckEnvelope {
  const _LoaderCheckEnvelope({
    required this.ok,
    required this.verificationOk,
    required this.missing,
    required this.mismatched,
    required this.launchOk,
    required this.launchIssue,
    required this.hasError,
    required this.errorCode,
    required this.errorMessage,
  });

  final bool ok;
  final bool verificationOk;
  final int missing;
  final int mismatched;
  final bool launchOk;
  final String? launchIssue;
  final bool hasError;
  final String? errorCode;
  final String? errorMessage;

  /// Degraded stand-in for an envelope that is not a map at all.
  static const empty = _LoaderCheckEnvelope(
    ok: false,
    verificationOk: false,
    missing: 0,
    mismatched: 0,
    launchOk: true,
    launchIssue: null,
    hasError: false,
    errorCode: null,
    errorMessage: null,
  );

  bool get isHealthy => ok && verificationOk && launchOk;

  static Map<Object?, Object?> _asMap(Object? value) => value is Map
      ? value as Map<Object?, Object?>
      : const <Object?, Object?>{};

  static _LoaderCheckEnvelope? tryParse(Object? envelope) {
    if (envelope is! Map) return null;
    final map = envelope as Map<Object?, Object?>;
    final data = _asMap(map['data']);
    final verification = _asMap(data['verification']);
    final launchFiles = _asMap(data['launchFiles']);
    final error = map['error'] is Map
        ? map['error'] as Map<Object?, Object?>
        : null;
    return _LoaderCheckEnvelope(
      ok: map['ok'] == true,
      verificationOk: verification['ok'] == true,
      missing: verification['missing'] is List
          ? (verification['missing'] as List).length
          : 0,
      mismatched: verification['mismatched'] is List
          ? (verification['mismatched'] as List).length
          : 0,
      launchOk: launchFiles['ok'] != false,
      launchIssue:
          launchFiles['message']?.toString() ?? launchFiles['code']?.toString(),
      hasError: error != null,
      errorCode: error?['code']?.toString(),
      errorMessage: error?['message']?.toString(),
    );
  }
}

class _DependencyCheckDialog extends ConsumerStatefulWidget {
  const _DependencyCheckDialog({
    required this.l10n,
    required this.instance,
    required this.mode,
    required this.envelope,
  });

  final AppLocalizations l10n;
  final GameInstance instance;
  final String mode;
  final Map<String, Object?> envelope;

  @override
  ConsumerState<_DependencyCheckDialog> createState() =>
      _DependencyCheckDialogState();
}

class _DependencyCheckDialogState
    extends ConsumerState<_DependencyCheckDialog> {
  late Map<String, Object?> _envelope;
  bool _repaired = false;
  bool _queued = false;
  bool _checking = false;
  String? _error;

  /// Highest task id seen before the repair was queued. The repair task has a
  /// larger id, which lets this dialog recognise it without relying on stream
  /// delivery order (the task center also keeps old repairs with the same
  /// label).
  int _sinceTaskId = 0;

  @override
  void initState() {
    super.initState();
    _envelope = widget.envelope;
  }

  /// Queues the repair instead of running it here.
  ///
  /// The dialog stays open and read-only: progress, cancellation and history
  /// belong to the task center, and [_onTaskUpdate] re-runs the check once the
  /// native task finishes so the verdict refreshes on its own.
  Future<void> _repair() async {
    final l10n = widget.l10n;
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(() => _error = l10n.commonCoreNotConnected);
      return;
    }

    _sinceTaskId = ref
        .read(taskCenterProvider)
        .fold<int>(0, (max, task) => task.taskId > max ? task.taskId : max);
    setState(() {
      _error = null;
      _queued = true;
    });

    try {
      await ref
          .read(downloadsProvider.notifier)
          .repairInstance(
            widget.instance,
            successMessage: l10n.instanceCheckRepairDone,
            failureMessage: l10n.instanceCheckRepairFailed,
            cancelledMessage: l10n.downloadCancelled(widget.instance.name),
            describeError: (error) => switch (error) {
              CoreException(code: 'JAVA_MISSING' || 'JAVA_VERSION_MISMATCH') =>
                l10n.launchJavaMissing,
              CoreException(code: 'DOWNLOAD_DISABLED') =>
                l10n.downloadUnavailableTitle,
              _ => null,
            },
          );
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _queued = false;
        _error = '${l10n.instanceCheckRepairFailed} $error';
      });
      return;
    }

    if (!mounted) return;
    toast(l10n.instanceCheckRepairQueued, type: ToastType.success);
    // A tiny repair can reach a terminal state before the next build, so check
    // the current snapshot as well as listening for future updates.
    _onTaskUpdate(ref.read(taskCenterProvider));
  }

  /// Refreshes the verdict once the background repair lands.
  void _onTaskUpdate(List<TaskUpdate> tasks) {
    if (!_queued || _checking) return;
    for (final task in tasks) {
      if (task.taskId <= _sinceTaskId) continue;
      if (task.method != 'loader.autoRepair') continue;
      if (!task.isTerminal) continue;
      if (task.label != null && task.label != widget.instance.name) continue;

      _sinceTaskId = task.taskId;
      if (task.state == TaskUpdateState.completed) {
        unawaited(_recheck());
      } else if (mounted) {
        setState(() => _queued = false);
      }
      return;
    }
  }

  /// Re-runs the read-only verification so the verdict reflects the repair.
  Future<void> _recheck() async {
    final l10n = widget.l10n;
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(() => _error = l10n.commonCoreNotConnected);
      return;
    }
    setState(() {
      _checking = true;
      _error = null;
    });
    try {
      final envelope = await client.checkLoader(
        root: widget.instance.rootPath,
        kind: widget.instance.loader.name,
        gameVersion: widget.instance.gameVersion,
        loaderVersion: widget.instance.loaderVersion,
        mode: widget.mode,
      );
      if (!mounted) return;
      setState(() {
        _envelope = envelope;
        _checking = false;
        _queued = false;
        _repaired = true;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _checking = false;
        _queued = false;
        _error = '${l10n.instanceCheckFailed} $error';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = widget.l10n;
    final tokens = context.tokens;
    final check =
        _LoaderCheckEnvelope.tryParse(_envelope) ?? _LoaderCheckEnvelope.empty;
    final healthy = check.isHealthy;
    final errorMessage = check.errorCode == 'MANIFEST_MISSING'
        ? l10n.instanceManifestMissing
        : check.errorMessage ?? l10n.instanceCheckFailed;
    final downloadsEnabled = ref.watch(downloadEnabledProvider);
    ref.listen(taskCenterProvider, (_, tasks) => _onTaskUpdate(tasks));

    return AlertDialog(
      title: Text(l10n.instanceCheckTitle),
      content: SizedBox(
        width: 420,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              widget.mode == 'full'
                  ? l10n.instanceCheckFull
                  : l10n.instanceCheckQuick,
              style: Theme.of(context).textTheme.labelLarge
                  ?.copyWith(color: tokens.accent),
            ),
            const SizedBox(height: AppSpacing.sm),
            if (healthy)
              Text(
                _repaired ? l10n.instanceCheckRepairDone : l10n.instanceCheckOk,
                style: TextStyle(color: tokens.success),
              )
            else if (check.hasError)
              Text(errorMessage, style: TextStyle(color: tokens.danger))
            else ...[
              Text(
                l10n.instanceCheckFailed,
                style: TextStyle(color: tokens.danger),
              ),
              if (check.missing > 0)
                Text(l10n.instanceCheckMissing(check.missing)),
              if (check.mismatched > 0)
                Text(l10n.instanceCheckMismatched(check.mismatched)),
              if (!check.launchOk && check.launchIssue != null)
                Text(l10n.instanceCheckLaunchFiles(check.launchIssue!)),
            ],
            if (!healthy) ...[
              const SizedBox(height: AppSpacing.sm),
              Text(
                l10n.instanceCheckRepairHint,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
            if (_queued || _checking) ...[
              const SizedBox(height: AppSpacing.md),
              Text(
                _checking
                    ? l10n.instanceCheckRechecking
                    : l10n.instanceCheckRepairQueued,
                maxLines: 3,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: AppSpacing.sm),
              Text(
                _error!,
                maxLines: 4,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(color: tokens.danger),
              ),
            ],
          ],
        ),
      ),
      actions: [
        if (_repaired)
          OutlinedButton.icon(
            onPressed: _checking ? null : _recheck,
            icon: const Icon(AppIcons.refresh, size: 18),
            label: Text(l10n.instanceCheckRecheck),
          ),
        if (!healthy && downloadsEnabled && !_queued)
          FilledButton.icon(
            onPressed: _checking ? null : _repair,
            icon: const Icon(AppIcons.repair, size: 18),
            label: Text(l10n.instanceCheckRepair),
          ),
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: Text(l10n.commonClose),
        ),
      ],
    );
  }
}
