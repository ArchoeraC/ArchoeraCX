import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../services/core/launch/launch_log.dart';
import '../../stores/preferences.dart';
import '../../stores/providers.dart';
import '../../theme/icons/app_icons.dart';
import '../../theme/tokens.dart';
import '../../utils/format.dart';
import '../common/toast.dart';

part 'instances/log_panels.dart';

/// Opens the game log viewer for [instance].
///
/// The viewer reads through the business module (`launch.logs` /
/// `launch.log.read` / `launch.log.tail`); the UI never touches the log file.
/// While the game is running the dialog follows the log live, because the C#
/// module pushes appended lines through the core event pump.
Future<void> showGameLogDialog(BuildContext context, GameInstance instance) {
  return showDialog<void>(
    context: context,
    builder: (_) => GameLogDialog(instance: instance),
  );
}

/// Asks twice before force-terminating a running game.
///
/// The first dialog states the consequence (unsaved world progress can be
/// lost); the second is the explicit final confirmation. Returns true only
/// when both steps are accepted, so a stray click can never kill a game.
Future<bool> confirmForceStopGame(
  BuildContext context, {
  required String instanceName,
  int? pid,
}) async {
  final l10n = AppLocalizations.of(context);
  final tokens = context.tokens;

  final first = await showDialog<bool>(
    context: context,
    builder: (dialogContext) => AlertDialog(
      icon: Icon(AppIcons.cancelled, color: tokens.danger),
      title: Text(l10n.gameLogForceStopTitle),
      content: Text(l10n.gameLogForceStopBody(instanceName)),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(dialogContext).pop(false),
          child: Text(l10n.commonCancel),
        ),
        TextButton(
          onPressed: () => Navigator.of(dialogContext).pop(true),
          child: Text(l10n.gameLogForceStopContinue),
        ),
      ],
    ),
  );
  if (first != true || !context.mounted) return false;

  final second = await showDialog<bool>(
    context: context,
    builder: (dialogContext) => AlertDialog(
      icon: Icon(AppIcons.warning, color: tokens.danger),
      title: Text(l10n.gameLogForceStopAgainTitle),
      content: Text(
        pid == null
            ? l10n.gameLogForceStopAgainBody
            : l10n.gameLogForceStopAgainBodyPid(pid),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(dialogContext).pop(false),
          child: Text(l10n.commonCancel),
        ),
        FilledButton(
          style: FilledButton.styleFrom(
            backgroundColor: tokens.danger,
            foregroundColor: tokens.onAccent,
          ),
          onPressed: () => Navigator.of(dialogContext).pop(true),
          child: Text(l10n.gameLogForceStopConfirm),
        ),
      ],
    ),
  );
  return second == true;
}

class GameLogDialog extends ConsumerStatefulWidget {
  const GameLogDialog({super.key, required this.instance});

  final GameInstance instance;

  @override
  ConsumerState<GameLogDialog> createState() => _GameLogDialogState();
}

class _GameLogDialogState extends ConsumerState<GameLogDialog> {
  /// Older lines are dropped so a long session cannot grow without bound.
  static const int _maxLines = 5000;

  final ScrollController _scroll = ScrollController();
  LaunchLogList? _list;
  LaunchLogSnapshot? _snapshot;
  List<String> _lines = const [];
  String? _selected;
  String? _error;
  bool _loading = true;
  bool _following = false;
  bool _stopping = false;
  bool _atBottom = true;
  LaunchLogTail? _tail;

