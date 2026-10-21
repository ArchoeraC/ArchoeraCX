import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../services/core/core_client.dart';
import '../../services/core/java_scan.dart';
import '../../services/java/java_path_store.dart';
import '../../theme/tokens.dart';
import 'app_section.dart';

/// Signature of a Java scan call, matching [CoreClient.scanJava].
typedef JavaScanCallback = Future<JavaScanResult> Function({
  List<String> roots,
  List<String> executables,
  bool includeDefaults,
  void Function(int done, int total)? onProgress,
});

/// Opens a native folder picker and returns the selected absolute path.
typedef PickDirectory = Future<String?> Function();

/// Opens a native file picker and returns the selected Java executable path.
typedef PickExecutable = Future<String?> Function();

/// A small, zero-business card that lists Java runtimes found by `java.scan`.
///
/// The card owns its [CoreClient] for its lifetime and creates it lazily on the
/// first scan. A loader or scan callback can be injected for tests, which lets
/// the unavailable path render without a native library or business module.
///
/// Manual paths are persisted by [JavaPathStore] and included in every scan.
/// When a default scan finds nothing, the user can add a folder or point at a
/// `java` executable directly.
class JavaStatusCard extends StatefulWidget {
  const JavaStatusCard({
    super.key,
    this.clientFactory = CoreClient.openDefault,
    this.scan,
    this.pathStore = const JavaPathStore(),
    this.pickDirectory,
    this.pickExecutable,
    this.selectedPath,
    this.onSelect,
  });

  /// Opens the core client used by [CoreClient.scanJava].
  final CoreClient Function() clientFactory;

  /// Optional scan implementation; tests use it to avoid native code.
  final JavaScanCallback? scan;

  /// Persistence for user-supplied roots and executables.
  final JavaPathStore pathStore;

  /// Optional folder picker; defaults to `file_selector`.
  final PickDirectory? pickDirectory;

  /// Optional executable picker; defaults to `file_selector`.
  final PickExecutable? pickExecutable;

  /// Java path persisted by the settings service.
  final String? selectedPath;

  /// Called when the user chooses a runtime; null keeps the card read-only.
  final ValueChanged<JavaRuntime>? onSelect;

  @override
  State<JavaStatusCard> createState() => _JavaStatusCardState();
}

enum _JavaCardState { idle, scanning, ready, unavailable }

enum _ScanOrigin { manual, directory, executable }

class _JavaStatusCardState extends State<JavaStatusCard> {
  CoreClient? _client;
  _JavaCardState _state = _JavaCardState.idle;
  JavaScanResult? _result;
  String? _reason;
  String? _details;
  int _progressDone = 0;
  int _progressTotal = 0;

  late final JavaPathStore _store;
  late final Future<void> _pathsReady;
  List<String> _roots = const <String>[];
  List<String> _executables = const <String>[];

  /// True when the last scan was triggered by a picked folder that yielded no
  /// runtimes. The UI then asks for an executable as the stronger fallback.
  bool _directoryAttemptEmpty = false;

  /// Non-null when a picked executable produced no usable runtime or an error.
  String? _invalidExecutablePath;
  String? _invalidExecutableDetail;

  @override
  void initState() {
    super.initState();
    _store = widget.pathStore;
    _pathsReady = _loadSavedPaths();
  }

  @override
  void dispose() {
    _client?.close();
    _client = null;
    super.dispose();
  }

  Future<void> _loadSavedPaths() async {
    try {
      final roots = await _store.loadRoots();
      final executables = await _store.loadExecutables();
      if (!mounted) return;
      setState(() {
        _roots = _mergePaths(<String>[...roots, ..._roots]);
        _executables = _mergePaths(<String>[...executables, ..._executables]);
      });
    } catch (_) {
      // A failed preference read must never block scanning.
    }
  }

