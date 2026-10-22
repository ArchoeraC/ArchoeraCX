import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/core_client.dart';
import '../services/core/java_scan.dart';
import '../services/java/java_path_store.dart';
import '../stores/providers.dart';
import '../stores/settings.dart';
import '../theme/tokens.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_panel.dart';
import '../widgets/common/toast.dart';

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
              icon: const Icon(Icons.arrow_back_rounded, size: 18),
            ),
            title: l10n.settingsJava,
            subtitle: l10n.javaPageSubtitle,
            actions: [
              OutlinedButton.icon(
                onPressed: scanning ? null : () => _runScan(),
                icon: scanning
                    ? const SizedBox(
                        width: 14,
                        height: 14,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.manage_search_rounded, size: 18),
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
            LinearProgressIndicator(
              value: _progressTotal > 0
                  ? (_progressDone / _progressTotal).clamp(0.0, 1.0)
                  : null,
              minHeight: 3,
              backgroundColor: tokens.surface2,
              color: tokens.accent,
            ),
            const SizedBox(height: AppSpacing.md),
          ],
          if (_state == _JavaState.unavailable) ...[
            _Notice(
              icon: Icons.error_outline_rounded,
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
              icon: Icons.terminal_rounded,
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
              icon: Icons.coffee_rounded,
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

/// Current selection summary plus the automatic-selection switch.
class _CurrentJavaPanel extends StatelessWidget {
  const _CurrentJavaPanel({
    required this.auto,
    required this.selectedPath,
    required this.runtimes,
    required this.onAutoChanged,
  });

  final bool auto;
  final String? selectedPath;
  final List<JavaRuntime> runtimes;
  final ValueChanged<bool>? onAutoChanged;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    JavaRuntime? selected;
    for (final runtime in runtimes) {
      if (runtime.path == selectedPath) {
        selected = runtime;
        break;
      }
    }

    final title = auto
        ? l10n.settingsJavaAutoValue
        : (selected?.home ?? selectedPath ?? '—');
    final detail = auto
        ? l10n.settingsJavaAutoHint
        : selected == null
        ? selectedPath
        : l10n.javaStatusMeta(selected.displayVersion, selected.arch ?? '—');

    return AppPanel(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Text(
                l10n.settingsJavaCurrent,
                style: textTheme.labelSmall?.copyWith(
                  color: tokens.textSecondary,
                  letterSpacing: 1.2,
                ),
              ),
              const Spacer(),
              Text(
                l10n.settingsJavaAuto,
                style: textTheme.labelMedium?.copyWith(
                  color: tokens.textSecondary,
                ),
              ),
              const SizedBox(width: AppSpacing.xxs),
              Switch(value: auto, onChanged: onAutoChanged),
            ],
          ),
          const SizedBox(height: AppSpacing.sm),
          Container(height: 1, color: tokens.border),
          const SizedBox(height: AppSpacing.md),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(
                auto ? Icons.auto_awesome_rounded : Icons.coffee_rounded,
                size: 18,
                color: tokens.accent,
              ),
              const SizedBox(width: AppSpacing.xs),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.titleSmall,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      detail ?? '',
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: textTheme.bodySmall?.copyWith(
                        color: tokens.textTertiary,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _Toolbar extends StatelessWidget {
  const _Toolbar({
    required this.controller,
    required this.query,
    required this.onQueryChanged,
    required this.onAddFolder,
    required this.onPickExecutable,
  });

  final TextEditingController controller;
  final String query;
  final ValueChanged<String> onQueryChanged;
  final VoidCallback? onAddFolder;
  final VoidCallback? onPickExecutable;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    final searchField = TextField(
      controller: controller,
      onChanged: onQueryChanged,
      style: Theme.of(context).textTheme.bodySmall,
      decoration: InputDecoration(
        isDense: true,
        hintText: l10n.javaSearchHint,
        prefixIcon: Icon(
          Icons.search_rounded,
          size: 18,
          color: tokens.textTertiary,
        ),
        suffixIcon: query.isEmpty
            ? null
            : IconButton(
                tooltip: l10n.close,
                onPressed: () {
                  controller.clear();
                  onQueryChanged('');
                },
                icon: const Icon(Icons.close_rounded, size: 16),
              ),
      ),
    );

    final actions = <Widget>[
      OutlinedButton.icon(
        onPressed: onAddFolder,
        icon: const Icon(Icons.folder_open_rounded, size: 18),
        label: Text(l10n.javaStatusAddFolder),
      ),
      OutlinedButton.icon(
        onPressed: onPickExecutable,
        icon: const Icon(Icons.terminal_rounded, size: 18),
        label: Text(l10n.javaStatusSelectExecutable),
      ),
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        if (constraints.maxWidth < 720) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              searchField,
              const SizedBox(height: AppSpacing.xs),
              Wrap(
                spacing: AppSpacing.xs,
                runSpacing: AppSpacing.xs,
                children: actions,
              ),
            ],
          );
        }

        return Row(
          children: [
            Expanded(child: searchField),
            const SizedBox(width: AppSpacing.md),
            ...actions.map(
              (action) => Padding(
                padding: const EdgeInsets.only(left: AppSpacing.xs),
                child: action,
              ),
            ),
          ],
        );
      },
    );
  }
}