  @override
  void initState() {
    super.initState();
    _scroll.addListener(_onScroll);
    // Localizations and providers are only safe after the first frame; the
    // first load also starts the live follow when the game is running.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _load();
    });
  }

  @override
  void dispose() {
    _scroll.removeListener(_onScroll);
    _scroll.dispose();
    _stopFollowing();
    super.dispose();
  }

  void _onScroll() {
    if (!_scroll.hasClients) return;
    final position = _scroll.position;
    final atBottom = position.pixels >= position.maxScrollExtent - 24;
    if (atBottom != _atBottom) setState(() => _atBottom = atBottom);
  }

  Future<void> _load() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(() {
        _loading = false;
        _error = AppLocalizations.of(context).commonCoreNotConnected;
      });
      return;
    }

    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final list = await client.listLaunchLogs(widget.instance.id);
      if (!mounted) return;
      final selected = _selected ?? list.activeLog ?? _firstFileName(list);
      final snapshot = await client.readLaunchLog(
        instanceId: widget.instance.id,
        fileName: selected,
        lines: ref.read(preferencesProvider).logLineLimit,
      );
      if (!mounted) return;
      setState(() {
        _list = list;
        _snapshot = snapshot;
        _selected = snapshot.fileName ?? selected;
        _lines = snapshot.lines;
        _loading = false;
      });
      _jumpToBottom();
      if (list.running) await _startFollowing();
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = AppLocalizations.of(context).gameLogLoadFailed;
      });
    }
  }

  Future<void> _select(String? fileName) async {
    if (fileName == null || fileName == _selected) return;
    await _stopFollowing();
    setState(() => _selected = fileName);
    await _load();
  }

  Future<void> _startFollowing() async {
    if (_following) return;
    final client = ref.read(coreClientProvider);
    if (client == null) return;

    try {
      final tail = await client.tailLaunchLog(
        instanceId: widget.instance.id,
        fileName: _selected,
      );
      if (!mounted) {
        tail.stop();
        return;
      }

      final subscription = tail.chunks.listen(_append, onError: (_) {});
      setState(() => _following = true);
      unawaited(
        tail.done.whenComplete(() {
          unawaited(subscription.cancel());
          if (!mounted) return;
          setState(() => _following = false);
          // The game may have exited; refresh the running badge and file list.
          unawaited(_refreshStatus());
        }),
      );
      _tail = tail;
    } catch (_) {
      if (mounted) setState(() => _following = false);
    }
  }

  /// Refresh only the log list so the running badge and picker stay accurate.
  Future<void> _refreshStatus() async {
    final client = ref.read(coreClientProvider);
    if (client == null || !mounted) return;
    try {
      final list = await client.listLaunchLogs(widget.instance.id);
      if (!mounted) return;
      setState(() => _list = list);
    } catch (_) {
      // A stale badge is harmless; the next manual refresh fixes it.
    }
  }

  Future<void> _stopFollowing() async {
    final tail = _tail;
    _tail = null;
    if (tail == null) return;
    if (mounted && _following) setState(() => _following = false);
    tail.stop();
    await tail.done.catchError((_) {});
  }

  void _append(String chunk) {
    if (!mounted) return;
    final incoming = chunk.split('\n').where((line) => line.isNotEmpty);
    if (incoming.isEmpty) return;
    setState(() {
      final merged = [..._lines, ...incoming];
      _lines = merged.length > _maxLines
          ? merged.sublist(merged.length - _maxLines)
          : merged;
    });
    _jumpToBottom();
  }

  void _jumpToBottom() {
    if (!_atBottom) return;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || !_scroll.hasClients) return;
      _scroll.jumpTo(_scroll.position.maxScrollExtent);
    });
  }

  Future<void> _copyAll() async {
    if (_lines.isEmpty) return;
    await Clipboard.setData(ClipboardData(text: _lines.join('\n')));
    if (!mounted) return;
    toast(AppLocalizations.of(context).gameLogCopied, type: ToastType.success);
  }

  static String? _firstFileName(LaunchLogList list) =>
      list.logs.isEmpty ? null : list.logs.first.fileName;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final list = _list;
    final running = list?.running ?? false;
    final screen = MediaQuery.sizeOf(context);

    return Dialog(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxWidth: 900,
          maxHeight: screen.height * 0.84,
          minWidth: 520,
        ),
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.lg),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _Header(
                title: l10n.gameLogTitle,
                subtitle:
                    '${widget.instance.name} · '
                    '${widget.instance.gameVersion} · '
                    '${widget.instance.loader.labelFor(l10n)}',
                running: running,
                forceStopping: _stopping,
                onForceStop: _forceStop,
                onOpenFolder: list == null ? null : _openLogFolder,
                onRefresh: _loading ? null : _load,
                onClose: () => Navigator.of(context).pop(),
              ),
              const SizedBox(height: AppSpacing.sm),
              Divider(height: 1, color: tokens.border),
              const SizedBox(height: AppSpacing.sm),
              Row(
                children: [
                  Expanded(
                    child: _FilePicker(
                      list: list,
                      selected: _selected,
                      l10n: l10n,
                      onChanged: _select,
                    ),
                  ),
                  const SizedBox(width: AppSpacing.sm),
                  if (running)
                    _FollowToggle(
                      following: _following,
                      l10n: l10n,
                      onChanged: (value) {
                        if (value) {
                          _startFollowing();
                        } else {
                          _stopFollowing();
                        }
                      },
                    ),
                  IconButton(
                    tooltip: l10n.gameLogClear,
                    onPressed: list == null || list.logs.isEmpty
                        ? null
                        : _clearOldLogs,
                    icon: const Icon(AppIcons.delete, size: 20),
                  ),
                  IconButton(
                    tooltip: l10n.gameLogCopy,
                    onPressed: _lines.isEmpty ? null : _copyAll,
                    icon: const Icon(AppIcons.copy, size: 20),
                  ),
                ],
              ),
              if (list != null &&
                  list.logs.isNotEmpty &&
                  list.retentionMaxFiles > 0) ...[
                const SizedBox(height: AppSpacing.xxs),
                Text(
                  l10n.gameLogRetentionInfo(
                    formatBytes(list.totalSize),
                    list.retentionMaxFiles,
                  ),
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ],
              if (_snapshot?.truncated == true) ...[
                const SizedBox(height: AppSpacing.xxs),
                Text(
                  l10n.gameLogTruncated(_lines.length),
                  style: Theme.of(context).textTheme.labelSmall
                      ?.copyWith(color: tokens.textTertiary),
                ),
              ],
              const SizedBox(height: AppSpacing.xs),
              Expanded(child: _body(context, l10n, tokens)),
              if (_error != null) ...[
                const SizedBox(height: AppSpacing.xs),
                Text(
                  _error!,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(color: tokens.danger),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  /// Reveals the instance log directory in the OS file manager.
  Future<void> _openLogFolder() async {
    final directory = _list?.logDirectory;
    if (directory == null || directory.isEmpty) return;
    final client = ref.read(coreClientProvider);
    final opened = client != null && await client.openSystemPath(directory);
    if (!opened && mounted) {
      toast(
        AppLocalizations.of(context).gameLogOpenFolderFailed,
        type: ToastType.error,
      );
    }
  }

  /// Deletes archived launch logs of this instance (the running one is kept).
  Future<void> _clearOldLogs() async {
    final l10n = AppLocalizations.of(context);
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(l10n.gameLogClearTitle),
        content: Text(l10n.gameLogClearBody),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: Text(l10n.commonCancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: Text(l10n.commonDelete),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    try {
      final result = await client.clearLaunchLogs(
        instanceId: widget.instance.id,
      );
      if (!mounted) return;
      toast(
        l10n.gameLogClearDone(
          result.deletedFiles,
          formatBytes(result.freedBytes),
        ),
        type: ToastType.success,
      );
      await _load();
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    }
  }

  /// Force-terminates the running game after two explicit warnings.
  ///
  /// The C# module resolves the pid from the launch it started, so this can
  /// only ever stop this launcher's game. When the game exited between the
  /// confirmation and the call, the result is a normal "already stopped" toast
  /// instead of an error, and the status is refreshed either way.
  Future<void> _forceStop() async {
    if (_stopping) return;
    final l10n = AppLocalizations.of(context);
    final client = ref.read(coreClientProvider);
    if (client == null) return;

    final confirmed = await confirmForceStopGame(
      context,
      instanceName: widget.instance.name,
      pid: _list?.pid,
    );
    if (!confirmed || !mounted) return;

    setState(() => _stopping = true);
    try {
      final result = await client.stopLaunch(widget.instance.id);
      if (!mounted) return;
      await _stopFollowing();
      if (!mounted) return;
      toast(
        result.stopped ? l10n.gameLogForceStopped : l10n.gameLogAlreadyStopped,
        type: result.stopped ? ToastType.success : ToastType.info,
      );
      await _load();
    } catch (error) {
      if (!mounted) return;
      toast('$error', type: ToastType.error);
    } finally {
      if (mounted) setState(() => _stopping = false);
    }
  }

  Widget _body(BuildContext context, AppLocalizations l10n, AppTokens tokens) {
    if (_loading && _lines.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    if (_lines.isEmpty) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(AppSpacing.xl),
          child: Text(
            _list == null || _list!.logs.isEmpty
                ? l10n.gameLogEmpty
                : l10n.gameLogNoOutput,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodyMedium
                ?.copyWith(color: tokens.textSecondary),
          ),
        ),
      );
    }

    return DecoratedBox(
      decoration: BoxDecoration(
        color: tokens.bg,
        border: Border.all(color: tokens.border),
      ),
      child: Scrollbar(
        controller: _scroll,
        child: ListView.builder(
          controller: _scroll,
          padding: const EdgeInsets.symmetric(
            horizontal: AppSpacing.sm,
            vertical: AppSpacing.xs,
          ),
          itemCount: _lines.length,
          itemBuilder: (context, index) =>
              _LogLine(text: _lines[index], tokens: tokens),
        ),
      ),
    );
  }
}