  Future<void> _runScan({
    _ScanOrigin origin = _ScanOrigin.manual,
    String? executablePath,
    bool includeDefaults = true,
  }) async {
    if (_state == _JavaCardState.scanning) return;

    // Make sure persisted paths participate in the first scan as well.
    await _pathsReady;
    if (!mounted) return;

    final roots = List<String>.unmodifiable(_roots);
    final executables = List<String>.unmodifiable(_executables);

    setState(() {
      _state = _JavaCardState.scanning;
      _reason = null;
      _details = null;
      _progressDone = 0;
      _progressTotal = 0;
    });

    JavaScanCallback runner;
    final override = widget.scan;
    if (override != null) {
      runner = override;
    } else {
      var client = _client;
      if (client == null || !client.isOpen) {
        try {
          client = widget.clientFactory();
          _client = client;
        } catch (error) {
          if (!mounted) return;
          setState(() => _setUnavailable(error));
          return;
        }
      }
      runner = client.scanJava;
    }

    try {
      final result = await runner(
        roots: roots,
        executables: executables,
        includeDefaults: includeDefaults,
        onProgress: (done, total) {
          if (!mounted) return;
          setState(() {
            _progressDone = done;
            _progressTotal = total;
          });
        },
      );
      if (!mounted) return;
      setState(() {
        _state = _JavaCardState.ready;
        _result = result;
        _reason = null;
        _details = null;
        _directoryAttemptEmpty =
            origin == _ScanOrigin.directory && result.isEmpty;
        _invalidExecutablePath =
            origin == _ScanOrigin.executable && result.isEmpty
            ? executablePath
            : null;
        _invalidExecutableDetail = null;
      });
    } catch (error) {
      if (!mounted) return;
      if (origin == _ScanOrigin.executable &&
          error is! CoreUnavailableException) {
        // The module is reachable; the chosen executable is simply unusable.
        setState(() {
          _state = _JavaCardState.ready;
          _reason = null;
          _details = null;
          _directoryAttemptEmpty = false;
          _invalidExecutablePath = executablePath;
          _invalidExecutableDetail = error is CoreException
              ? error.message
              : error.toString();
        });
      } else {
        setState(() => _setUnavailable(error));
      }
    }
  }

  Future<void> _searchFolder({bool verifyOnly = false}) async {
    if (_state == _JavaCardState.scanning) return;

    final injected = widget.pickDirectory;
    String? path;
    if (injected != null) {
      path = await injected();
    } else {
      final l10n = AppLocalizations.of(context);
      path = await getDirectoryPath(
        confirmButtonText: l10n.javaStatusSearchFolder,
      );
    }

    if (!mounted) return;
    final normalized = path?.trim();
    if (normalized == null || normalized.isEmpty) return;

    await _addRoot(normalized);
    if (!mounted) return;
    await _runScan(origin: _ScanOrigin.directory, includeDefaults: !verifyOnly);
  }

  Future<void> _selectExecutable() async {
    if (_state == _JavaCardState.scanning) return;

    final injected = widget.pickExecutable;
    String? path;
    if (injected != null) {
      path = await injected();
    } else {
      final l10n = AppLocalizations.of(context);
      final file = await openFile(
        acceptedTypeGroups: <XTypeGroup>[
          XTypeGroup(label: l10n.javaStatusSelectExecutable),
        ],
      );
      path = file?.path;
    }

    if (!mounted) return;
    final normalized = path?.trim();
    if (normalized == null || normalized.isEmpty) return;

    await _addExecutable(normalized);
    if (!mounted) return;
    await _runScan(
      origin: _ScanOrigin.executable,
      executablePath: normalized,
      includeDefaults: true,
    );
  }

  Future<void> _addRoot(String path) async {
    final updated = _mergePaths(<String>[..._roots, path]);
    setState(() => _roots = updated);
    try {
      await _store.saveRoots(updated);
    } catch (_) {
      // Persistence is best-effort; the in-memory path is still used.
    }
  }

  Future<void> _addExecutable(String path) async {
    final updated = _mergePaths(<String>[..._executables, path]);
    setState(() => _executables = updated);
    try {
      await _store.saveExecutables(updated);
    } catch (_) {
      // Persistence is best-effort; the in-memory path is still used.
    }
  }

  Future<void> _removeRoot(String path) async {
    final updated = _mergePaths(_roots.where((value) => value != path));
    setState(() => _roots = updated);
    try {
      await _store.saveRoots(updated);
    } catch (_) {
      // Ignore persistence failures; the chip is already gone in memory.
    }
  }

