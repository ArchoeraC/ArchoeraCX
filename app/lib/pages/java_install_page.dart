import 'package:file_selector/file_selector.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../l10n/generated/app_localizations.dart';
import '../services/core/core_client.dart';
import '../services/core/java/java_distribution.dart';
import '../services/core/java/java_path_store.dart';
import '../stores/java.dart';
import '../stores/providers.dart';
import '../stores/settings.dart';
import '../theme/icons/app_icons.dart';
import '../theme/tokens.dart';
import '../utils/app_paths.dart';
import '../utils/format.dart';
import '../widgets/common/app_empty_state.dart';
import '../widgets/common/app_page_header.dart';
import '../widgets/common/app_section.dart';
import '../widgets/common/app_progress_bar.dart';
import '../widgets/common/confirm.dart';
import '../widgets/common/safe_url.dart';
import '../widgets/common/toast.dart';

part 'java_install/cve_panel.dart';
part 'java_install/install_panel.dart';
part 'java_install/selection_panels.dart';

/// Opens a native folder picker and returns the selected absolute path.
typedef JavaInstallPickDirectory = Future<String?> Function();

/// Standalone JDK installer.
///
/// Replaces the old four-option dialog: vendors, feature releases and every
/// published build are enumerated on one page, and the archive is installed
/// into the launcher-managed root or a user-chosen directory. All work is
/// event-driven through `java.distributions.*`; no polling or periodic timer
/// is involved.
class JavaInstallPage extends ConsumerStatefulWidget {
  const JavaInstallPage({
    super.key,
    this.pickDirectory,
    this.pathStore = const JavaPathStore(),
  });

  /// Optional folder picker; tests inject a deterministic path.
  final JavaInstallPickDirectory? pickDirectory;

  /// Persistence for roots the Java page should scan afterwards.
  final JavaPathStore pathStore;

  @override
  ConsumerState<JavaInstallPage> createState() => _JavaInstallPageState();
}

class _JavaInstallPageState extends ConsumerState<JavaInstallPage> {
  JavaDistributionCatalog? _catalog;
  bool _catalogLoading = true;
  String? _catalogError;
  JavaDistributionPlatformInfo? _selectedPlatform;

  String? _providerId;
  JavaDistributionMajors? _majors;
  bool _majorsLoading = false;
  String? _majorsError;

  int? _major;
  JavaDistributionVersions? _versions;
  bool _versionsLoading = false;
  String? _versionsError;
  String? _selectedVersionId;

