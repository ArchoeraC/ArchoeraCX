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
  String get navTasks => 'Tasks';

  @override
  String get navNotifications => 'Notifications';

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
  String get commonNoTasks => 'No active tasks.';

  @override
  String get commonNoNotifications => 'No notifications.';

  @override
  String get dashboardTitle => 'Dashboard';

  @override
  String get dashboardSubtitle =>
      'A calm place to launch, install, and manage your worlds.';

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
  String get instanceLaunchNotReady => 'Launch flow is not wired yet.';

  @override
  String get instanceCreated => 'Instance created.';

  @override
  String get commonCancel => 'Cancel';

  @override
  String get commonCreate => 'Create';

  @override
  String get commonDelete => 'Delete';

  @override
  String get downloadTitle => 'Download';

  @override
  String get downloadSubtitle =>
      'Versions, loaders, mods, packs, resource packs, and shaders.';

  @override
  String get tabVersions => 'Game versions';

  @override
  String get tabLoaders => 'Loaders';

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
  String get accountMicrosoftDesc => 'Sign in with Microsoft device code.';

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
  String get settingsSubtitle =>
      'Appearance, Java, network, compliance, and diagnostics.';

  @override
  String get settingsGeneral => 'General';

  @override
  String get settingsAppearance => 'Appearance';

  @override
  String get settingsStorage => 'Storage';

  @override
  String get settingsInstancesRoot => 'Instances folder';

  @override
  String get settingsDataRoot => 'Application data';

  @override
  String get settingsChooseFolder => 'Choose folder';

  @override
  String get settingsSaved => 'Saved';

  @override
  String get settingsJava => 'Java';

  @override
  String get settingsNetwork => 'Network';

  @override
  String get settingsCompliance => 'Compliance';

  @override
  String get settingsAbout => 'About';

  @override
  String get settingsTheme => 'Theme';

  @override
  String get settingsThemeSystem => 'Follow system';

  @override
  String get settingsLanguage => 'Language';

  @override
  String get settingsLanguageValue => 'English / 简体中文';

  @override
  String get settingsMotion => 'Motion';

  @override
  String get settingsMotionDesc => 'Reduce transitions and background effects.';

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
  String get coreStatusTitle => 'Core status';

  @override
  String get coreStatusIdle => 'Idle';

  @override
  String get coreStatusConnecting => 'Pinging';

  @override
  String get coreStatusConnected => 'Connected';

  @override
  String get coreStatusUnavailable => 'Core unavailable';

  @override
  String get coreStatusIdleHint =>
      'The native core is loaded. Ping it to verify the ABI.';

  @override
  String get coreStatusPingingHint => 'Calling system.pingAsync…';

  @override
  String coreStatusProgress(int done, int total) {
    return 'Progress: $done/$total';
  }

  @override
  String get coreStatusUnavailableHint =>
      'The native core library was not found or could not be loaded.';

  @override
  String get coreStatusPing => 'Ping core';

  @override
  String coreStatusPong(String value) {
    return 'pong: $value';
  }

  @override
  String coreStatusMethod(String method) {
    return 'Method: $method';
  }

  @override
  String coreStatusAbiVersion(int version) {
    return 'ABI v$version';
  }

  @override
  String coreStatusBuildInfo(String build) {
    return 'Build: $build';
  }

  @override
  String coreStatusPingFailed(String message) {
    return 'Ping failed: $message';
  }

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
}