  Future<void> _removeExecutable(String path) async {
    final updated = _mergePaths(_executables.where((value) => value != path));
    setState(() => _executables = updated);
    try {
      await _store.saveExecutables(updated);
    } catch (_) {
      // Ignore persistence failures; the chip is already gone in memory.
    }
  }

  Future<void> _showAll(BuildContext context, JavaScanResult result) async {
    final l10n = AppLocalizations.of(context);

    await showDialog<void>(
      context: context,
      builder: (dialogContext) {
        return Dialog(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 720, maxHeight: 560),
            child: Padding(
              padding: const EdgeInsets.all(AppSpacing.lg),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    l10n.javaStatusAllTitle(result.count),
                    style: Theme.of(dialogContext).textTheme.titleLarge,
                  ),
                  const SizedBox(height: AppSpacing.md),
                  Flexible(
                    child: ListView.separated(
                      shrinkWrap: true,
                      itemCount: result.items.length,
                      separatorBuilder: (_, _) =>
                          const SizedBox(height: AppSpacing.xs),
                      itemBuilder: (context, index) {
                        return _JavaRuntimeTile(
                          runtime: result.items[index],
                          l10n: l10n,
                          selected:
                              result.items[index].path == widget.selectedPath,
                          onSelect: widget.onSelect == null
                              ? null
                              : (runtime) {
                                  Navigator.of(dialogContext).pop();
                                  widget.onSelect!(runtime);
                                },
                        );
                      },
                    ),
                  ),
                  const SizedBox(height: AppSpacing.sm),
                  Align(
                    alignment: Alignment.centerRight,
                    child: TextButton(
                      onPressed: () => Navigator.of(dialogContext).pop(),
                      child: Text(l10n.close),
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }

  void _setUnavailable(Object error) {
    _state = _JavaCardState.unavailable;
    _result = null;
    _directoryAttemptEmpty = false;
    _invalidExecutablePath = null;
    _invalidExecutableDetail = null;

    if (error is CoreUnavailableException) {
      _reason = error.message;
      _details = error.summary;
    } else if (error is CoreException) {
      _reason = error.message;
      _details = error.toString();
    } else {
      _reason = error.toString();
      _details = error.toString();
    }
  }

  static List<String> _mergePaths(Iterable<String> values) {
    final seen = <String>{};
    final result = <String>[];

    for (final value in values) {
      final path = value.trim();
      if (path.isEmpty || !seen.add(path)) continue;
      result.add(path);
    }

    return List<String>.unmodifiable(result);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final result = _result;
    final hasRuntimes = result != null && result.items.isNotEmpty;
    final showNoResults = result != null && result.isEmpty;
    final showInvalidExecutable = _invalidExecutablePath != null;
    final showPathActions =
        showNoResults || showInvalidExecutable || hasRuntimes;
    final hasCustomPaths = _roots.isNotEmpty || _executables.isNotEmpty;

    return AppSection(
      showTop: true,
      showBottom: false,
      padding: const EdgeInsets.symmetric(vertical: AppSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.coffee_rounded, size: 18, color: tokens.accent),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Text(l10n.javaStatusTitle, style: textTheme.titleMedium),
              ),
              _JavaStatusPill(state: _state, l10n: l10n),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          if (_state == _JavaCardState.idle)
            _MutedText(
              l10n.javaStatusIdleHint,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
          if (_state == _JavaCardState.scanning) ...[
            _MutedText(
              _progressTotal > 0
                  ? l10n.javaStatusProgress(_progressDone, _progressTotal)
                  : l10n.javaStatusScanningHint,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
            const SizedBox(height: AppSpacing.xs),
            ClipRRect(
              borderRadius: BorderRadius.circular(tokens.radiusS),
              child: LinearProgressIndicator(
                value: _progressTotal > 0
                    ? (_progressDone / _progressTotal).clamp(0.0, 1.0)
                    : null,
                minHeight: 4,
                backgroundColor: tokens.surface2,
                color: tokens.accent,
              ),
            ),
          ],
          if (_state == _JavaCardState.unavailable) ...[
            _MutedText(
              _reason != null
                  ? l10n.javaStatusScanFailed(_reason!)
                  : l10n.javaStatusUnavailableHint,
              maxLines: 2,
              style: textTheme.bodySmall?.copyWith(color: tokens.danger),
            ),
            if (_reason == null) ...[
              const SizedBox(height: AppSpacing.xxs),
              _MutedText(
                l10n.javaStatusUnavailableHint,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
            ],
          ],
          if (_state == _JavaCardState.ready) ...[
            if (result != null)
              _MutedText(
                l10n.javaStatusCount(result.count),
                style: textTheme.labelLarge?.copyWith(
                  color: result.isEmpty ? tokens.warning : tokens.success,
                ),
              ),
            if (showNoResults) ...[
              const SizedBox(height: AppSpacing.xxs),
              _MutedText(
                l10n.javaStatusNoResultsTitle,
                style: textTheme.labelLarge?.copyWith(color: tokens.warning),
              ),
              const SizedBox(height: AppSpacing.xxs),
              _MutedText(
                _directoryAttemptEmpty
                    ? l10n.javaStatusStillNotFound
                    : l10n.javaStatusNoResultsHint,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
            ],
            if (showInvalidExecutable) ...[
              const SizedBox(height: AppSpacing.xs),
              _MutedText(
                l10n.javaStatusNoValidExecutable(_invalidExecutablePath!),
                style: textTheme.bodySmall?.copyWith(color: tokens.danger),
              ),
              _MutedText(
                _invalidExecutableDetail ??
                    l10n.javaStatusNoValidExecutableHint,
                maxLines: 2,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
            ],
            if (hasRuntimes) ...[
              const SizedBox(height: AppSpacing.xs),
              for (final runtime in result.items.take(5))
                Padding(
                  padding: const EdgeInsets.only(bottom: AppSpacing.xxs),
                  child: _JavaRuntimeTile(
                    runtime: runtime,
                    l10n: l10n,
                    selected: runtime.path == widget.selectedPath,
                    onSelect: widget.onSelect,
                  ),
                ),
              if (result.items.length > 5)
                Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton(
                    onPressed: () => _showAll(context, result),
                    child: Text(l10n.javaStatusViewAll(result.count)),
                  ),
                ),
            ],
            if (showPathActions) ...[
              const SizedBox(height: AppSpacing.xs),
              Wrap(
                spacing: AppSpacing.xs,
                runSpacing: AppSpacing.xxs,
                children: [
                  OutlinedButton.icon(
                    onPressed: hasRuntimes
                        ? _searchFolder
                        : () => _searchFolder(verifyOnly: true),
                    icon: const Icon(Icons.folder_open_rounded, size: 18),
                    label: Text(
                      hasRuntimes
                          ? l10n.javaStatusAddFolder
                          : l10n.javaStatusSearchFolder,
                    ),
                  ),
                  if (!hasRuntimes)
                    OutlinedButton.icon(
                      onPressed: () => _searchFolder(verifyOnly: true),
                      icon: const Icon(Icons.home_work_outlined, size: 18),
                      label: Text(l10n.javaStatusSelectJdkHome),
                    ),
                  if (!hasRuntimes)
                    OutlinedButton.icon(
                      onPressed: _selectExecutable,
                      icon: const Icon(Icons.terminal_rounded, size: 18),
                      label: Text(l10n.javaStatusSelectExecutable),
                    ),
                ],
              ),
            ],
            if (hasCustomPaths) ...[
              const SizedBox(height: AppSpacing.sm),
              _MutedText(
                l10n.javaStatusCustomPaths,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
              const SizedBox(height: AppSpacing.xxs),
              Wrap(
                spacing: AppSpacing.xxs,
                runSpacing: AppSpacing.xxs,
                children: [
                  for (final path in _roots)
                    _PathChip(
                      path: path,
                      removeLabel: l10n.javaStatusRemovePath(path),
                      onDeleted: () => _removeRoot(path),
                    ),
                  for (final path in _executables)
                    _PathChip(
                      path: path,
                      removeLabel: l10n.javaStatusRemovePath(path),
                      onDeleted: () => _removeExecutable(path),
                    ),
                ],
              ),
            ],
          ],
          const SizedBox(height: AppSpacing.sm),
          Row(
            children: [
              FilledButton.icon(
                onPressed: _state == _JavaCardState.scanning
                    ? null
                    : () => _runScan(),
                icon: const Icon(Icons.manage_search_rounded, size: 18),
                label: Text(
                  _state == _JavaCardState.scanning
                      ? l10n.javaStatusScanning
                      : l10n.javaStatusScan,
                ),
              ),
              if (_details != null) ...[
                const SizedBox(width: AppSpacing.xs),
                Tooltip(
                  message: _details!,
                  child: Icon(
                    Icons.info_outline_rounded,
                    size: 18,
                    color: tokens.textTertiary,
                  ),
                ),
              ],
            ],
          ),
        ],
      ),
    );
  }
}

class _PathChip extends StatelessWidget {
  const _PathChip({
    required this.path,
    required this.removeLabel,
    required this.onDeleted,
  });

  final String path;
  final String removeLabel;
  final VoidCallback onDeleted;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    return InputChip(
      label: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 260),
        child: Text(
          path,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: textTheme.labelSmall,
        ),
      ),
      tooltip: path,
      deleteIcon: const Icon(Icons.close_rounded, size: 14),
      deleteButtonTooltipMessage: removeLabel,
      onDeleted: onDeleted,
      backgroundColor: tokens.surface2,
      side: BorderSide(color: tokens.border),
      visualDensity: VisualDensity.compact,
    );
  }
}

class _JavaRuntimeTile extends StatelessWidget {
  const _JavaRuntimeTile({
    required this.runtime,
    required this.l10n,
    this.selected = false,
    this.onSelect,
  });

  final JavaRuntime runtime;
  final AppLocalizations l10n;
  final bool selected;
  final ValueChanged<JavaRuntime>? onSelect;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;

    final content = Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Tooltip(
                  message: runtime.path,
                  child: Text(
                    runtime.home ?? runtime.path,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: textTheme.labelLarge?.copyWith(
                      color: selected ? tokens.accent : null,
                    ),
                  ),
                ),
                const SizedBox(height: 1),
                Text(
                  l10n.javaStatusMeta(
                    runtime.displayVersion,
                    runtime.arch ?? '—',
                  ),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodySmall?.copyWith(
                    color: tokens.textSecondary,
                  ),
                ),
              ],
            ),
          ),
          if (selected)
            Icon(Icons.check_rounded, size: 18, color: tokens.accent)
          else if (onSelect != null)
            Icon(
              Icons.chevron_right_rounded,
              size: 18,
              color: tokens.textTertiary,
            ),
        ],
      ),
    );

    if (onSelect == null) return content;
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () => onSelect!(runtime),
        hoverColor: tokens.surface2,
        child: content,
      ),
    );
  }
}

