import 'dart:async';

import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/core_client.dart';
import '../services/core/java/java_scan.dart';
import '../services/core/java/java_path_store.dart';
import '../stores/downloads.dart';
import '../stores/providers.dart';
import '../stores/settings.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_progress_bar.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_panel.dart';
import '../widgets/common/toast.dart';

part 'java/panels.dart';

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

/// Dedicated Java page under Settings.
///
/// The candidates used to be truncated to five entries behind a "view all"
/// dialog. A page keeps the whole list flat and leaves the settings column for
/// options. The persisted choice still goes through `settings.set` (C# owns
/// it); this page only renders scan results and asks the settings store for the
/// selection.
class JavaPage extends ConsumerStatefulWidget {
  const JavaPage({
    super.key,
    this.scan,
    this.pathStore = const JavaPathStore(),
    this.pickDirectory,
    this.pickExecutable,
  });

  /// Optional scan implementation; tests use it to avoid native code.
  ///
  /// Production reads the shared client from `coreClientProvider` instead, so
  /// the launcher keeps a single native core.
  final JavaScanCallback? scan;

  /// Persistence for user-supplied roots and executables.
  final JavaPathStore pathStore;

  /// Optional folder picker; defaults to `file_selector`.
  final PickDirectory? pickDirectory;

  /// Optional executable picker; defaults to `file_selector`.
  final PickExecutable? pickExecutable;

  @override
  ConsumerState<JavaPage> createState() => _JavaPageState();
}

enum _JavaState { idle, scanning, ready, unavailable }

enum _ScanOrigin { manual, directory, executable }

/// Roots vs executables: the add/remove flows are identical apart from which
/// list they touch and which store method they call.
enum _PathKind { roots, executables }

class _JavaPageState extends ConsumerState<JavaPage> {
  _JavaState _state = _JavaState.idle;
  JavaScanResult? _result;
  String? _reason;
  String? _details;
  int _progressDone = 0;
  int _progressTotal = 0;

  late final JavaPathStore _store;
  late final Future<void> _pathsReady;
  List<String> _roots = const <String>[];
  List<String> _executables = const <String>[];

  final TextEditingController _search = TextEditingController();
  String _query = '';

  /// True when a picked folder yielded no runtimes; the UI then asks for an
  /// executable as the stronger fallback.
  bool _directoryAttemptEmpty = false;

  /// Non-null when a picked executable produced no usable runtime or an error.
  String? _invalidExecutablePath;
  String? _invalidExecutableDetail;