  String? _customDirectory;
  bool _autoExtract = true;
  bool _autoDelete = true;
  bool _allowOutdated = false;
  JavaCveReport? _cveReport;
  bool _cveLoading = false;
  String? _cveError;
  int _cveGeneration = 0;
  bool _acknowledgeCves = false;
  bool _installing = false;
  bool _cancelling = false;
  int? _installTaskId;
  int _done = 0;
  int _total = 0;
  int _rate = 0;
  String? _phase;
  String? _installError;
  JavaDistributionInstallResult? _installed;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _loadCatalog();
    });
  }

  AppLocalizations get _l10n => AppLocalizations.of(context);

  bool get _foreignPlatform =>
      _selectedPlatform != null && !_selectedPlatform!.current;

  Future<void> _loadCatalog() async {
    final client = ref.read(coreClientProvider);
    if (client == null) {
      setState(() {
        _catalogLoading = false;
        _catalogError = _l10n.commonCoreNotConnected;
      });
      return;
    }

    setState(() {
      _catalogLoading = true;
      _catalogError = null;
    });
    try {
      final catalog = await client.javaDistributions();
      if (!mounted) return;
      final current = catalog.platforms.firstWhere(
        (platform) => platform.current,
        orElse: () => catalog.platform,
      );
      setState(() {
        _catalog = catalog;
        _selectedPlatform = current;
        _catalogLoading = false;
      });
      if (catalog.providers.isNotEmpty) {
        await _selectProvider(catalog.providers.first.id);
      }
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _catalogLoading = false;
        _catalogError = _describe(error);
      });
    }
  }

  void _resetCveState({bool cancelPending = true}) {
    if (cancelPending) _cveGeneration++;
    _cveReport = null;
    _cveLoading = false;
    _cveError = null;
    _acknowledgeCves = false;
  }

  Future<void> _loadAdvisories(
    String providerId,
    int major,
    String versionId,
  ) async {
    final client = ref.read(coreClientProvider);
    final generation = ++_cveGeneration;
    final platform = _selectedPlatform;
    if (client == null) {
      setState(() {
        _cveLoading = false;
        _cveError = _l10n.commonCoreNotConnected;
      });
      return;
    }

    setState(() {
      _cveLoading = true;
      _cveReport = null;
      _cveError = null;
      _acknowledgeCves = false;
    });
    try {
      final report = await client.javaDistributionAdvisories(
        provider: providerId,
        major: major,
        versionId: versionId,
        platform: platform,
      );
      if (!mounted ||
          generation != _cveGeneration ||
          _selectedVersionId != versionId ||
          !_isCurrentPlatform(platform)) {
        return;
      }
      setState(() {
        _cveLoading = false;
        _cveReport = report;
      });
    } catch (error) {
      if (!mounted ||
          generation != _cveGeneration ||
          _selectedVersionId != versionId) {
        return;
      }
      setState(() {
        _cveLoading = false;
        _cveError = _describe(error);
      });
    }
  }

  void _selectVersion(String id) {
    final providerId = _providerId;
    final major = _major;
    setState(() {
      _selectedVersionId = id;
      _allowOutdated = false;
      _installed = null;
      _installError = null;
    });
    _resetCveState(cancelPending: false);
    if (providerId != null && major != null) {
      _loadAdvisories(providerId, major, id);
    }
  }

  Future<void> _selectProvider(String providerId, {bool force = false}) async {
    if (!force && _providerId == providerId && _majors != null) return;
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    final platform = _selectedPlatform;

    setState(() {
      _providerId = providerId;
      _majors = null;
      _majorsLoading = true;
      _majorsError = null;
      _major = null;
      _versions = null;
      _versionsError = null;
      _selectedVersionId = null;
      _allowOutdated = false;
      _installed = null;
      _installError = null;
      _resetCveState();
    });

    try {
      final majors = await client.javaDistributionMajors(
        provider: providerId,
        platform: platform,
      );
      if (!mounted ||
          _providerId != providerId ||
          !_isCurrentPlatform(platform)) {
        return;
      }
      final preferred = majors.majors.contains(majors.recommendedMajor)
          ? majors.recommendedMajor
          : (majors.majors.isEmpty ? null : majors.majors.last);
      setState(() {
        _majors = majors;
        _majorsLoading = false;
        _major = preferred;
      });
      if (preferred != null) {
        await _selectMajor(providerId, preferred, platform);
      }
    } catch (error) {
      if (!mounted ||
          _providerId != providerId ||
          !_isCurrentPlatform(platform)) {
        return;
      }
      setState(() {
        _majorsLoading = false;
        _majorsError = _describe(error);
      });
    }
  }

  bool _isCurrentPlatform(JavaDistributionPlatformInfo? platform) {
    if (platform == null) return _selectedPlatform == null;
    final current = _selectedPlatform;
    if (current == null) return false;
    return current.os == platform.os &&
        current.arch == platform.arch &&
        current.archive == platform.archive;
  }

  Future<void> _selectPlatform(JavaDistributionPlatformInfo platform) async {
    final selected = _selectedPlatform;
    if (selected != null &&
        selected.os == platform.os &&
        selected.arch == platform.arch &&
        selected.archive == platform.archive) {
      return;
    }

    setState(() {
      _selectedPlatform = platform;
      _majors = null;
      _major = null;
      _versions = null;
      _selectedVersionId = null;
      _allowOutdated = false;
      _installed = null;
      _installError = null;
      _resetCveState();
    });
    final providerId = _providerId;
    if (providerId != null) {
      await _selectProvider(providerId, force: true);
    }
  }

  Future<void> _selectMajor(
    String providerId,
    int major, [
    JavaDistributionPlatformInfo? platform,
  ]) async {
    final client = ref.read(coreClientProvider);
    if (client == null) return;
    final target = platform ?? _selectedPlatform;

    setState(() {
      _major = major;
      _versions = null;
      _versionsLoading = true;
      _versionsError = null;
      _selectedVersionId = null;
      _allowOutdated = false;
      _installed = null;
      _installError = null;
      _resetCveState();
    });
    try {
      final versions = await client.javaDistributionVersions(
        provider: providerId,
        major: major,
        platform: target,
      );
      if (!mounted ||
          _providerId != providerId ||
          _major != major ||
          !_isCurrentPlatform(target)) {
        return;
      }
      JavaDistributionVersion? selected;
      for (final version in versions.versions) {
        if (version.recommended) {
          selected = version;
          break;
        }
      }
      selected ??= versions.versions.isEmpty ? null : versions.versions.first;
      setState(() {
        _versions = versions;
        _versionsLoading = false;
        _selectedVersionId = selected?.id;
      });
      if (selected != null) {
        _loadAdvisories(providerId, major, selected.id);
      }
    } catch (error) {
      if (!mounted ||
          _providerId != providerId ||
          _major != major ||
          !_isCurrentPlatform(target)) {
        return;
      }
      setState(() {
        _versionsLoading = false;
        _versionsError = _describe(error);
      });
    }
  }

  JavaDistributionVersion? get _selectedVersion {
    final id = _selectedVersionId;
    if (id == null) return null;
    for (final version
        in _versions?.versions ?? const <JavaDistributionVersion>[]) {
      if (version.id == id) return version;
    }

    return null;
  }

  JavaDistributionProviderInfo? get _selectedProvider {
    for (final provider
        in _catalog?.providers ?? const <JavaDistributionProviderInfo>[]) {
      if (provider.id == _providerId) return provider;
    }

    return null;
  }

  /// The exact folder the backend will use. It mirrors the C# default
  /// (`<root>/<provider>-<version-slug>`) so the page shows the real target
  /// before a byte is downloaded.
  String? get _targetPath {
    final catalog = _catalog;
    if (catalog == null) return null;
    final custom = _customDirectory;
    if (custom != null) return custom;

    final providerId = _providerId;
    final version = _selectedVersion;
    final platform = _selectedPlatform;
    if (providerId == null || version == null) return catalog.defaultRoot;
    final folderProvider = platform != null && !platform.current
        ? '$providerId-${platform.os}-${platform.arch}'
        : providerId;
    return joinPath(
      catalog.defaultRoot,
      '$folderProvider-${_versionSlug(version.version)}',
    );
  }

  Future<void> _chooseDirectory() async {
    String? path;
    final injected = widget.pickDirectory;
    if (injected != null) {
      path = await injected();
    } else {
      path = await getDirectoryPath(
        confirmButtonText: _l10n.javaInstallChooseDirectory,
      );
    }

    if (!mounted) return;
    final normalized = path?.trim();
    if (normalized == null || normalized.isEmpty) return;
    setState(() {
      _customDirectory = normalized;
      _installed = null;
      _installError = null;
    });
  }

  void _useDefaultDirectory() {
    setState(() {
      _customDirectory = null;
      _installed = null;
      _installError = null;
    });
  }

  Future<bool> _confirmRisk(AppLocalizations l10n) async {
    final advisories = _cveReport?.advisories ?? const <JavaCveAdvisory>[];
    final platform = _selectedPlatform;
    final host = _catalog?.platform;
    final parts = <String>[];
    if (platform != null && !platform.current && host != null) {
      parts.add(
        l10n.javaInstallRiskPlatform(
          platform.os,
          platform.arch,
          host.os,
          host.arch,
        ),
      );
    }

    if (advisories.isNotEmpty) {
      final ids = advisories.take(6).map((item) => item.id).join(', ');
      final suffix = advisories.length > 6 ? ', …' : '';
      parts.add(l10n.javaInstallRiskCve(advisories.length, '$ids$suffix'));
    }

    if (parts.isEmpty) return true;
    final confirmed = await confirmDestructive(
      context,
      l10n,
      title: l10n.javaInstallRiskTitle,
      body: parts.join('\n\n'),
      confirmLabel: l10n.javaInstallRiskAction,
    );
    return confirmed == true;
  }

  Future<void> _install() async {
    final l10n = _l10n;
    final providerId = _providerId;
    final version = _selectedVersion;
    final target = _targetPath;
    if (version == null || providerId == null) {
      toast(l10n.javaInstallVersionRequired, type: ToastType.error);
      return;
    }

    if (target == null || target.trim().isEmpty) {
      toast(l10n.javaInstallDirectoryRequired, type: ToastType.error);
      return;
    }

    final client = ref.read(coreClientProvider);
    if (client == null) {
      toast(l10n.commonCoreNotConnected, type: ToastType.error);
      return;
    }

    final foreign = _foreignPlatform;
    if (!await _confirmRisk(l10n)) return;
    if (!mounted) return;

    setState(() {
      _installing = true;
      _cancelling = false;
      _installTaskId = null;
      _done = 0;
      _total = version.size;
      _rate = 0;
      _installError = null;
      _installed = null;
      _phase = l10n.javaInstallDownloading;
    });

    try {
      final result = await client.installJavaDistribution(
        provider: providerId,
        versionId: version.id,
        directory: _customDirectory,
        autoExtract: _autoExtract,
        autoDelete: _autoDelete,
        allowOutdated: _allowOutdated,
        acknowledgeCves: _acknowledgeCves,
        platform: _selectedPlatform,
        allowIncompatible: foreign,
        label: 'Java ${version.major}',
        onTaskStarted: (taskId) {
          if (!mounted || !_installing) return;
          setState(() => _installTaskId = taskId);
        },
        onProgress: (done, total) {
          if (!mounted || _cancelling) return;
          setState(() {
            _done = done;
            _total = total > 0 ? total : _total;
          });
        },
        onRate: (rate) {
          if (!mounted || _cancelling || rate == _rate) return;
          setState(() => _rate = rate);
        },
        onDetailedProgress: (done, total, message) {
          if (!mounted || _cancelling) return;
          final extracting = total > 0 && done >= total;
          setState(() {
            _rate = extracting ? 0 : _rate;
            _phase = extracting
                ? l10n.javaInstallExtracting
                : l10n.javaInstallDownloading;
          });
        },
      );
      if (!mounted) return;

      setState(() {
        _installing = false;
        _cancelling = false;
        _installTaskId = null;
        _installed = result;
        _done = result.bytes;
        _total = result.bytes;
        _rate = 0;
        _phase = null;
      });
      if (!foreign) {
        await _rememberRoot(result.home);
        ref.invalidate(javaRuntimesProvider);
      }
      if (!mounted) return;
      toast(l10n.javaInstallSuccess(result.version), type: ToastType.success);
    } catch (error) {
      if (!mounted) return;
      final cancelled = error is CoreException && error.code == 'E_CANCELLED';
      final message = cancelled ? null : _describe(error);
      setState(() {
        _installing = false;
        _cancelling = false;
        _installTaskId = null;
        _rate = 0;
        _installError = message;
        _phase = null;
      });
      if (cancelled) {
        toast(l10n.javaInstallCancelled, type: ToastType.info);
      } else {
        toast(message!, type: ToastType.error);
      }
    }
  }

  void _cancelInstall() {
    if (_cancelling) return;
    final taskId = _installTaskId;
    final client = ref.read(coreClientProvider);
    if (taskId == null || client == null) return;
    setState(() {
      _cancelling = true;
      _phase = _l10n.javaInstallCancelling;
    });
    try {
      client.cancel(taskId);
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _cancelling = false;
        _phase = _l10n.javaInstallDownloading;
      });
      toast(_describe(error), type: ToastType.error);
    }
  }

  /// Adds a custom location to the Java page's scan roots so the next scan
  /// finds the runtime without another manual folder pick.
  Future<void> _rememberRoot(String home) async {
    final defaultRoot = _catalog?.defaultRoot ?? '';
    if (defaultRoot.isNotEmpty &&
        (home == defaultRoot ||
            home.startsWith('$defaultRoot/') ||
            home.startsWith('$defaultRoot\\'))) {
      // The managed runtime root is already part of every scan.
      return;
    }

    try {
      final roots = await widget.pathStore.loadRoots();
      if (roots.contains(home)) return;
      await widget.pathStore.saveRoots([...roots, home]);
    } catch (_) {
      // Persistence is best-effort; install already succeeded.
    }
  }

  Future<void> _useInstalledRuntime() async {
    final result = _installed;
    if (result == null) return;
    final l10n = _l10n;
    if (_foreignPlatform) {
      toast(l10n.javaInstallIncompatibleUse, type: ToastType.error);
      return;
    }
    try {
      await ref
          .read(appSettingsProvider.notifier)
          .setJavaSelection(javaPath: result.executable, javaAuto: false);
      if (!mounted) return;
      toast(l10n.javaInstallSelected, type: ToastType.success);
    } catch (error) {
      if (!mounted) return;
      toast(error.toString(), type: ToastType.error);
    }
  }

  void _back() {
    if (context.canPop()) {
      context.pop();
    } else {
      context.go('/settings/java');
    }
  }

  String _describe(Object error) {
    if (error is CoreException) {
      if (error.code == 'JAVA_VERSION_OUTDATED') {
        return _l10n.javaInstallOutdatedSelected;
      }

      if (error.code == 'JAVA_CVE_UNACKNOWLEDGED') {
        return _l10n.javaInstallCveAcknowledgeHint;
      }

      if (error.code == 'JAVA_PLATFORM_INCOMPATIBLE') {
        return _l10n.javaInstallPlatformIncompatible;
      }

      return error.message;
    }

    return error.toString();
  }

  static String _versionSlug(String version) {
    final slug = version.replaceAll(RegExp('[^A-Za-z0-9._-]+'), '-');
    return slug.replaceAll(RegExp(r'^[-. ]+|[-. ]+$'), '');
  }

  @override
  Widget build(BuildContext context) {
    final l10n = _l10n;
    final tokens = context.tokens;

    return SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(
        AppSpacing.md,
        0,
        AppSpacing.md,
        AppSpacing.xl,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          AppPageHeader(
            leading: IconButton.filledTonal(
              onPressed: _back,
              tooltip: l10n.back,
              icon: const Icon(AppIcons.back, size: 18),
            ),
            title: l10n.javaInstallAction,
            subtitle: l10n.javaInstallPageSubtitle,
          ),
          const SizedBox(height: AppSpacing.md),
          if (_catalogLoading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: AppSpacing.xl),
              child: Center(child: CircularProgressIndicator()),
            )
          else if (_catalogError != null)
            AppEmptyState(
              icon: AppIcons.error,
              title: l10n.javaInstallLoadFailed,
              message: _catalogError!,
              primaryAction: FilledButton(
                onPressed: _loadCatalog,
                child: Text(l10n.refresh),
              ),
            )
          else if (_catalog == null || _catalog!.providers.isEmpty)
            AppEmptyState(
              icon: AppIcons.java,
              title: l10n.javaInstallProvidersEmpty,
              message: l10n.javaInstallPageSubtitle,
              primaryAction: FilledButton(
                onPressed: _loadCatalog,
                child: Text(l10n.refresh),
              ),
            )
          else ...[
            _PlatformSelector(
              platforms: _catalog!.platforms,
              host: _catalog!.platform,
              selected: _selectedPlatform,
              enabled: !_installing,
              onSelected: _selectPlatform,
              l10n: l10n,
            ),
            _ProviderSelector(
              catalog: _catalog!,
              platform: _selectedPlatform,
              selected: _providerId,
              enabled: !_installing,
              onSelected: _selectProvider,
              l10n: l10n,
            ),
            _MajorSelector(
              majors: _majors,
              loading: _majorsLoading,
              error: _majorsError,
              selected: _major,
              enabled: !_installing,
              onSelected: (major) => _selectMajor(_providerId!, major),
              onRetry: () => _selectProvider(_providerId!),
              l10n: l10n,
            ),
            _VersionPanel(
              provider: _selectedProvider,
              versions: _versions,
              loading: _versionsLoading,
              error: _versionsError,
              selectedId: _selectedVersionId,
              enabled: !_installing,
              onSelected: _selectVersion,
              onRetry: () => _selectMajor(_providerId!, _major!),
              l10n: l10n,
            ),
            _DirectoryPanel(
              selected: _selectedVersion,
              targetPath: _targetPath,
              custom: _customDirectory != null,
              enabled: !_installing,
              onChoose: _chooseDirectory,
              onUseDefault: _useDefaultDirectory,
              l10n: l10n,
            ),
            _InstallOptions(
              autoExtract: _autoExtract,
              autoDelete: _autoDelete,
              enabled: !_installing,
              onAutoExtractChanged: (value) => setState(() {
                _autoExtract = value;
                _autoDelete = value;
                _installed = null;
                _installError = null;
              }),
              onAutoDeleteChanged: (value) => setState(() {
                _autoDelete = value;
                _installed = null;
                _installError = null;
              }),
              showOutdatedOption:
                  _selectedVersion != null && !_selectedVersion!.latestPatch,
              allowOutdated: _allowOutdated,
              onAllowOutdatedChanged: (value) => setState(() {
                _allowOutdated = value;
                _installed = null;
                _installError = null;
              }),
              l10n: l10n,
            ),
            if (_selectedVersion != null)
              _CvePanel(
                report: _cveReport,
                loading: _cveLoading,
                error: _cveError,
                acknowledge: _acknowledgeCves,
                enabled: !_installing,
                onAcknowledgeChanged: (value) => setState(() {
                  _acknowledgeCves = value;
                  _installed = null;
                  _installError = null;
                }),
                l10n: l10n,
              ),
            _InstallBar(
              installing: _installing,
              cancelling: _cancelling,
              canCancel: _installTaskId != null,
              autoExtract: _autoExtract,
              done: _done,
              total: _total,
              rate: _rate,
              phase: _phase,
              error: _installError,
              installed: _installed,
              foreign: _foreignPlatform,
              platform: _selectedPlatform,
              canInstall:
                  _selectedVersion != null &&
                  (_selectedVersion!.latestPatch || _allowOutdated) &&
                  !_cveLoading &&
                  (_cveReport == null ||
                      !_cveReport!.available ||
                      _cveReport!.advisories.isEmpty ||
                      _acknowledgeCves) &&
                  _targetPath != null &&
                  _targetPath!.trim().isNotEmpty,
              onInstall: _install,
              onCancel: _cancelInstall,
              onUseRuntime: _useInstalledRuntime,
              onBack: _back,
              l10n: l10n,
              tokens: tokens,
            ),
          ],
        ],
      ),
    );
  }
}