class _JavaStatusPill extends StatelessWidget {
  const _JavaStatusPill({required this.state, required this.l10n});

  final _JavaCardState state;
  final AppLocalizations l10n;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final (color, label) = switch (state) {
      _JavaCardState.idle => (tokens.textTertiary, l10n.javaStatusIdle),
      _JavaCardState.scanning => (tokens.warning, l10n.javaStatusScanning),
      _JavaCardState.ready => (tokens.success, l10n.javaStatusReady),
      _JavaCardState.unavailable => (tokens.danger, l10n.javaStatusUnavailable),
    };

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.xs,
        vertical: AppSpacing.xxs,
      ),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.14),
        borderRadius: BorderRadius.circular(tokens.radiusS),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 6,
            height: 6,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: AppSpacing.xxs),
          Text(
            label,
            style: Theme.of(context).textTheme.labelMedium
                ?.copyWith(color: color),
          ),
        ],
      ),
    );
  }
}

class _MutedText extends StatelessWidget {
  const _MutedText(this.text, {required this.style, this.maxLines});

  final String text;
  final TextStyle? style;
  final int? maxLines;

  @override
  Widget build(BuildContext context) {
    return Text(
      text,
      maxLines: maxLines,
      overflow: maxLines == null ? null : TextOverflow.ellipsis,
      style: style,
    );
  }
}