  @override
  void initState() {
    super.initState();
    _store = widget.pathStore;
    _pathsReady = _loadSavedPaths();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _runScan();
    });
  }

  @override
  void dispose() {
    _search.dispose();
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
    if (_state == _JavaState.scanning) return;

    await _pathsReady;
    if (!mounted) return;

    final roots = List<String>.unmodifiable(_roots);
    final executables = List<String>.unmodifiable(_executables);

    setState(() {
      _state = _JavaState.scanning;
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
      final client = ref.read(coreClientProvider);
      if (client == null) {
        if (!mounted) return;
        setState(
          () => _setUnavailable(
            CoreUnavailableException('The native core is not connected.'),
          ),
        );
        return;
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
        _state = _JavaState.ready;
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
        setState(() {
          _state = _JavaState.ready;
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
    if (_state == _JavaState.scanning) return;

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
    if (_state == _JavaState.scanning) return;

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

  Future<void> _addRoot(String path) =>
      _persist(_PathKind.roots, _mergePaths(<String>[..._roots, path]));

  Future<void> _addExecutable(String path) => _persist(
    _PathKind.executables,
    _mergePaths(<String>[..._executables, path]),
  );

  Future<void> _removeRoot(String path) => _persist(
    _PathKind.roots,
    _mergePaths(_roots.where((value) => value != path)),
  );

  Future<void> _removeExecutable(String path) => _persist(
    _PathKind.executables,
    _mergePaths(_executables.where((value) => value != path)),
  );

  /// Applies a merged path list to the page state and persists it; persistence
  /// is best-effort and never touches the in-memory list (the chip is already
  /// gone / already added there).
  Future<void> _persist(_PathKind kind, List<String> updated) async {
    setState(() {
      switch (kind) {
        case _PathKind.roots:
          _roots = updated;
        case _PathKind.executables:
          _executables = updated;
      }
    });
    try {
      switch (kind) {
        case _PathKind.roots:
          await _store.saveRoots(updated);
        case _PathKind.executables:
          await _store.saveExecutables(updated);
      }
    } catch (_) {
      // Persistence is best-effort; the in-memory path is still used.
    }
  }

  void _setUnavailable(Object error) {
    _state = _JavaState.unavailable;
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

  Future<void> _selectRuntime(JavaRuntime runtime) async {
    final l10n = AppLocalizations.of(context);
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setJavaSelection(javaPath: runtime.path, javaAuto: false);
      if (!mounted) return;
      toast(l10n.settingsJavaSelected, type: ToastType.success);
    } catch (error) {
      if (!mounted) return;
      toast(error.toString(), type: ToastType.error);
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

  List<JavaRuntime> _visibleRuntimes(JavaScanResult? result) {
    final items = result?.items ?? const <JavaRuntime>[];
    final query = _query.trim().toLowerCase();
    if (query.isEmpty) return items;
    return items
        .where(
          (runtime) =>
              [
                runtime.path,
                runtime.home,
                runtime.version,
                runtime.vendor,
                runtime.arch,
                runtime.source,
              ].whereType<String>().any(
                (value) => value.toLowerCase().contains(query),
              ),
        )
        .toList(growable: false);
  }

  Future<void> _setAuto(bool auto) async {
    final l10n = AppLocalizations.of(context);
    final settings = ref.read(appSettingsProvider).value;
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setJavaSelection(
            javaPath: auto ? null : settings?.javaPath,
            javaAuto: auto,
          );
      if (!mounted) return;
      toast(l10n.settingsSaved, type: ToastType.success);
    } catch (error) {
      if (!mounted) return;
      toast(error.toString(), type: ToastType.error);
    }
  }

  /// Installs a Mojang Java runtime in the background and rescans afterwards.
  Future<void> _installJavaRuntime() async {
    final l10n = AppLocalizations.of(context);
    final major = await showDialog<int>(
      context: context,
      builder: (context) {
        final textTheme = Theme.of(context).textTheme;
        final tokens = context.tokens;
        return SimpleDialog(
          title: Text(l10n.javaInstallTitle),
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(
                AppSpacing.lg,
                0,
                AppSpacing.lg,
                AppSpacing.sm,
              ),
              child: Text(
                l10n.javaInstallHint,
                style: textTheme.bodySmall?.copyWith(
                  color: tokens.textTertiary,
                ),
              ),
            ),
            for (final value in const [8, 17, 21, 25])
              SimpleDialogOption(
                onPressed: () => Navigator.of(context).pop(value),
                child: Row(
                  children: [
                    Icon(AppIcons.java, size: 18, color: tokens.accent),
                    const SizedBox(width: AppSpacing.sm),
                    Text('Java $value', style: textTheme.titleSmall),
                    if (value == 21) ...[
                      const SizedBox(width: AppSpacing.xs),
                      Text(
                        l10n.javaInstallRecommended,
                        style: textTheme.labelSmall?.copyWith(
                          color: tokens.accent,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
          ],
        );
      },
    );
    if (major == null || !mounted) return;

    toast(l10n.javaInstallQueued(major), type: ToastType.success);
    unawaited(() async {
      await ref
          .read(downloadsProvider.notifier)
          .installJavaRuntime(
            major,
            successMessage: l10n.javaInstallDone(major),
            failureMessage: l10n.javaInstallFailed(major),
            cancelledMessage: l10n.javaInstallCancelled(major),
          );
      if (mounted) await _runScan();
    }());
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final settings = ref.watch(appSettingsProvider).value;
    final auto = settings?.javaAuto ?? true;
    final selectedPath = settings?.javaPath;
    final result = _result;
    final scanning = _state == _JavaState.scanning;
    final visible = _visibleRuntimes(result);

    return SingleChildScrollView(
      padding: const EdgeInsets.only(bottom: AppSpacing.xl),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppPageHeader(
            leading: IconButton.filledTonal(
              onPressed: () => context.go('/settings'),
              tooltip: l10n.back,
              icon: const Icon(AppIcons.back, size: 18),
            ),
            title: l10n.settingsJava,
            subtitle: l10n.javaPageSubtitle,
            actions: [
              OutlinedButton.icon(
                onPressed: _installJavaRuntime,
                icon: const Icon(AppIcons.download, size: 18),
                label: Text(l10n.javaInstallAction),
              ),
              const SizedBox(width: AppSpacing.xs),
              OutlinedButton.icon(
                onPressed: scanning ? null : () => _runScan(),
                icon: scanning
                    ? const SizedBox(
                        width: 14,
                        height: 14,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(AppIcons.scan, size: 18),
                label: Text(
                  scanning ? l10n.javaStatusScanning : l10n.javaStatusScan,
                ),
              ),
            ],
          ),
          const SizedBox(height: AppSpacing.lg),
          _CurrentJavaPanel(
            auto: auto,
            selectedPath: selectedPath,
            runtimes: result?.items ?? const <JavaRuntime>[],
            onAutoChanged: scanning ? null : _setAuto,
          ),
          const SizedBox(height: AppSpacing.xl),
          _Toolbar(
            controller: _search,
            query: _query,
            onQueryChanged: (value) => setState(() => _query = value),
            onAddFolder: scanning
                ? null
                : () => _searchFolder(
                    verifyOnly: (result?.items ?? const []).isEmpty,
                  ),
            onPickExecutable: scanning ? null : _selectExecutable,
          ),
          const SizedBox(height: AppSpacing.sm),
          if (scanning) ...[
            Text(
              _progressTotal > 0
                  ? l10n.javaStatusProgress(_progressDone, _progressTotal)
                  : l10n.javaStatusScanningHint,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            ),
            const SizedBox(height: AppSpacing.xs),
            AppProgressBar(
              value: _progressTotal > 0
                  ? (_progressDone / _progressTotal).clamp(0.0, 1.0)
                  : null,
              height: 4,
            ),
            const SizedBox(height: AppSpacing.md),
          ],
          if (_state == _JavaState.unavailable) ...[
            _Notice(
              icon: AppIcons.error,
              color: tokens.danger,
              message: _reason != null
                  ? l10n.javaStatusScanFailed(_reason!)
                  : l10n.javaStatusUnavailableHint,
              details: _details,
            ),
            const SizedBox(height: AppSpacing.md),
          ],
          if (_invalidExecutablePath != null) ...[
            _Notice(
              icon: AppIcons.terminal,
              color: tokens.danger,
              message: l10n.javaStatusNoValidExecutable(
                _invalidExecutablePath!,
              ),
              details:
                  _invalidExecutableDetail ??
                  l10n.javaStatusNoValidExecutableHint,
            ),
            const SizedBox(height: AppSpacing.md),
          ],
          if (result != null && _state == _JavaState.ready) ...[
            Row(
              children: [
                Text(
                  l10n.javaStatusCount(visible.length),
                  style: textTheme.labelMedium?.copyWith(
                    color: visible.isEmpty ? tokens.warning : tokens.success,
                  ),
                ),
                if (_query.trim().isNotEmpty) ...[
                  const SizedBox(width: AppSpacing.xs),
                  Text(
                    '/ ${result.count}',
                    style: textTheme.labelSmall?.copyWith(
                      color: tokens.textTertiary,
                    ),
                  ),
                ],
              ],
            ),
            const SizedBox(height: AppSpacing.sm),
          ],
          if (_roots.isNotEmpty || _executables.isNotEmpty) ...[
            _CustomPaths(
              roots: _roots,
              executables: _executables,
              onRemoveRoot: _removeRoot,
              onRemoveExecutable: _removeExecutable,
            ),
            const SizedBox(height: AppSpacing.md),
          ],
          if (result == null && _state == _JavaState.idle)
            Text(
              l10n.javaStatusIdleHint,
              style: textTheme.bodySmall?.copyWith(color: tokens.textTertiary),
            )
          else if (visible.isEmpty && _state != _JavaState.scanning)
            AppEmptyState(
              icon: AppIcons.java,
              title: _query.trim().isNotEmpty
                  ? l10n.javaFilteredEmpty
                  : l10n.javaStatusNoResultsTitle,
              message: _query.trim().isNotEmpty
                  ? l10n.javaSearchHint
                  : _directoryAttemptEmpty
                  ? l10n.javaStatusStillNotFound
                  : l10n.javaStatusNoResultsHint,
            )
          else
            _RuntimeGrid(
              runtimes: visible,
              selectedPath: selectedPath,
              onSelect: _selectRuntime,
            ),
        ],
      ),
    );
  }
}
