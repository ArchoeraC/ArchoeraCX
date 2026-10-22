import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../l10n/l10n_labels.dart';
import '../../models/game_instance.dart';
import '../../services/core/launch_log.dart';
import '../../stores/providers.dart';
import '../../theme/tokens.dart';
import '../common/toast.dart';

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
                    tooltip: l10n.gameLogCopy,
                    onPressed: _lines.isEmpty ? null : _copyAll,
                    icon: const Icon(Icons.copy_all_rounded, size: 20),
                  ),
                ],
              ),
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

class _Header extends StatelessWidget {
  const _Header({
    required this.title,
    required this.subtitle,
    required this.running,
    required this.onRefresh,
    required this.onClose,
  });

  final String title;
  final String subtitle;
  final bool running;
  final VoidCallback? onRefresh;
  final VoidCallback onClose;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    return Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(title, style: Theme.of(context).textTheme.titleLarge),
                  const SizedBox(width: AppSpacing.xs),
                  _StatusChip(running: running),
                ],
              ),
              const SizedBox(height: 2),
              Text(
                subtitle,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(color: tokens.textTertiary),
              ),
            ],
          ),
        ),
        IconButton(
          onPressed: onRefresh,
          tooltip: l10n.refresh,
          icon: const Icon(Icons.refresh_rounded),
        ),
        IconButton(
          onPressed: onClose,
          tooltip: l10n.commonClose,
          icon: const Icon(Icons.close_rounded),
        ),
      ],
    );
  }
}

class _StatusChip extends StatelessWidget {
  const _StatusChip({required this.running});

  final bool running;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final color = running ? tokens.success : tokens.textTertiary;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: ShapeDecoration(
        color: color.withValues(alpha: 0.12),
        shape: BeveledRectangleBorder(
          side: BorderSide(color: color.withValues(alpha: 0.4)),
          borderRadius: BorderRadius.circular(tokens.radiusS),
        ),
      ),
      child: Text(
        running ? l10n.gameLogRunning : l10n.gameLogStopped,
        style: Theme.of(context).textTheme.labelSmall
            ?.copyWith(color: color, fontWeight: FontWeight.w600),
      ),
    );
  }
}

class _FilePicker extends StatelessWidget {
  const _FilePicker({
    required this.list,
    required this.selected,
    required this.l10n,
    required this.onChanged,
  });

  final LaunchLogList? list;
  final String? selected;
  final AppLocalizations l10n;
  final ValueChanged<String?> onChanged;

  @override
  Widget build(BuildContext context) {
    final logs = list?.logs ?? const <LaunchLogFile>[];
    final value = logs.any((file) => file.fileName == selected)
        ? selected
        : null;

    return InputDecorator(
      decoration: const InputDecoration(
        isDense: true,
        border: OutlineInputBorder(),
        contentPadding: EdgeInsets.symmetric(
          horizontal: AppSpacing.sm,
          vertical: AppSpacing.xs,
        ),
      ),
      child: DropdownButtonHideUnderline(
        child: DropdownButton<String>(
          value: value,
          isExpanded: true,
          isDense: true,
          hint: Text(l10n.gameLogFileLabel),
          items: [
            for (final file in logs)
              DropdownMenuItem(
                value: file.fileName,
                child: Text(
                  '${file.fileName}  ·  ${_formatBytes(file.size)}',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
          ],
          onChanged: logs.isEmpty ? null : onChanged,
        ),
      ),
    );
  }
}

class _FollowToggle extends StatelessWidget {
  const _FollowToggle({
    required this.following,
    required this.l10n,
    required this.onChanged,
  });

  final bool following;
  final AppLocalizations l10n;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    return Tooltip(
      message: l10n.gameLogFollowHint,
      child: FilterChip(
        selected: following,
        showCheckmark: false,
        avatar: Icon(
          following ? Icons.pause_rounded : Icons.play_arrow_rounded,
          size: 16,
          color: following ? tokens.accent : tokens.textSecondary,
        ),
        label: Text(l10n.gameLogFollow),
        onSelected: onChanged,
      ),
    );
  }
}

class _LogLine extends StatelessWidget {
  const _LogLine({required this.text, required this.tokens});

  final String text;
  final AppTokens tokens;

  @override
  Widget build(BuildContext context) {
    return Text(
      text.isEmpty ? ' ' : text,
      style: TextStyle(
        fontFamily: 'monospace',
        fontFamilyFallback: const ['NotoSansSC', 'DejaVu Sans Mono'],
        fontSize: 12,
        height: 1.35,
        color: _color,
      ),
    );
  }

  Color get _color {
    final line = text.toUpperCase();
    if (line.contains('ERROR') ||
        line.contains('FATAL') ||
        line.contains('SEVERE') ||
        line.contains('EXCEPTION')) {
      return tokens.danger;
    }
    if (line.contains('WARN')) return tokens.warning;
    if (line.startsWith('AT ') || line.startsWith('\tat ')) {
      return tokens.textTertiary;
    }
    return tokens.textSecondary;
  }
}

String _formatBytes(int bytes) {
  if (bytes >= 1024 * 1024) {
    return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
  }
  if (bytes >= 1024) return '${(bytes / 1024).toStringAsFixed(0)} KB';
  return '$bytes B';
}
