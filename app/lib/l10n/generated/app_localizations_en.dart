// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appName => 'ArchoeraCX';

  @override
  String get appTagline => 'Cross-platform Minecraft: Java Edition launcher';

  @override
  String get appPreview => 'UI PREVIEW';

  @override
  String get navDashboard => 'Dashboard';

  @override
  String get navInstances => 'Instances';

  @override
  String get navDownload => 'Download';

  @override
  String get navAccounts => 'Accounts';

  @override
  String get navSettings => 'Settings';

  @override
  String get coreOffline => 'Core offline';

  @override
  String get coreOfflineHint => 'Native core is not connected yet.';

  @override
  String get accountOffline => 'Offline';

  @override
  String get accountLocal => 'Local account';

  @override
  String get launch => 'Launch';

  @override
  String get selectInstance => 'Select an instance first';

  @override
  String get commonMockData => 'Mock data · UI shell';

  @override
  String get commonCoreNotConnected => 'The native core is not connected yet.';

  @override
  String get dashboardTitle => 'Dashboard';

  @override
  String get dashboardSubtitle =>
      'A calm place to launch, install, and manage your worlds.';

  @override
  String get dashboardWorkspace => 'Workspace';

  @override
  String get dashboardQuickActions => 'Quick actions';

  @override
  String get dashboardBrowseContent => 'Browse content';

  @override
  String get dashboardEnvironment => 'Environment';

  @override
  String get quickLaunch => 'Quick launch';

  @override
  String get quickLaunchSubtitle => 'Your most recent instance is ready.';

  @override
  String get recentInstances => 'Recent instances';

  @override
  String get viewAll => 'View all';

  @override
  String get statInstances => 'Instances';

  @override
  String get statJava => 'Java';

  @override
  String get statDownloads => 'Downloads';

  @override
  String get statAccounts => 'Accounts';

  @override
  String get instanceName1 => '1.21.4 · Fabric';

  @override
  String get instanceMeta1 => 'Fabric 0.16.10 · 12 mods';

  @override
  String get instanceName2 => '1.20.1 · Forge';

  @override
  String get instanceMeta2 => 'Forge 47.2.0 · 8 mods';

  @override
  String get lastPlayedToday => 'Played today';

  @override
  String get lastPlayedYesterday => 'Played yesterday';

  @override
  String get instancesTitle => 'Instances';

  @override
  String get instancesSubtitle =>
      'Isolated game directories with shared libraries and assets.';

  @override
  String get searchInstances => 'Search instances';

  @override
  String get filterAll => 'All';

  @override
  String get createInstance => 'Create instance';

  @override
  String get importPack => 'Import pack';

  @override
  String get packInstallTitle => 'Install modpack';

  @override
  String get packImportTitle => 'Import modpack';

  @override
  String get packInstallHint =>
      'A new instance will be created. Minecraft and the pack loader are installed from the pack dependencies, then bundled overrides are merged.';

  @override
  String get packInstallRunning => 'Installing modpack…';

  @override
  String get packInstallDone => 'Modpack installed.';

  @override
  String get packInstallFailed => 'Modpack installation failed.';

  @override
  String packInspectMeta(String game, String loader, String loaderVersion) {
    return '$game · $loader $loaderVersion';
  }

  @override
  String packInspectFiles(int count, int overrides) {
    return '$count index files · $overrides bundled overrides';
  }

  @override
  String get packInspectFailed => 'This file is not a valid Modrinth modpack.';

  @override
  String get packLoaderUnsupported =>
      'This pack loader is not supported. Supported loaders: vanilla, Fabric, Quilt, Forge and NeoForge.';

  @override
  String get packJavaMissing =>
      'This pack needs a Java runtime for its loader processors. Install or select a matching Java in Settings first.';

  @override
  String get packCancelling => 'Cancelling…';

  @override
  String get packCancelled => 'Modpack installation was cancelled.';

  @override
  String get packTargetNewInstance => 'Creates a new instance';

  @override
  String packResultSummary(int downloaded, int bundled) {
    return '$downloaded files downloaded · $bundled bundled';
  }

  @override
  String get instanceCreateTitle => 'Create instance';

  @override
  String get instanceNameLabel => 'Name';

  @override
  String get instanceNameHint => 'e.g. 1.21.4 · Fabric';

  @override
  String get instanceGameVersionLabel => 'Game version';

  @override
  String get instanceLocationLabel => 'Instance folder';

  @override
  String get instanceChooseLocation => 'Choose folder';

  @override
  String get instanceLocationHint =>
      'Leave blank to use the global instances folder';

  @override
  String get instanceRootLabel => 'Storage location';

  @override
  String get instanceCustomRoot => 'Custom folder';

  @override
  String get instanceMissing => 'Folder missing';

  @override
  String get instanceCustom => 'Custom';

  @override
  String get instanceSyncDone => 'Instance folders synced.';

  @override
  String instanceSyncAdopted(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count instances indexed',
      one: '1 instance indexed',
    );
    return '$_temp0';
  }

  @override
  String instanceSyncMissing(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count instance folders missing',
      one: '1 instance folder missing',
    );
    return '$_temp0';
  }

  @override
  String get instanceLoaderLabel => 'Loader';

  @override
  String instanceMods(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count mods',
      one: '1 mod',
      zero: 'No mods',
    );
    return '$_temp0';
  }

  @override
  String get instanceLastPlayedNever => 'Never played';

  @override
  String get instanceEmptyTitle => 'No instances yet';

  @override
  String get instanceEmptyMessage =>
      'Create a vanilla or modded instance to get started.';

  @override
  String get instanceNoMatches => 'No instances match your search.';

  @override
  String get instanceDelete => 'Delete instance';

  @override
  String instanceDeleteConfirm(String name) {
    return 'Delete “$name”?';
  }

  @override
  String get instanceCheckQuick => 'Quick check';

  @override
  String get instanceCheckFull => 'Full check';

  @override
  String get instanceCheckTitle => 'Dependency check';

  @override
  String get instanceCheckOk => 'Dependencies are complete.';

  @override
  String instanceCheckMissing(int count) {
    return 'Missing: $count';
  }

  @override
  String instanceCheckMismatched(int count) {
    return 'Mismatched: $count';
  }

  @override
  String get instanceCheckFailed => 'Dependency check failed.';

  @override
  String instanceCheckLaunchFiles(String issue) {
    return 'Launch files: $issue';
  }

  @override
  String get instanceCheckRepair => 'Repair dependencies';

  @override
  String get instanceCheckRepairHint =>
      'Restores missing libraries/assets and reruns loader processors when needed.';

  @override
  String get instanceCheckRepairing => 'Repairing dependencies…';

  @override
  String get instanceCheckRepairDone => 'Dependencies repaired.';

  @override
  String get instanceCheckRepairFailed => 'Dependency repair failed.';

  @override
  String get instanceManifestMissing =>
      'Loader installer not found in this instance.';

  @override
  String get instanceLaunchNotReady => 'Launch flow is not wired yet.';

  @override
  String launchStarted(int pid) {
    return 'Game started (PID $pid).';
  }

  @override
  String get launchFailed => 'Launch failed.';

  @override
  String get launchStarting => 'Starting game…';

  @override
  String get launchNoAccount => 'Add an account before launching.';

  @override
  String get launchOfflineOnly =>
      'Basic launch supports offline accounts only.';

  @override
  String get launchAutoRepairFailed =>
      'Launch files are missing and automatic repair failed. Reinstall the loader from the instance menu.';

  @override
  String get launchAutoRepairBlocked =>
      'Launch files are missing, but downloads are disabled. Enable downloads to repair them.';

  @override
  String get launchJavaMissing => 'No suitable Java runtime found.';

  @override
  String launchLog(String path) {
    return 'Log: $path';
  }

  @override
  String get instanceCreated => 'Instance created.';

  @override
  String get commonCancel => 'Cancel';

  @override
  String get commonCreate => 'Create';

  @override
  String get commonDelete => 'Delete';

  @override
  String get commonSave => 'Save';

  @override
  String get downloadTitle => 'Download';

  @override
  String get downloadSubtitle =>
      'Versions, mods, packs, resource packs, and shaders.';

  @override
  String get sourceLabel => 'Source';

  @override
  String get tabVersions => 'Game versions';

  @override
  String get versionListTitle => 'Minecraft versions';

  @override
  String get versionSearchHint => 'Search versions';

  @override
  String get versionIncludeSnapshots => 'Include snapshots';

  @override
  String get versionUse => 'Use this version';

  @override
  String get versionLoadFailed => 'Could not load versions.';

  @override
  String get versionLoading => 'Loading versions…';

  @override
  String get versionEmpty => 'No versions match.';

  @override
  String get tabMods => 'Mods';

  @override
  String get tabPacks => 'Modpacks';

  @override
  String get tabResourcePacks => 'Resource packs';

  @override
  String get tabShaders => 'Shaders';

  @override
  String get sourceModrinth => 'Modrinth';

  @override
  String get sourceCurseForge => 'CurseForge';

  @override
  String get sourceCurseForgeKeyHint =>
      'Add a CurseForge API key in Settings to use this source.';

  @override
  String get providerNotWired =>
      'Provider wiring is next; this is the UI shell.';

  @override
  String get loaderInstallTitle => 'Install Forge / NeoForge';

  @override
  String get loaderKindLabel => 'Loader';

  @override
  String get loaderGameVersionLabel => 'Game version';

  @override
  String get loaderVersionLabel => 'Loader version';

  @override
  String get loaderInstallerLabel => 'Installer jar';

  @override
  String get loaderChooseInstaller => 'Choose installer';

  @override
  String get loaderInstallAction => 'Download and commit';

  @override
  String get loaderInstalling => 'Downloading and committing…';

  @override
  String get loaderInstallDone =>
      'Loader files downloaded, version JSON written, and processors completed.';

  @override
  String get loaderInstallPickInstaller =>
      'Choose a Forge/NeoForge installer jar first.';

  @override
  String get loaderInstallFailed => 'Loader install failed.';

  @override
  String get searchDownload => 'Search resources';

  @override
  String get downloadSearchAction => 'Search';

  @override
  String get downloadSearchEmpty => 'No compatible projects found.';

  @override
  String get downloadSearchError => 'Search failed';

  @override
  String get downloadInstall => 'Install';

  @override
  String get downloadInstalling => 'Downloading…';

  @override
  String get downloadInstalled => 'Installed';

  @override
  String get downloadSelectVersion => 'Select version';

  @override
  String get downloadNoVersions => 'No compatible versions.';

  @override
  String downloadResult(String status) {
    return 'Result: $status';
  }

  @override
  String get downloadRootLabel => 'Install folder';

  @override
  String get commonClose => 'Close';

  @override
  String get comingSoon => 'Coming soon';

  @override
  String get versionStable => 'Stable';

  @override
  String get versionSnapshot => 'Snapshot';

  @override
  String get accountsTitle => 'Accounts';

  @override
  String get accountsSubtitle =>
      'Microsoft, offline, and third-party authlib accounts.';

  @override
  String get currentAccount => 'Current account';

  @override
  String get addAccount => 'Add account';

  @override
  String get accountMicrosoftDesc =>
      'Sign in with your Microsoft account in the browser.';

  @override
  String get accountOfflineDesc => 'Local profile for single-player and LAN.';

  @override
  String get accountThirdPartyDesc => 'Yggdrasil via authlib-injector.';

  @override
  String get refresh => 'Refresh';

  @override
  String get logout => 'Sign out';

  @override
  String get settingsTitle => 'Settings';

  @override
  String get settingsSubtitle => 'Appearance, storage, Java, and about.';

  @override
  String get settingsGeneral => 'General';

  @override
  String get settingsAppearance => 'Appearance';

  @override
  String get settingsStorage => 'Storage';

  @override
  String get settingsInstancesRoot => 'Instances folder';

  @override
  String get settingsInstanceRoots => 'Instance storage';

  @override
  String get settingsAddRoot => 'Add folder';

  @override
  String get settingsDefaultRoot => 'Default';

  @override
  String get settingsMakeDefault => 'Set as default';

  @override
  String get settingsRemoveRoot => 'Remove folder';

  @override
  String get settingsRootsUpdated => 'Instance folders updated.';

  @override
  String get settingsDataRoot => 'Application data';

  @override
  String get settingsChooseFolder => 'Choose folder';

  @override
  String get settingsSaved => 'Saved';

  @override
  String get settingsJava => 'Java';

  @override
  String get settingsJavaAuto => 'Auto select';

  @override
  String get settingsJavaAutoHint =>
      'Choose a Java runtime that matches the game version.';

  @override
  String get settingsJavaCurrent => 'Current Java';

  @override
  String get settingsJavaAutoValue => 'Automatic (per game version)';

  @override
  String get settingsJavaUseAuto => 'Use automatic selection';

  @override
  String get settingsJavaSelected => 'Java selection saved.';

  @override
  String get settingsMemory => 'Memory';

  @override
  String get settingsJvmArgs => 'JVM arguments';

  @override
  String get settingsRuntimeHint =>
      'Runtime options apply to the next launch and save automatically.';

  @override
  String get settingsMemoryMin => 'Minimum memory (MB)';

  @override
  String get settingsMemoryMax => 'Maximum memory (MB)';

  @override
  String get settingsJvmArgsHint => 'Optional, space-separated';

  @override
  String get settingsGc => 'Garbage collector';

  @override
  String get settingsGcAuto => 'Automatic';

  @override
  String get settingsGcG1 => 'G1GC';

  @override
  String get settingsGcZgc => 'ZGC (Java 21+)';

  @override
  String get settingsGcParallel => 'Parallel';

  @override
  String get settingsGcSerial => 'Serial';

  @override
  String get settingsGcShenandoah => 'Shenandoah';

  @override
  String get settingsGcEpsilon => 'Epsilon (testing)';

  @override
  String get settingsGameArgs => 'Additional game arguments';

  @override
  String get settingsGameArgsHint =>
      'Optional, appended after the version arguments';

  @override
  String get settingsMemoryInvalid =>
      'Memory must be 128–65536 MB and min ≤ max.';

  @override
  String get settingsAbout => 'About';

  @override
  String get settingsTheme => 'Theme';

  @override
  String get settingsThemeSystem => 'Follow system';

  @override
  String get settingsLanguage => 'Language';

  @override
  String get settingsMotion => 'Reduce motion';

  @override
  String get settingsMotionDesc =>
      'Limit page transitions and background effects.';

  @override
  String get settingsDiagnostics => 'Diagnostics';

  @override
  String get settingsDiagnosticsDesc =>
      'Logs, module versions, and crash reports.';

  @override
  String get settingsVersion => 'Version';

  @override
  String get settingsLicense => 'License';

  @override
  String get settingsSource => 'Source code';

  @override
  String get javaStatusTitle => 'Java';

  @override
  String get javaStatusIdle => 'Idle';

  @override
  String get javaStatusScanning => 'Scanning';

  @override
  String get javaStatusReady => 'Ready';

  @override
  String get javaStatusUnavailable => 'Unavailable';

  @override
  String get javaStatusIdleHint =>
      'Scan your system for installed Java runtimes.';

  @override
  String get javaStatusScanningHint => 'Calling java.scan…';

  @override
  String javaStatusProgress(int done, int total) {
    return 'Progress: $done/$total';
  }

  @override
  String get javaStatusUnavailableHint =>
      'Java scanning is unavailable. The business module may not be built yet.';

  @override
  String get javaStatusScan => 'Scan Java';

  @override
  String get javaStatusSelect => 'Use this Java';

  @override
  String javaStatusCount(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count Java runtimes found',
      one: '1 Java runtime found',
      zero: 'No Java runtimes found',
    );
    return '$_temp0';
  }

  @override
  String javaStatusMeta(String version, String arch) {
    return '$version · $arch';
  }

  @override
  String javaStatusScanFailed(String message) {
    return 'Scan failed: $message';
  }

  @override
  String get javaStatusEmptyHint =>
      'Install a JDK or set JAVA_HOME, then scan again.';

  @override
  String get javaStatusNoResultsTitle => 'No Java found';

  @override
  String get javaStatusNoResultsHint =>
      'Java was not found in the default locations. Search a folder or select the java executable.';

  @override
  String get javaStatusSearchFolder => 'Search a folder';

  @override
  String get javaStatusSelectExecutable => 'Select java executable';

  @override
  String get javaStatusAddFolder => 'Add folder';

  @override
  String get javaStatusStillNotFound =>
      'Still not found — select the java executable.';

  @override
  String javaStatusNoValidExecutable(String path) {
    return 'No valid Java at this path: $path';
  }

  @override
  String get javaStatusNoValidExecutableHint =>
      'Choose the bin/java file inside a JDK (bin\\java.exe on Windows).';

  @override
  String get javaStatusCustomPaths => 'Custom paths';

  @override
  String javaStatusRemovePath(String path) {
    return 'Remove $path';
  }

  @override
  String get homeFootnote =>
      'ArchoeraCX is not an official Minecraft product. Not approved by or associated with Mojang or Microsoft.';

  @override
  String javaStatusViewAll(int count) {
    return 'View all $count';
  }

  @override
  String javaStatusAllTitle(int count) {
    return 'Java runtimes ($count)';
  }

  @override
  String get close => 'Close';

  @override
  String get javaStatusSelectJdkHome => 'Select JDK home';

  @override
  String get accountMicrosoftTitle => 'Microsoft sign-in';

  @override
  String get accountDeviceCodeTitle => 'Enter this code';

  @override
  String accountDeviceCodeHint(String uri) {
    return 'Open $uri in a browser and enter the code below.';
  }

  @override
  String get accountDeviceCodeWaiting => 'Waiting for Microsoft authorization…';

  @override
  String get accountDeviceCodeCopied => 'Code copied.';

  @override
  String accountDeviceCodeExpires(int minutes) {
    return 'The code expires in about $minutes minutes.';
  }

  @override
  String get accountBrowserSignInHint =>
      'Your browser has been opened. Sign in with your Microsoft account to continue.';

  @override
  String get accountBrowserWaitingCallback =>
      'Waiting for the browser callback…';

  @override
  String get accountOpenBrowser => 'Open browser';

  @override
  String get accountBrowserOpenFailed =>
      'Could not open the browser automatically. Open the link below manually.';

  @override
  String get accountAddOffline => 'Add offline account';

  @override
  String get accountOfflineNameLabel => 'Player name';

  @override
  String get accountOfflineNameHint => '3–16 characters';

  @override
  String get accountRemove => 'Remove account';

  @override
  String accountRemoveConfirm(String name) {
    return 'Remove “$name”? The game files are not deleted.';
  }

  @override
  String get accountRefresh => 'Refresh session';

  @override
  String get accountNoAccounts =>
      'No accounts yet. Add a Microsoft or offline account to launch the game.';

  @override
  String accountLoginFailed(String message) {
    return 'Sign-in failed: $message';
  }

  @override
  String get accountWaitingAuthorization => 'Waiting for authorization';

  @override
  String get accountSignIn => 'Sign in';

  @override
  String get accountCancelSignIn => 'Cancel sign-in';

  @override
  String get accountSessionRefreshed => 'Session refreshed.';

  @override
  String get accountRemoved => 'Account removed.';

  @override
  String get accountSelected => 'Current account changed.';

  @override
  String get commonCopy => 'Copy';

  @override
  String get commonAdd => 'Add';

  @override
  String get instanceSelected => 'Selected';

  @override
  String get loaderVanilla => 'Vanilla';

  @override
  String get loaderFabric => 'Fabric';

  @override
  String get loaderQuilt => 'Quilt';

  @override
  String get loaderForge => 'Forge';

  @override
  String get loaderNeoForge => 'NeoForge';

  @override
  String loaderVersionValue(String loader, String version) {
    return '$loader $version';
  }

  @override
  String get accountMicrosoft => 'Microsoft';

  @override
  String get accountAuthlib => 'Authlib';

  @override
  String get settingsDiagnosticsOpen => 'Open';

  @override
  String get settingsLanguageSystem => 'Follow system';

  @override
  String get settingsLanguageEnglish => 'English';

  @override
  String get settingsLanguageChinese => '简体中文';

  @override
  String get settingsLanguageChanged => 'Language changed.';

  @override
  String get resourceVersionRelease => 'Release';

  @override
  String get resourceVersionBeta => 'Beta';

  @override
  String get resourceVersionAlpha => 'Alpha';

  @override
  String get javaStatusUnknownVersion => 'Unknown';

  @override
  String instanceCreateSummary(String loader, String version) {
    return '$loader · Minecraft $version';
  }

  @override
  String resourceTargetGame(String version) {
    return 'Minecraft $version';
  }

  @override
  String resourceTargetGameLoader(String version, String loader) {
    return 'Minecraft $version · $loader';
  }

  @override
  String resourceTargetShader(String version) {
    return 'Minecraft $version · Iris';
  }

  @override
  String get loaderProcessorsDone =>
      'Forge/NeoForge processors completed with the selected Java.';

  @override
  String instancesCount(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count instances',
      one: '1 instance',
      zero: 'No instances',
    );
    return '$_temp0';
  }

  @override
  String instancesLoadFailed(String message) {
    return 'Could not load instances: $message';
  }

  @override
  String accountsLoadFailed(String message) {
    return 'Could not load accounts: $message';
  }

  @override
  String get downloadGlobalTarget => 'All versions · all loaders';

  @override
  String get instanceManage => 'Manage';

  @override
  String get instanceManageLoader => 'Install / update loader';

  @override
  String get instanceManageMods => 'Manage mods';

  @override
  String get instanceModsTitle => 'Installed mods';

  @override
  String get instanceModsEmpty => 'No mods in this instance.';

  @override
  String instanceModsLoadFailed(String message) {
    return 'Could not load mods: $message';
  }

  @override
  String get instanceModDelete => 'Delete mod';

  @override
  String instanceModDeleteConfirm(String name) {
    return 'Delete “$name”? This removes the mod file.';
  }

  @override
  String get instanceModDisabled => 'Disabled';

  @override
  String get instanceModsToggleFailed => 'Could not update the mod.';

  @override
  String get instanceModsRemoveFailed => 'Could not remove the mod.';

  @override
  String get accountDefaultSkin => 'Default appearance';

  @override
  String get settingsMemorySystem => 'System memory';

  @override
  String get settingsMemoryTotal => 'Total';

  @override
  String get settingsMemoryAvailable => 'Available';

  @override
  String get settingsMemoryEstimated => 'After launch';

  @override
  String get settingsMemoryUnavailable => 'Unavailable';

  @override
  String get settingsMemoryOverAvailable =>
      'Estimated usage exceeds available memory.';

  @override
  String downloadResults(num count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count results',
      one: '1 result',
      zero: 'No results',
    );
    return '$_temp0';
  }

  @override
  String get downloadSort => 'Sort';

  @override
  String get downloadSortRelevance => 'Relevance';

  @override
  String get downloadSortDownloads => 'Downloads';

  @override
  String get downloadSortFollows => 'Follows';

  @override
  String get downloadSortNewest => 'Newest';

  @override
  String get downloadSortUpdated => 'Recently updated';

  @override
  String get downloadScreenshots => 'Screenshots';

  @override
  String downloadDependencies(num count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count dependencies',
      one: '1 dependency',
      zero: 'No dependencies',
    );
    return '$_temp0';
  }

  @override
  String get downloadLoadMore => 'Load more';

  @override
  String get downloadImagePreview => 'Image preview';

  @override
  String downloadByAuthor(Object author) {
    return 'by $author';
  }

  @override
  String get loaderVersionSelect => 'Select loader version';

  @override
  String get loaderVersionSearch => 'Search versions';

  @override
  String get loaderVersionLatest => 'Latest';

  @override
  String get loaderVersionRecommended => 'Recommended';

  @override
  String get loaderVersionBeta => 'Beta';

  @override
  String get loaderVersionLoadFailed => 'Could not load loader versions.';

  @override
  String get loaderVersionEmpty =>
      'No loader versions were found for this game version.';

  @override
  String get downloadDescription => 'Description';

  @override
  String get instanceInstallGame => 'Download game files';

  @override
  String get instanceInstallGameTitle => 'Install game files';

  @override
  String get instanceInstallGameHint =>
      'Downloads the vanilla client, libraries and assets, then writes the version JSON used by launch.';

  @override
  String get instanceInstallGameRunning => 'Downloading game files…';

  @override
  String get instanceInstallGameDone => 'Game files are ready.';

  @override
  String get instanceInstallGameFailed => 'Game file installation failed.';

  @override
  String get commonRetry => 'Retry';

  @override
  String get taskCenterTitle => 'Tasks';

  @override
  String get taskCenterEmpty => 'No active tasks.';

  @override
  String taskCenterActive(Object count) {
    return '$count active';
  }

  @override
  String get taskCenterCompleted => 'Done';

  @override
  String get taskCenterFailed => 'Failed';

  @override
  String get taskCenterCancelled => 'Cancelled';

  @override
  String get taskCenterCancel => 'Cancel';

  @override
  String get taskCenterClear => 'Clear completed';

  @override
  String get taskCenterDownload => 'Download';

  @override
  String get taskCenterLoader => 'Loader';

  @override
  String get taskCenterModpack => 'Modpack';

  @override
  String get taskCenterGameFiles => 'Game files';

  @override
  String get taskCenterJavaScan => 'Java scan';

  @override
  String get taskCenterTask => 'Task';

  @override
  String taskCenterSpeed(Object rate) {
    return '$rate/s';
  }

  @override
  String get taskCenterWorking => 'Working';

  @override
  String get resourceNeedsLoader =>
      'Select a Fabric/Forge/NeoForge/Quilt instance before installing mods.';

  @override
  String get instanceVersionIsolation => 'Version isolation';

  @override
  String get instanceVersionIsolationHint =>
      'The game data lives in versions/<name>; libraries and assets are shared with the parent game folder.';

  @override
  String get accountPremiumRequired =>
      'Sign in with a Microsoft account that owns Minecraft first.';

  @override
  String get accountPremiumRequiredHint =>
      'A verified Microsoft account is required before adding or selecting other accounts.';

  @override
  String get downloadUnavailableTitle =>
      'Downloads are disabled in this region';

  @override
  String get downloadUnavailableMessage =>
      'Downloads are disabled by the local region policy for the detected mainland-China network. Add the required fields to the configuration file, then re-check. Existing local instances and launch features remain available.';

  @override
  String downloadConfigPath(Object path) {
    return 'Configuration file: $path';
  }

  @override
  String get downloadConfigFields =>
      'Required fields: developerMode=true, acceptRisk=true, downloadLevel=L2, acknowledgement=ENABLE-DOWNLOADS 1';

  @override
  String get downloadConfigReload => 'Re-check configuration';

  @override
  String get accountMicrosoftOnly =>
      'Only a verified Microsoft account is allowed in this region.';

  @override
  String get accountMicrosoftOnlyHint =>
      'This region only allows Microsoft-verified accounts. Offline and third-party accounts are disabled.';

  @override
  String get accountsSubtitleMicrosoftOnly =>
      'Microsoft-verified accounts only in this region.';

  @override
  String get accountNoAccountsMicrosoftOnly =>
      'Add a Microsoft account to get started.';

  @override
  String get launchOnlineNotReady =>
      'Online launch is not enabled in this build yet.';

  @override
  String get developmentBuildBadge => 'DEV';

  @override
  String get developmentBuildWarning =>
      'Development build — incomplete and may corrupt data. Do not use it for production or important instances.';

  @override
  String get launchOnlineSessionExpired =>
      'Your Minecraft session expired. Sign in again.';

  @override
  String get launchMicrosoftAccountRequired =>
      'Select a Microsoft account to launch online.';

  @override
  String get launchOnlineNotConfigured =>
      'Online launch is not configured in this build.';

  @override
  String get accountVerifiedRequiredNotice =>
      'This launcher requires a verified Microsoft account to start a game. You can still download content and manage instance files, but offline/local accounts stay locked until a verified Minecraft account is present.';

  @override
  String get accountLoginSuccessNotificationTitle => 'ArchoeraCX signed in';

  @override
  String accountLoginSuccessNotificationBody(String account) {
    return 'Signed in as $account.';
  }

  @override
  String get settingsAppearanceNote =>
      'Theme, accent color and interface effects apply immediately.';

  @override
  String get settingsAppearanceLanguage => 'Language & region';

  @override
  String get settingsDensity => 'Interface density';

  @override
  String get settingsDensityDesc => 'Adjust control spacing and list height.';

  @override
  String get settingsDensityCompact => 'Compact';

  @override
  String get settingsDensityStandard => 'Standard';

  @override
  String get settingsDensityComfortable => 'Comfortable';

  @override
  String get settingsThemeMode => 'Color mode';

  @override
  String get settingsThemeModeDesc =>
      'Use light, dark, or match the operating system.';

  @override
  String get settingsThemeLight => 'Light';

  @override
  String get settingsThemeDark => 'Dark';

  @override
  String get settingsThemeAccent => 'Accent color';

  @override
  String get settingsThemeAccentDesc =>
      'Use the system accent, or pick a color for the launcher.';

  @override
  String get settingsThemeAccentSystem => 'System';

  @override
  String get settingsThemeAccentCustom => 'Custom';

  @override
  String get settingsThemeAccentCustomHint =>
      'Enter a hex color such as #4F7CFF.';

  @override
  String get settingsThemeAccentInvalid => 'Enter a valid hex color.';

  @override
  String get settingsGlobalTint => 'Global tint';

  @override
  String get settingsGlobalTintDesc =>
      'Shift panels and backgrounds slightly toward the accent color.';

  @override
  String get settingsResetAppearance => 'Reset appearance';

  @override
  String get settingsAppearanceResetDone => 'Appearance reset.';

  @override
  String get settingsAboutDesc =>
      'Version, runtime, licenses and community links.';

  @override
  String get settingsAboutTagline =>
      'Cross-platform Minecraft: Java Edition launcher';

  @override
  String get settingsAboutEnvironment => 'Environment';

  @override
  String get settingsAboutBuildInfo => 'Build';

  @override
  String get settingsAboutPlatform => 'Platform';

  @override
  String get settingsAboutRuntime => 'Runtime';

  @override
  String get settingsAboutCore => 'Native core';

  @override
  String get settingsAboutCoreUnknown => 'Not connected';

  @override
  String get settingsAboutCopyEnvironment => 'Copy environment';

  @override
  String get settingsAboutCopied => 'Environment copied to the clipboard.';

  @override
  String get settingsAboutCommunity => 'Community & support';

  @override
  String get settingsAboutRepository => 'Source code';

  @override
  String get settingsAboutIssues => 'Report an issue';

  @override
  String get settingsAboutThanks => 'Thanks';

  @override
  String get settingsAboutThanksDesc =>
      'Open-source projects and services this build relies on.';

  @override
  String get settingsAboutLegal => 'Legal';

  @override
  String get settingsAboutPrivacy => 'Privacy policy';

  @override
  String get settingsAboutSecurity => 'Security policy';

  @override
  String get settingsAboutThirdParty => 'Third-party licenses';

  @override
  String get settingsAboutThirdPartyDesc =>
      'Open-source packages bundled with this build.';

  @override
  String get settingsAboutDisclaimer =>
      'NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.';

  @override
  String get settingsAboutOpenFailed => 'Could not open the link.';

  @override
  String get instanceViewLogs => 'View logs';

  @override
  String get gameLogTitle => 'Game log';

  @override
  String get gameLogEmpty =>
      'No log yet. Launch this instance once and its output will appear here.';

  @override
  String get gameLogNoOutput => 'This log is empty.';

  @override
  String get gameLogLoadFailed => 'Could not read the game log.';

  @override
  String get gameLogRunning => 'Running';

  @override
  String get gameLogStopped => 'Stopped';

  @override
  String get gameLogFollow => 'Follow';

  @override
  String get gameLogFollowHint => 'Stream new lines while the game is running.';

  @override
  String get gameLogCopy => 'Copy all';

  @override
  String get gameLogCopied => 'Log copied to the clipboard.';

  @override
  String get gameLogFileLabel => 'Log file';

  @override
  String gameLogTruncated(int count) {
    return 'Showing the last $count lines.';
  }

  @override
  String gameLogRetentionInfo(String size, int max) {
    return '$size of logs on disk; only the latest $max launches are kept.';
  }

  @override
  String get taskCenterLaunch => 'Launch';

  @override
  String taskCenterTaskWithTarget(String category, String target) {
    return '$category · $target';
  }

  @override
  String get navWiki => 'Wiki';

  @override
  String get wikiSubtitle => 'Minecraft community news and machine load.';

  @override
  String get wikiSystemTitle => 'System load';

  @override
  String get wikiFeedTitle => 'Community';

  @override
  String get wikiFeedSubtitle => 'Official news, Java versions and wiki edits.';

  @override
  String get wikiRefresh => 'Refresh';

  @override
  String wikiUpdatedAt(String time) {
    return 'Updated $time';
  }

  @override
  String get wikiSourceNews => 'Official news';

  @override
  String get wikiSourceJava => 'Java versions';

  @override
  String get wikiSourceWiki => 'Wiki edits';

  @override
  String get wikiKindNews => 'News';

  @override
  String get wikiKindRelease => 'Release';

  @override
  String get wikiKindSnapshot => 'Snapshot';

  @override
  String get wikiKindEdit => 'Edit';

  @override
  String get wikiKindNew => 'New page';

  @override
  String get wikiFeedEmpty => 'Nothing to show right now.';

  @override
  String get wikiFeedFailed => 'Could not load the community feed';

  @override
  String get wikiStatusFailed => 'Could not read system load';

  @override
  String get wikiUnavailable => 'Unavailable';

  @override
  String get wikiCpuLabel => 'CPU';

  @override
  String get wikiMemoryLabel => 'Memory';

  @override
  String get wikiDiskLabel => 'Disk';

  @override
  String get wikiLauncherLabel => 'Launcher';

  @override
  String wikiCoresCount(int count) {
    return '$count threads';
  }

  @override
  String wikiLoadAverage(String value) {
    return 'load $value';
  }

  @override
  String wikiMemoryUsage(String used, String total) {
    return '$used / $total GB';
  }

  @override
  String wikiDiskFree(String free) {
    return '$free free';
  }

  @override
  String wikiUptime(String duration) {
    return 'up $duration';
  }

  @override
  String wikiSystemUptime(String duration) {
    return 'System up $duration';
  }

  @override
  String get wikiTimeJustNow => 'just now';

  @override
  String wikiTimeMinutesAgo(int count) {
    return '$count min ago';
  }

  @override
  String wikiTimeHoursAgo(int count) {
    return '$count h ago';
  }

  @override
  String wikiTimeDaysAgo(int count) {
    return '$count d ago';
  }

  @override
  String get wikiOpenLinkFailed => 'Could not open the link';

  @override
  String wikiSourceError(String error) {
    return 'Fetch failed: $error';
  }

  @override
  String get wikiRetry => 'Retry';

  @override
  String get wikiNoSourceData => 'No items from this source';

  @override
  String get wikiOpenInBrowser => 'Open in browser';

  @override
  String get javaSearchHint =>
      'Filter by version, vendor, architecture or path';

  @override
  String get javaSelectedBadge => 'In use';

  @override
  String get javaFilteredEmpty => 'No Java matches this filter.';

  @override
  String get back => 'Back';

  @override
  String get javaPageSubtitle =>
      'Every Java runtime found on this machine; pick the one used to launch games.';

  @override
  String get javaPageManage => 'Java runtimes';

  @override
  String get javaPageManageHint =>
      'Browse every scan result, add a folder or pick a java executable.';

  @override
  String get settingsSectionTheme => 'Theme';

  @override
  String get settingsSectionAccent => 'Accent';

  @override
  String get settingsSectionInterface => 'Interface';

  @override
  String get settingsThemeAccentSystemDesc => 'Use the desktop accent color.';

  @override
  String get settingsThemeAccentCustomDesc =>
      'Pick a single accent color for the launcher.';

  @override
  String get wikiSystemLive => 'Updates every 5 s';

  @override
  String get loaderPickerVanillaHint =>
      'No mod loader; the instance installs the vanilla game.';

  @override
  String loaderVersionCount(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count builds',
      one: '1 build',
      zero: 'No builds',
    );
    return '$_temp0';
  }

  @override
  String get loaderVersionNoMatch => 'No builds match this search.';
}