class _RuntimeGrid extends StatelessWidget {
  const _RuntimeGrid({
    required this.runtimes,
    required this.selectedPath,
    required this.onSelect,
  });

  final List<JavaRuntime> runtimes;
  final String? selectedPath;
  final ValueChanged<JavaRuntime> onSelect;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = constraints.maxWidth >= 1040 ? 2 : 1;
        final gap = AppSpacing.md;
        final width = (constraints.maxWidth - gap * (columns - 1)) / columns;

        return Wrap(
          spacing: gap,
          runSpacing: gap,
          children: [
            for (final runtime in runtimes)
              SizedBox(
                width: width,
                child: _RuntimeTile(
                  runtime: runtime,
                  selected: runtime.path == selectedPath,
                  onTap: () => onSelect(runtime),
                ),
              ),
          ],
        );
      },
    );
  }
}

class _RuntimeTile extends StatelessWidget {
  const _RuntimeTile({
    required this.runtime,
    required this.selected,
    required this.onTap,
  });

  final JavaRuntime runtime;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final major = runtime.major;
    final badge = major == null ? l10n.javaStatusUnknownVersion : 'Java $major';

    return AppPanel(
      accent: selected,
      onTap: onTap,
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.md,
        vertical: AppSpacing.sm,
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(
              horizontal: AppSpacing.xs,
              vertical: 3,
            ),
            decoration: ShapeDecoration(
              color: selected
                  ? tokens.accent.withValues(alpha: 0.14)
                  : tokens.surface2,
              shape: BeveledRectangleBorder(
                side: BorderSide(
                  color: selected
                      ? tokens.accent.withValues(alpha: 0.45)
                      : tokens.border,
                ),
                borderRadius: BorderRadius.circular(tokens.radiusS),
              ),
            ),
            child: Text(
              badge,
              style: textTheme.labelSmall?.copyWith(
                color: selected ? tokens.accent : tokens.textSecondary,
                letterSpacing: 0,
              ),
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
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
                    style: textTheme.labelLarge,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  [
                    runtime.displayVersion,
                    runtime.arch ?? '—',
                    if (runtime.vendor != null && runtime.vendor!.isNotEmpty)
                      runtime.vendor!,
                    if (runtime.source != null && runtime.source!.isNotEmpty)
                      runtime.source!,
                  ].join(' · '),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: textTheme.bodySmall?.copyWith(
                    color: tokens.textTertiary,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          if (selected)
            Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.check_rounded, size: 16, color: tokens.accent),
                const SizedBox(width: 2),
                Text(
                  l10n.javaSelectedBadge,
                  style: textTheme.labelSmall?.copyWith(color: tokens.accent),
                ),
              ],
            )
          else
            Icon(
              Icons.chevron_right_rounded,
              size: 18,
              color: tokens.textTertiary,
            ),
        ],
      ),
    );
  }
}

class _Notice extends StatelessWidget {
  const _Notice({
    required this.icon,
    required this.color,
    required this.message,
    this.details,
  });

  final IconData icon;
  final Color color;
  final String message;
  final String? details;

  @override
  Widget build(BuildContext context) {
    final tokens = context.tokens;
    final textTheme = Theme.of(context).textTheme;
    final detail = details;

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 16, color: color),
        const SizedBox(width: AppSpacing.xs),
        Expanded(
          child: Text(
            message,
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: textTheme.bodySmall?.copyWith(color: tokens.textSecondary),
          ),
        ),
        if (detail != null)
          Tooltip(
            message: detail,
            child: Icon(
              Icons.info_outline_rounded,
              size: 16,
              color: tokens.textTertiary,
            ),
          ),
      ],
    );
  }
}

class _CustomPaths extends StatelessWidget {
  const _CustomPaths({
    required this.roots,
    required this.executables,
    required this.onRemoveRoot,
    required this.onRemoveExecutable,
  });

  final List<String> roots;
  final List<String> executables;
  final ValueChanged<String> onRemoveRoot;
  final ValueChanged<String> onRemoveExecutable;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final tokens = context.tokens;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          l10n.javaStatusCustomPaths,
          style: Theme.of(context).textTheme.labelSmall
              ?.copyWith(color: tokens.textTertiary),
        ),
        const SizedBox(height: AppSpacing.xxs),
        Wrap(
          spacing: AppSpacing.xxs,
          runSpacing: AppSpacing.xxs,
          children: [
            for (final path in roots)
              _PathChip(
                path: path,
                removeLabel: l10n.javaStatusRemovePath(path),
                onDeleted: () => onRemoveRoot(path),
              ),
            for (final path in executables)
              _PathChip(
                path: path,
                removeLabel: l10n.javaStatusRemovePath(path),
                onDeleted: () => onRemoveExecutable(path),
              ),
          ],
        ),
      ],
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
