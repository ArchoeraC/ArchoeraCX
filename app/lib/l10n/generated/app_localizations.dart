import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_en.dart';
import 'app_localizations_zh.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'generated/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
    : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations)!;
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
      ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('en'),
    Locale('zh'),
  ];

  /// Product name.
  ///
  /// In en, this message translates to:
  /// **'ArchoeraCX'**
  String get appName;

  /// Short product tagline.
  ///
  /// In en, this message translates to:
  /// **'Cross-platform Minecraft: Java Edition launcher'**
  String get appTagline;

  /// No description provided for @appPreview.
  ///
  /// In en, this message translates to:
  /// **'UI PREVIEW'**
  String get appPreview;

  /// No description provided for @navDashboard.
  ///
  /// In en, this message translates to:
  /// **'Dashboard'**
  String get navDashboard;

  /// No description provided for @navInstances.
  ///
  /// In en, this message translates to:
  /// **'Instances'**
  String get navInstances;

  /// No description provided for @navDownload.
  ///
  /// In en, this message translates to:
  /// **'Download'**
  String get navDownload;

  /// No description provided for @navAccounts.
  ///
  /// In en, this message translates to:
  /// **'Accounts'**
  String get navAccounts;

  /// No description provided for @navSettings.
  ///
  /// In en, this message translates to:
  /// **'Settings'**
  String get navSettings;

  /// No description provided for @coreOffline.
  ///
  /// In en, this message translates to:
  /// **'Core offline'**
  String get coreOffline;

  /// No description provided for @coreOfflineHint.
  ///
  /// In en, this message translates to:
  /// **'Native core is not connected yet.'**
  String get coreOfflineHint;

  /// No description provided for @accountOffline.
  ///
  /// In en, this message translates to:
  /// **'Offline'**
  String get accountOffline;

  /// No description provided for @accountLocal.
  ///
  /// In en, this message translates to:
  /// **'Local account'**
  String get accountLocal;

  /// No description provided for @launch.
  ///
  /// In en, this message translates to:
  /// **'Launch'**
  String get launch;

  /// No description provided for @selectInstance.
  ///
  /// In en, this message translates to:
  /// **'Select an instance first'**
  String get selectInstance;

  /// No description provided for @commonMockData.
  ///
  /// In en, this message translates to:
  /// **'Mock data · UI shell'**
  String get commonMockData;

  /// No description provided for @commonCoreNotConnected.
  ///
  /// In en, this message translates to:
  /// **'The native core is not connected yet.'**
  String get commonCoreNotConnected;

  /// No description provided for @dashboardTitle.
  ///
  /// In en, this message translates to:
  /// **'Dashboard'**
  String get dashboardTitle;

  /// No description provided for @dashboardSubtitle.
  ///
  /// In en, this message translates to:
  /// **'A calm place to launch, install, and manage your worlds.'**
  String get dashboardSubtitle;

  /// No description provided for @dashboardWorkspace.
  ///
  /// In en, this message translates to:
  /// **'Workspace'**
  String get dashboardWorkspace;

  /// No description provided for @dashboardQuickActions.
  ///
  /// In en, this message translates to:
  /// **'Quick actions'**
  String get dashboardQuickActions;

  /// No description provided for @dashboardBrowseContent.
  ///
  /// In en, this message translates to:
  /// **'Browse content'**
  String get dashboardBrowseContent;

  /// No description provided for @dashboardEnvironment.
  ///
  /// In en, this message translates to:
  /// **'Environment'**
  String get dashboardEnvironment;

  /// No description provided for @quickLaunch.
  ///
  /// In en, this message translates to:
  /// **'Quick launch'**
  String get quickLaunch;

  /// No description provided for @quickLaunchSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Your most recent instance is ready.'**
  String get quickLaunchSubtitle;

  /// No description provided for @recentInstances.
  ///
  /// In en, this message translates to:
  /// **'Recent instances'**
  String get recentInstances;

  /// No description provided for @viewAll.
  ///
  /// In en, this message translates to:
  /// **'View all'**
  String get viewAll;

  /// No description provided for @statInstances.
  ///
  /// In en, this message translates to:
  /// **'Instances'**
  String get statInstances;

  /// No description provided for @statJava.
  ///
  /// In en, this message translates to:
  /// **'Java'**
  String get statJava;

  /// No description provided for @statDownloads.
  ///
  /// In en, this message translates to:
  /// **'Downloads'**
  String get statDownloads;

  /// No description provided for @statAccounts.
  ///
  /// In en, this message translates to:
  /// **'Accounts'**
  String get statAccounts;

  /// No description provided for @instanceName1.
  ///
  /// In en, this message translates to:
  /// **'1.21.4 · Fabric'**
  String get instanceName1;

  /// No description provided for @instanceMeta1.
  ///
  /// In en, this message translates to:
  /// **'Fabric 0.16.10 · 12 mods'**
  String get instanceMeta1;

  /// No description provided for @instanceName2.
  ///
  /// In en, this message translates to:
  /// **'1.20.1 · Forge'**
  String get instanceName2;

  /// No description provided for @instanceMeta2.
  ///
  /// In en, this message translates to:
  /// **'Forge 47.2.0 · 8 mods'**
  String get instanceMeta2;

  /// No description provided for @lastPlayedToday.
  ///
  /// In en, this message translates to:
  /// **'Played today'**
  String get lastPlayedToday;

  /// No description provided for @lastPlayedYesterday.
  ///
  /// In en, this message translates to:
  /// **'Played yesterday'**
  String get lastPlayedYesterday;

  /// No description provided for @instancesTitle.
  ///
  /// In en, this message translates to:
  /// **'Instances'**
  String get instancesTitle;

  /// No description provided for @instancesSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Isolated game directories with shared libraries and assets.'**
  String get instancesSubtitle;

  /// No description provided for @searchInstances.
  ///
  /// In en, this message translates to:
  /// **'Search instances'**
  String get searchInstances;

  /// No description provided for @filterAll.
  ///
  /// In en, this message translates to:
  /// **'All'**
  String get filterAll;

  /// No description provided for @createInstance.
  ///
  /// In en, this message translates to:
  /// **'Create instance'**
  String get createInstance;

  /// No description provided for @importPack.
  ///
  /// In en, this message translates to:
  /// **'Import pack'**
  String get importPack;

  /// Progress dialog title for Modrinth .mrpack installation.
  ///
  /// In en, this message translates to:
  /// **'Install modpack'**
  String get packInstallTitle;

  /// Dialog title when installing a local .mrpack file.
  ///
  /// In en, this message translates to:
  /// **'Import modpack'**
  String get packImportTitle;

  /// Explains that pack installation creates a new isolated instance.
  ///
  /// In en, this message translates to:
  /// **'A new instance will be created. Minecraft and the pack loader are installed from the pack dependencies, then bundled overrides are merged.'**
  String get packInstallHint;

  /// Progress dialog status while a pack installs.
  ///
  /// In en, this message translates to:
  /// **'Installing modpack…'**
  String get packInstallRunning;

  /// Progress dialog success status.
  ///
  /// In en, this message translates to:
  /// **'Modpack installed.'**
  String get packInstallDone;

  /// Progress dialog failure status.
  ///
  /// In en, this message translates to:
  /// **'Modpack installation failed.'**
  String get packInstallFailed;

  /// Pack details line: Minecraft version and loader build.
  ///
  /// In en, this message translates to:
  /// **'{game} · {loader} {loaderVersion}'**
  String packInspectMeta(String game, String loader, String loaderVersion);

  /// Pack details line: download count and bundled override count.
  ///
  /// In en, this message translates to:
  /// **'{count} index files · {overrides} bundled overrides'**
  String packInspectFiles(int count, int overrides);

  /// shown when a local .mrpack cannot be parsed.
  ///
  /// In en, this message translates to:
  /// **'This file is not a valid Modrinth modpack.'**
  String get packInspectFailed;

  /// Shown for Forge/NeoForge modpack dependencies.
  ///
  /// In en, this message translates to:
  /// **'This pack loader is not supported. Supported loaders: vanilla, Fabric, Quilt, Forge and NeoForge.'**
  String get packLoaderUnsupported;

  /// Shown when Forge/NeoForge processors have no usable Java.
  ///
  /// In en, this message translates to:
  /// **'This pack needs a Java runtime for its loader processors. Install or select a matching Java in Settings first.'**
  String get packJavaMissing;

  /// Shown while a modpack task is being cancelled.
  ///
  /// In en, this message translates to:
  /// **'Cancelling…'**
  String get packCancelling;

  /// Shown after a modpack task is cancelled.
  ///
  /// In en, this message translates to:
  /// **'Modpack installation was cancelled.'**
  String get packCancelled;

  /// Modpack search header: no existing instance is used.
  ///
  /// In en, this message translates to:
  /// **'Creates a new instance'**
  String get packTargetNewInstance;

  /// Success summary after a pack install.
  ///
  /// In en, this message translates to:
  /// **'{downloaded} files downloaded · {bundled} bundled'**
  String packResultSummary(int downloaded, int bundled);

  /// No description provided for @instanceCreateTitle.
  ///
  /// In en, this message translates to:
  /// **'Create instance'**
  String get instanceCreateTitle;

  /// No description provided for @instanceNameLabel.
  ///
  /// In en, this message translates to:
  /// **'Name'**
  String get instanceNameLabel;

  /// No description provided for @instanceNameHint.
  ///
  /// In en, this message translates to:
  /// **'e.g. 1.21.4 · Fabric'**
  String get instanceNameHint;

  /// No description provided for @instanceGameVersionLabel.
  ///
  /// In en, this message translates to:
  /// **'Game version'**
  String get instanceGameVersionLabel;

  /// No description provided for @instanceLocationLabel.
  ///
  /// In en, this message translates to:
  /// **'Instance folder'**
  String get instanceLocationLabel;

  /// No description provided for @instanceChooseLocation.
  ///
  /// In en, this message translates to:
  /// **'Choose folder'**
  String get instanceChooseLocation;

  /// No description provided for @instanceLocationHint.
  ///
  /// In en, this message translates to:
  /// **'Leave blank to use the global instances folder'**
  String get instanceLocationHint;

  /// No description provided for @instanceRootLabel.
  ///
  /// In en, this message translates to:
  /// **'Storage location'**
  String get instanceRootLabel;

  /// No description provided for @instanceCustomRoot.
  ///
  /// In en, this message translates to:
  /// **'Custom folder'**
  String get instanceCustomRoot;

  /// No description provided for @instanceMissing.
  ///
  /// In en, this message translates to:
  /// **'Folder missing'**
  String get instanceMissing;

  /// No description provided for @instanceCustom.
  ///
  /// In en, this message translates to:
  /// **'Custom'**
  String get instanceCustom;

  /// No description provided for @instanceSyncDone.
  ///
  /// In en, this message translates to:
  /// **'Instance folders synced.'**
  String get instanceSyncDone;

  /// No description provided for @instanceSyncAdopted.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 instance indexed} other{{count} instances indexed}}'**
  String instanceSyncAdopted(int count);

  /// No description provided for @instanceSyncMissing.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 instance folder missing} other{{count} instance folders missing}}'**
  String instanceSyncMissing(int count);

  /// No description provided for @instanceLoaderLabel.
  ///
  /// In en, this message translates to:
  /// **'Loader'**
  String get instanceLoaderLabel;

  /// Mod count shown under an instance.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =0{No mods} =1{1 mod} other{{count} mods}}'**
  String instanceMods(int count);

  /// No description provided for @instanceLastPlayedNever.
  ///
  /// In en, this message translates to:
  /// **'Never played'**
  String get instanceLastPlayedNever;

  /// No description provided for @instanceEmptyTitle.
  ///
  /// In en, this message translates to:
  /// **'No instances yet'**
  String get instanceEmptyTitle;

  /// No description provided for @instanceEmptyMessage.
  ///
  /// In en, this message translates to:
  /// **'Create a vanilla or modded instance to get started.'**
  String get instanceEmptyMessage;

  /// No description provided for @instanceNoMatches.
  ///
  /// In en, this message translates to:
  /// **'No instances match your search.'**
  String get instanceNoMatches;

  /// No description provided for @instanceScanAction.
  ///
  /// In en, this message translates to:
  /// **'Scan instances'**
  String get instanceScanAction;

  /// No description provided for @instanceScanning.
  ///
  /// In en, this message translates to:
  /// **'Scanning instance folders…'**
  String get instanceScanning;

  /// No description provided for @instanceDelete.
  ///
  /// In en, this message translates to:
  /// **'Delete instance'**
  String get instanceDelete;

  /// Confirmation before removing an instance.
  ///
  /// In en, this message translates to:
  /// **'Delete “{name}”?'**
  String instanceDeleteConfirm(String name);

  /// No description provided for @instanceCheckQuick.
  ///
  /// In en, this message translates to:
  /// **'Quick check'**
  String get instanceCheckQuick;

  /// No description provided for @instanceCheckFull.
  ///
  /// In en, this message translates to:
  /// **'Full check'**
  String get instanceCheckFull;

  /// No description provided for @instanceCheckTitle.
  ///
  /// In en, this message translates to:
  /// **'Dependency check'**
  String get instanceCheckTitle;

  /// No description provided for @instanceCheckOk.
  ///
  /// In en, this message translates to:
  /// **'Dependencies are complete.'**
  String get instanceCheckOk;

  /// No description provided for @instanceCheckMissing.
  ///
  /// In en, this message translates to:
  /// **'Missing: {count}'**
  String instanceCheckMissing(int count);

  /// No description provided for @instanceCheckMismatched.
  ///
  /// In en, this message translates to:
  /// **'Mismatched: {count}'**
  String instanceCheckMismatched(int count);

  /// No description provided for @instanceCheckFailed.
  ///
  /// In en, this message translates to:
  /// **'Dependency check failed.'**
  String get instanceCheckFailed;

  /// Dependency check line for missing libraries/client/assets.
  ///
  /// In en, this message translates to:
  /// **'Launch files: {issue}'**
  String instanceCheckLaunchFiles(String issue);

  /// Button that restores missing loader files and reruns processors.
  ///
  /// In en, this message translates to:
  /// **'Repair dependencies'**
  String get instanceCheckRepair;

  /// Explains what the dependency repair action does.
  ///
  /// In en, this message translates to:
  /// **'Restores missing libraries/assets and reruns loader processors when needed.'**
  String get instanceCheckRepairHint;

  /// Progress text while repairing loader dependencies.
  ///
  /// In en, this message translates to:
  /// **'Repairing dependencies…'**
  String get instanceCheckRepairing;

  /// Success text after dependency repair.
  ///
  /// In en, this message translates to:
  /// **'Dependencies repaired.'**
  String get instanceCheckRepairDone;

  /// Failure text after dependency repair.
  ///
  /// In en, this message translates to:
  /// **'Dependency repair failed.'**
  String get instanceCheckRepairFailed;

  /// No description provided for @instanceManifestMissing.
  ///
  /// In en, this message translates to:
  /// **'Loader installer not found in this instance.'**
  String get instanceManifestMissing;

  /// No description provided for @instanceLaunchNotReady.
  ///
  /// In en, this message translates to:
  /// **'Launch flow is not wired yet.'**
  String get instanceLaunchNotReady;

  /// No description provided for @launchStarted.
  ///
  /// In en, this message translates to:
  /// **'Game started (PID {pid}).'**
  String launchStarted(int pid);

  /// No description provided for @launchFailed.
  ///
  /// In en, this message translates to:
  /// **'Launch failed.'**
  String get launchFailed;

  /// No description provided for @launchStarting.
  ///
  /// In en, this message translates to:
  /// **'Starting game…'**
  String get launchStarting;

  /// No description provided for @launchNoAccount.
  ///
  /// In en, this message translates to:
  /// **'Add an account before launching.'**
  String get launchNoAccount;

  /// No description provided for @launchOfflineOnly.
  ///
  /// In en, this message translates to:
  /// **'Basic launch supports offline accounts only.'**
  String get launchOfflineOnly;

  /// Launch error after a failed automatic Forge/NeoForge repair.
  ///
  /// In en, this message translates to:
  /// **'Launch files are missing and automatic repair failed. Reinstall the loader from the instance menu.'**
  String get launchAutoRepairFailed;

  /// Launch error when auto-repair needs downloads but the local policy blocks them.
  ///
  /// In en, this message translates to:
  /// **'Launch files are missing, but downloads are disabled. Enable downloads to repair them.'**
  String get launchAutoRepairBlocked;

  /// No description provided for @launchJavaMissing.
  ///
  /// In en, this message translates to:
  /// **'No suitable Java runtime found.'**
  String get launchJavaMissing;

  /// No description provided for @launchLog.
  ///
  /// In en, this message translates to:
  /// **'Log: {path}'**
  String launchLog(String path);

  /// No description provided for @instanceCreated.
  ///
  /// In en, this message translates to:
  /// **'Instance created.'**
  String get instanceCreated;

  /// No description provided for @commonCancel.
  ///
  /// In en, this message translates to:
  /// **'Cancel'**
  String get commonCancel;

  /// No description provided for @commonCreate.
  ///
  /// In en, this message translates to:
  /// **'Create'**
  String get commonCreate;

  /// No description provided for @commonDelete.
  ///
  /// In en, this message translates to:
  /// **'Delete'**
  String get commonDelete;

  /// No description provided for @commonSave.
  ///
  /// In en, this message translates to:
  /// **'Save'**
  String get commonSave;

  /// No description provided for @downloadTitle.
  ///
  /// In en, this message translates to:
  /// **'Download'**
  String get downloadTitle;

  /// Download hub subtitle.
  ///
  /// In en, this message translates to:
  /// **'Versions, mods, packs, resource packs, and shaders.'**
  String get downloadSubtitle;

  /// No description provided for @sourceLabel.
  ///
  /// In en, this message translates to:
  /// **'Source'**
  String get sourceLabel;

  /// No description provided for @tabVersions.
  ///
  /// In en, this message translates to:
  /// **'Game versions'**
  String get tabVersions;

  /// No description provided for @versionListTitle.
  ///
  /// In en, this message translates to:
  /// **'Minecraft versions'**
  String get versionListTitle;

  /// No description provided for @versionSearchHint.
  ///
  /// In en, this message translates to:
  /// **'Search versions'**
  String get versionSearchHint;

  /// No description provided for @versionIncludeSnapshots.
  ///
  /// In en, this message translates to:
  /// **'Include snapshots'**
  String get versionIncludeSnapshots;

  /// No description provided for @versionUse.
  ///
  /// In en, this message translates to:
  /// **'Use this version'**
  String get versionUse;

  /// No description provided for @versionLoadFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not load versions.'**
  String get versionLoadFailed;

  /// No description provided for @versionLoading.
  ///
  /// In en, this message translates to:
  /// **'Loading versions…'**
  String get versionLoading;

  /// No description provided for @versionEmpty.
  ///
  /// In en, this message translates to:
  /// **'No versions match.'**
  String get versionEmpty;

  /// No description provided for @tabMods.
  ///
  /// In en, this message translates to:
  /// **'Mods'**
  String get tabMods;

  /// No description provided for @tabPacks.
  ///
  /// In en, this message translates to:
  /// **'Modpacks'**
  String get tabPacks;

  /// No description provided for @tabResourcePacks.
  ///
  /// In en, this message translates to:
  /// **'Resource packs'**
  String get tabResourcePacks;

  /// No description provided for @tabShaders.
  ///
  /// In en, this message translates to:
  /// **'Shaders'**
  String get tabShaders;

  /// No description provided for @sourceModrinth.
  ///
  /// In en, this message translates to:
  /// **'Modrinth'**
  String get sourceModrinth;

  /// No description provided for @sourceCurseForge.
  ///
  /// In en, this message translates to:
  /// **'CurseForge'**
  String get sourceCurseForge;

  /// No description provided for @sourceCurseForgeKeyHint.
  ///
  /// In en, this message translates to:
  /// **'Add a CurseForge API key in Settings to use this source.'**
  String get sourceCurseForgeKeyHint;

  /// No description provided for @providerNotWired.
  ///
  /// In en, this message translates to:
  /// **'Provider wiring is next; this is the UI shell.'**
  String get providerNotWired;

  /// No description provided for @loaderInstallTitle.
  ///
  /// In en, this message translates to:
  /// **'Install Forge / NeoForge'**
  String get loaderInstallTitle;

  /// No description provided for @loaderKindLabel.
  ///
  /// In en, this message translates to:
  /// **'Loader'**
  String get loaderKindLabel;

  /// No description provided for @loaderGameVersionLabel.
  ///
  /// In en, this message translates to:
  /// **'Game version'**
  String get loaderGameVersionLabel;

  /// No description provided for @loaderVersionLabel.
  ///
  /// In en, this message translates to:
  /// **'Loader version'**
  String get loaderVersionLabel;

  /// No description provided for @loaderInstallerLabel.
  ///
  /// In en, this message translates to:
  /// **'Installer jar'**
  String get loaderInstallerLabel;

  /// No description provided for @loaderChooseInstaller.
  ///
  /// In en, this message translates to:
  /// **'Choose installer'**
  String get loaderChooseInstaller;

  /// No description provided for @loaderInstallAction.
  ///
  /// In en, this message translates to:
  /// **'Download and commit'**
  String get loaderInstallAction;

  /// No description provided for @loaderInstalling.
  ///
  /// In en, this message translates to:
  /// **'Downloading and committing…'**
  String get loaderInstalling;

  /// No description provided for @loaderInstallDone.
  ///
  /// In en, this message translates to:
  /// **'Loader files downloaded, version JSON written, and processors completed.'**
  String get loaderInstallDone;

  /// No description provided for @loaderInstallPickInstaller.
  ///
  /// In en, this message translates to:
  /// **'Choose a Forge/NeoForge installer jar first.'**
  String get loaderInstallPickInstaller;

  /// No description provided for @loaderInstallFailed.
  ///
  /// In en, this message translates to:
  /// **'Loader install failed.'**
  String get loaderInstallFailed;

  /// No description provided for @searchDownload.
  ///
  /// In en, this message translates to:
  /// **'Search resources'**
  String get searchDownload;

  /// No description provided for @downloadSearchAction.
  ///
  /// In en, this message translates to:
  /// **'Search'**
  String get downloadSearchAction;

  /// No description provided for @downloadSearchEmpty.
  ///
  /// In en, this message translates to:
  /// **'No compatible projects found.'**
  String get downloadSearchEmpty;

  /// No description provided for @downloadSearchError.
  ///
  /// In en, this message translates to:
  /// **'Search failed'**
  String get downloadSearchError;

  /// No description provided for @downloadInstall.
  ///
  /// In en, this message translates to:
  /// **'Install'**
  String get downloadInstall;

  /// No description provided for @downloadInstalling.
  ///
  /// In en, this message translates to:
  /// **'Downloading…'**
  String get downloadInstalling;

  /// No description provided for @downloadInstalled.
  ///
  /// In en, this message translates to:
  /// **'Installed'**
  String get downloadInstalled;

  /// No description provided for @downloadSelectVersion.
  ///
  /// In en, this message translates to:
  /// **'Select version'**
  String get downloadSelectVersion;

  /// No description provided for @downloadNoVersions.
  ///
  /// In en, this message translates to:
  /// **'No compatible versions.'**
  String get downloadNoVersions;

  /// Download result status.
  ///
  /// In en, this message translates to:
  /// **'Result: {status}'**
  String downloadResult(String status);

  /// No description provided for @downloadRootLabel.
  ///
  /// In en, this message translates to:
  /// **'Install folder'**
  String get downloadRootLabel;

  /// No description provided for @commonClose.
  ///
  /// In en, this message translates to:
  /// **'Close'**
  String get commonClose;

  /// No description provided for @comingSoon.
  ///
  /// In en, this message translates to:
  /// **'Coming soon'**
  String get comingSoon;

  /// No description provided for @versionStable.
  ///
  /// In en, this message translates to:
  /// **'Stable'**
  String get versionStable;

  /// No description provided for @versionSnapshot.
  ///
  /// In en, this message translates to:
  /// **'Snapshot'**
  String get versionSnapshot;

  /// No description provided for @accountsTitle.
  ///
  /// In en, this message translates to:
  /// **'Accounts'**
  String get accountsTitle;

  /// No description provided for @accountsSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Microsoft, offline, and third-party authlib accounts.'**
  String get accountsSubtitle;

  /// No description provided for @currentAccount.
  ///
  /// In en, this message translates to:
  /// **'Current account'**
  String get currentAccount;

  /// No description provided for @addAccount.
  ///
  /// In en, this message translates to:
  /// **'Add account'**
  String get addAccount;

  /// No description provided for @accountMicrosoftDesc.
  ///
  /// In en, this message translates to:
  /// **'Sign in with your Microsoft account in the browser.'**
  String get accountMicrosoftDesc;

  /// No description provided for @accountOfflineDesc.
  ///
  /// In en, this message translates to:
  /// **'Local profile for single-player and LAN.'**
  String get accountOfflineDesc;

  /// No description provided for @accountThirdPartyDesc.
  ///
  /// In en, this message translates to:
  /// **'Yggdrasil via authlib-injector.'**
  String get accountThirdPartyDesc;

  /// No description provided for @refresh.
  ///
  /// In en, this message translates to:
  /// **'Refresh'**
  String get refresh;

  /// No description provided for @logout.
  ///
  /// In en, this message translates to:
  /// **'Sign out'**
  String get logout;

  /// No description provided for @settingsTitle.
  ///
  /// In en, this message translates to:
  /// **'Settings'**
  String get settingsTitle;

  /// No description provided for @settingsSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Appearance, storage, Java, and about.'**
  String get settingsSubtitle;

  /// No description provided for @settingsGeneral.
  ///
  /// In en, this message translates to:
  /// **'General'**
  String get settingsGeneral;

  /// No description provided for @settingsAppearance.
  ///
  /// In en, this message translates to:
  /// **'Appearance'**
  String get settingsAppearance;

  /// No description provided for @settingsStorage.
  ///
  /// In en, this message translates to:
  /// **'Storage'**
  String get settingsStorage;

  /// No description provided for @settingsInstancesRoot.
  ///
  /// In en, this message translates to:
  /// **'Instances folder'**
  String get settingsInstancesRoot;

  /// No description provided for @settingsInstanceRoots.
  ///
  /// In en, this message translates to:
  /// **'Instance storage'**
  String get settingsInstanceRoots;

  /// No description provided for @settingsAddRoot.
  ///
  /// In en, this message translates to:
  /// **'Add folder'**
  String get settingsAddRoot;

  /// No description provided for @settingsDefaultRoot.
  ///
  /// In en, this message translates to:
  /// **'Default'**
  String get settingsDefaultRoot;

  /// No description provided for @settingsMakeDefault.
  ///
  /// In en, this message translates to:
  /// **'Set as default'**
  String get settingsMakeDefault;

  /// No description provided for @settingsRemoveRoot.
  ///
  /// In en, this message translates to:
  /// **'Remove folder'**
  String get settingsRemoveRoot;

  /// No description provided for @settingsRootsUpdated.
  ///
  /// In en, this message translates to:
  /// **'Instance folders updated.'**
  String get settingsRootsUpdated;

  /// No description provided for @settingsDataRoot.
  ///
  /// In en, this message translates to:
  /// **'Application data'**
  String get settingsDataRoot;

  /// No description provided for @settingsChooseFolder.
  ///
  /// In en, this message translates to:
  /// **'Choose folder'**
  String get settingsChooseFolder;

  /// No description provided for @settingsSaved.
  ///
  /// In en, this message translates to:
  /// **'Saved'**
  String get settingsSaved;

  /// No description provided for @settingsJava.
  ///
  /// In en, this message translates to:
  /// **'Java'**
  String get settingsJava;

  /// No description provided for @settingsJavaAuto.
  ///
  /// In en, this message translates to:
  /// **'Auto select'**
  String get settingsJavaAuto;

  /// No description provided for @settingsJavaAutoHint.
  ///
  /// In en, this message translates to:
  /// **'Choose a Java runtime that matches the game version.'**
  String get settingsJavaAutoHint;

  /// No description provided for @settingsJavaCurrent.
  ///
  /// In en, this message translates to:
  /// **'Current Java'**
  String get settingsJavaCurrent;

  /// No description provided for @settingsJavaAutoValue.
  ///
  /// In en, this message translates to:
  /// **'Automatic (per game version)'**
  String get settingsJavaAutoValue;

  /// No description provided for @settingsJavaUseAuto.
  ///
  /// In en, this message translates to:
  /// **'Use automatic selection'**
  String get settingsJavaUseAuto;

  /// No description provided for @settingsJavaSelected.
  ///
  /// In en, this message translates to:
  /// **'Java selection saved.'**
  String get settingsJavaSelected;

  /// No description provided for @settingsMemory.
  ///
  /// In en, this message translates to:
  /// **'Memory'**
  String get settingsMemory;

  /// No description provided for @settingsJvmArgs.
  ///
  /// In en, this message translates to:
  /// **'JVM arguments'**
  String get settingsJvmArgs;

  /// No description provided for @settingsRuntimeHint.
  ///
  /// In en, this message translates to:
  /// **'Runtime options apply to the next launch and save automatically.'**
  String get settingsRuntimeHint;

  /// No description provided for @settingsMemoryMin.
  ///
  /// In en, this message translates to:
  /// **'Minimum memory (MB)'**
  String get settingsMemoryMin;

  /// No description provided for @settingsMemoryMax.
  ///
  /// In en, this message translates to:
  /// **'Maximum memory (MB)'**
  String get settingsMemoryMax;

  /// No description provided for @settingsJvmArgsHint.
  ///
  /// In en, this message translates to:
  /// **'Optional, space-separated'**
  String get settingsJvmArgsHint;

  /// No description provided for @settingsGc.
  ///
  /// In en, this message translates to:
  /// **'Garbage collector'**
  String get settingsGc;

  /// No description provided for @settingsGcAuto.
  ///
  /// In en, this message translates to:
  /// **'Automatic'**
  String get settingsGcAuto;

  /// No description provided for @settingsGcG1.
  ///
  /// In en, this message translates to:
  /// **'G1GC'**
  String get settingsGcG1;

  /// No description provided for @settingsGcZgc.
  ///
  /// In en, this message translates to:
  /// **'ZGC (Java 21+)'**
  String get settingsGcZgc;

  /// No description provided for @settingsGcParallel.
  ///
  /// In en, this message translates to:
  /// **'Parallel'**
  String get settingsGcParallel;

  /// No description provided for @settingsGcSerial.
  ///
  /// In en, this message translates to:
  /// **'Serial'**
  String get settingsGcSerial;

  /// No description provided for @settingsGcShenandoah.
  ///
  /// In en, this message translates to:
  /// **'Shenandoah'**
  String get settingsGcShenandoah;

  /// No description provided for @settingsGcEpsilon.
  ///
  /// In en, this message translates to:
  /// **'Epsilon (testing)'**
  String get settingsGcEpsilon;

  /// No description provided for @settingsGameArgs.
  ///
  /// In en, this message translates to:
  /// **'Additional game arguments'**
  String get settingsGameArgs;

  /// No description provided for @settingsGameArgsHint.
  ///
  /// In en, this message translates to:
  /// **'Optional, appended after the version arguments'**
  String get settingsGameArgsHint;

  /// No description provided for @settingsMemoryInvalid.
  ///
  /// In en, this message translates to:
  /// **'Memory must be 128–65536 MB and min ≤ max.'**
  String get settingsMemoryInvalid;

  /// No description provided for @settingsAbout.
  ///
  /// In en, this message translates to:
  /// **'About'**
  String get settingsAbout;

  /// No description provided for @settingsTheme.
  ///
  /// In en, this message translates to:
  /// **'Theme'**
  String get settingsTheme;

  /// No description provided for @settingsThemeSystem.
  ///
  /// In en, this message translates to:
  /// **'Follow system'**
  String get settingsThemeSystem;

  /// No description provided for @settingsLanguage.
  ///
  /// In en, this message translates to:
  /// **'Language'**
  String get settingsLanguage;

  /// No description provided for @settingsMotion.
  ///
  /// In en, this message translates to:
  /// **'Reduce motion'**
  String get settingsMotion;

  /// No description provided for @settingsMotionDesc.
  ///
  /// In en, this message translates to:
  /// **'Limit page transitions and background effects.'**
  String get settingsMotionDesc;

  /// No description provided for @settingsDiagnostics.
  ///
  /// In en, this message translates to:
  /// **'Diagnostics'**
  String get settingsDiagnostics;

  /// No description provided for @settingsDiagnosticsDesc.
  ///
  /// In en, this message translates to:
  /// **'Logs, module versions, and crash reports.'**
  String get settingsDiagnosticsDesc;

  /// No description provided for @settingsVersion.
  ///
  /// In en, this message translates to:
  /// **'Version'**
  String get settingsVersion;

  /// No description provided for @settingsLicense.
  ///
  /// In en, this message translates to:
  /// **'License'**
  String get settingsLicense;

  /// No description provided for @settingsSource.
  ///
  /// In en, this message translates to:
  /// **'Source code'**
  String get settingsSource;

  /// Title of the Java runtime status card.
  ///
  /// In en, this message translates to:
  /// **'Java'**
  String get javaStatusTitle;

  /// Java status before the first scan.
  ///
  /// In en, this message translates to:
  /// **'Idle'**
  String get javaStatusIdle;

  /// Java status while java.scan is running.
  ///
  /// In en, this message translates to:
  /// **'Scanning'**
  String get javaStatusScanning;

  /// Java status after a successful scan.
  ///
  /// In en, this message translates to:
  /// **'Ready'**
  String get javaStatusReady;

  /// Java status when the scanning module is absent or fails.
  ///
  /// In en, this message translates to:
  /// **'Unavailable'**
  String get javaStatusUnavailable;

  /// Hint shown before the first Java scan.
  ///
  /// In en, this message translates to:
  /// **'Scan your system for installed Java runtimes.'**
  String get javaStatusIdleHint;

  /// Hint shown while the Java scan task is in flight.
  ///
  /// In en, this message translates to:
  /// **'Calling java.scan…'**
  String get javaStatusScanningHint;

  /// Progress line shown while the Java scan task is running.
  ///
  /// In en, this message translates to:
  /// **'Progress: {done}/{total}'**
  String javaStatusProgress(int done, int total);

  /// Generic explanation when Java scanning cannot run.
  ///
  /// In en, this message translates to:
  /// **'Java scanning is unavailable. The business module may not be built yet.'**
  String get javaStatusUnavailableHint;

  /// Button label that calls java.scan.
  ///
  /// In en, this message translates to:
  /// **'Scan Java'**
  String get javaStatusScan;

  /// No description provided for @javaStatusSelect.
  ///
  /// In en, this message translates to:
  /// **'Use this Java'**
  String get javaStatusSelect;

  /// Number of Java runtimes found by the last scan.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =0{No Java runtimes found} =1{1 Java runtime found} other{{count} Java runtimes found}}'**
  String javaStatusCount(int count);

  /// Version and architecture line for one Java runtime.
  ///
  /// In en, this message translates to:
  /// **'{version} · {arch}'**
  String javaStatusMeta(String version, String arch);

  /// Shown when a Java scan was attempted but failed.
  ///
  /// In en, this message translates to:
  /// **'Scan failed: {message}'**
  String javaStatusScanFailed(String message);

  /// Hint shown when a scan succeeded but found no runtimes.
  ///
  /// In en, this message translates to:
  /// **'Install a JDK or set JAVA_HOME, then scan again.'**
  String get javaStatusEmptyHint;

  /// Title shown when a scan succeeded but found no runtimes.
  ///
  /// In en, this message translates to:
  /// **'No Java found'**
  String get javaStatusNoResultsTitle;

  /// Explains the manual folder and executable fallbacks.
  ///
  /// In en, this message translates to:
  /// **'Java was not found in the default locations. Search a folder or select the java executable.'**
  String get javaStatusNoResultsHint;

  /// Button that opens the folder picker for manual Java discovery.
  ///
  /// In en, this message translates to:
  /// **'Search a folder'**
  String get javaStatusSearchFolder;

  /// Button that opens the file picker for a java executable.
  ///
  /// In en, this message translates to:
  /// **'Select java executable'**
  String get javaStatusSelectExecutable;

  /// Adds another search root when runtimes were already found.
  ///
  /// In en, this message translates to:
  /// **'Add folder'**
  String get javaStatusAddFolder;

  /// Stronger fallback prompt after a picked folder yielded no runtimes.
  ///
  /// In en, this message translates to:
  /// **'Still not found — select the java executable.'**
  String get javaStatusStillNotFound;

  /// Shown when a picked executable is not a usable Java runtime.
  ///
  /// In en, this message translates to:
  /// **'No valid Java at this path: {path}'**
  String javaStatusNoValidExecutable(String path);

  /// Hint explaining which file the executable picker expects.
  ///
  /// In en, this message translates to:
  /// **'Choose the bin/java file inside a JDK (bin\\java.exe on Windows).'**
  String get javaStatusNoValidExecutableHint;

  /// Label above the removable chips of manually added paths.
  ///
  /// In en, this message translates to:
  /// **'Custom paths'**
  String get javaStatusCustomPaths;

  /// Tooltip for removing a saved custom path.
  ///
  /// In en, this message translates to:
  /// **'Remove {path}'**
  String javaStatusRemovePath(String path);

  /// No description provided for @homeFootnote.
  ///
  /// In en, this message translates to:
  /// **'ArchoeraCX is not an official Minecraft product. Not approved by or associated with Mojang or Microsoft.'**
  String get homeFootnote;

  /// No description provided for @javaStatusViewAll.
  ///
  /// In en, this message translates to:
  /// **'View all {count}'**
  String javaStatusViewAll(int count);

  /// No description provided for @javaStatusAllTitle.
  ///
  /// In en, this message translates to:
  /// **'Java runtimes ({count})'**
  String javaStatusAllTitle(int count);

  /// Generic close button label.
  ///
  /// In en, this message translates to:
  /// **'Close'**
  String get close;

  /// Folder picker action for a JDK home directory.
  ///
  /// In en, this message translates to:
  /// **'Select JDK home'**
  String get javaStatusSelectJdkHome;

  /// No description provided for @accountMicrosoftTitle.
  ///
  /// In en, this message translates to:
  /// **'Microsoft sign-in'**
  String get accountMicrosoftTitle;

  /// No description provided for @accountDeviceCodeTitle.
  ///
  /// In en, this message translates to:
  /// **'Enter this code'**
  String get accountDeviceCodeTitle;

  /// Device code instruction.
  ///
  /// In en, this message translates to:
  /// **'Open {uri} in a browser and enter the code below.'**
  String accountDeviceCodeHint(String uri);

  /// No description provided for @accountDeviceCodeWaiting.
  ///
  /// In en, this message translates to:
  /// **'Waiting for Microsoft authorization…'**
  String get accountDeviceCodeWaiting;

  /// No description provided for @accountDeviceCodeCopied.
  ///
  /// In en, this message translates to:
  /// **'Code copied.'**
  String get accountDeviceCodeCopied;

  /// Device code lifetime.
  ///
  /// In en, this message translates to:
  /// **'The code expires in about {minutes} minutes.'**
  String accountDeviceCodeExpires(int minutes);

  /// Authorization code flow instruction after opening the browser.
  ///
  /// In en, this message translates to:
  /// **'Your browser has been opened. Sign in with your Microsoft account to continue.'**
  String get accountBrowserSignInHint;

  /// Status while waiting for the loopback browser callback.
  ///
  /// In en, this message translates to:
  /// **'Waiting for the browser callback…'**
  String get accountBrowserWaitingCallback;

  /// Button that reopens the Microsoft authorization page.
  ///
  /// In en, this message translates to:
  /// **'Open browser'**
  String get accountOpenBrowser;

  /// Shown when url_launcher could not open the authorization URL.
  ///
  /// In en, this message translates to:
  /// **'Could not open the browser automatically. Open the link below manually.'**
  String get accountBrowserOpenFailed;

  /// No description provided for @accountAddOffline.
  ///
  /// In en, this message translates to:
  /// **'Add offline account'**
  String get accountAddOffline;

  /// No description provided for @accountOfflineNameLabel.
  ///
  /// In en, this message translates to:
  /// **'Player name'**
  String get accountOfflineNameLabel;

  /// No description provided for @accountOfflineNameHint.
  ///
  /// In en, this message translates to:
  /// **'3–16 characters'**
  String get accountOfflineNameHint;

  /// No description provided for @accountRemove.
  ///
  /// In en, this message translates to:
  /// **'Remove account'**
  String get accountRemove;

  /// Remove account confirmation.
  ///
  /// In en, this message translates to:
  /// **'Remove “{name}”? The game files are not deleted.'**
  String accountRemoveConfirm(String name);

  /// No description provided for @accountRefresh.
  ///
  /// In en, this message translates to:
  /// **'Refresh session'**
  String get accountRefresh;

  /// No description provided for @accountNoAccounts.
  ///
  /// In en, this message translates to:
  /// **'No accounts yet. Add a Microsoft or offline account to launch the game.'**
  String get accountNoAccounts;

  /// Sign-in error.
  ///
  /// In en, this message translates to:
  /// **'Sign-in failed: {message}'**
  String accountLoginFailed(String message);

  /// No description provided for @accountWaitingAuthorization.
  ///
  /// In en, this message translates to:
  /// **'Waiting for authorization'**
  String get accountWaitingAuthorization;

  /// No description provided for @accountSignIn.
  ///
  /// In en, this message translates to:
  /// **'Sign in'**
  String get accountSignIn;

  /// No description provided for @accountCancelSignIn.
  ///
  /// In en, this message translates to:
  /// **'Cancel sign-in'**
  String get accountCancelSignIn;

  /// No description provided for @accountSessionRefreshed.
  ///
  /// In en, this message translates to:
  /// **'Session refreshed.'**
  String get accountSessionRefreshed;

  /// No description provided for @accountRemoved.
  ///
  /// In en, this message translates to:
  /// **'Account removed.'**
  String get accountRemoved;

  /// No description provided for @accountSelected.
  ///
  /// In en, this message translates to:
  /// **'Current account changed.'**
  String get accountSelected;

  /// No description provided for @commonCopy.
  ///
  /// In en, this message translates to:
  /// **'Copy'**
  String get commonCopy;

  /// No description provided for @commonAdd.
  ///
  /// In en, this message translates to:
  /// **'Add'**
  String get commonAdd;

  /// No description provided for @instanceSelected.
  ///
  /// In en, this message translates to:
  /// **'Selected'**
  String get instanceSelected;

  /// Vanilla loader name.
  ///
  /// In en, this message translates to:
  /// **'Vanilla'**
  String get loaderVanilla;

  /// Fabric loader name.
  ///
  /// In en, this message translates to:
  /// **'Fabric'**
  String get loaderFabric;

  /// Quilt loader name.
  ///
  /// In en, this message translates to:
  /// **'Quilt'**
  String get loaderQuilt;

  /// Forge loader name.
  ///
  /// In en, this message translates to:
  /// **'Forge'**
  String get loaderForge;

  /// NeoForge loader name.
  ///
  /// In en, this message translates to:
  /// **'NeoForge'**
  String get loaderNeoForge;

  /// Loader name followed by its version.
  ///
  /// In en, this message translates to:
  /// **'{loader} {version}'**
  String loaderVersionValue(String loader, String version);

  /// Microsoft account type.
  ///
  /// In en, this message translates to:
  /// **'Microsoft'**
  String get accountMicrosoft;

  /// Authlib account type.
  ///
  /// In en, this message translates to:
  /// **'Authlib'**
  String get accountAuthlib;

  /// Action that opens diagnostics.
  ///
  /// In en, this message translates to:
  /// **'Open'**
  String get settingsDiagnosticsOpen;

  /// Use the operating system language.
  ///
  /// In en, this message translates to:
  /// **'Follow system'**
  String get settingsLanguageSystem;

  /// English language option.
  ///
  /// In en, this message translates to:
  /// **'English'**
  String get settingsLanguageEnglish;

  /// Simplified Chinese language option.
  ///
  /// In en, this message translates to:
  /// **'简体中文'**
  String get settingsLanguageChinese;

  /// Language preference saved.
  ///
  /// In en, this message translates to:
  /// **'Language changed.'**
  String get settingsLanguageChanged;

  /// Stable provider release channel.
  ///
  /// In en, this message translates to:
  /// **'Release'**
  String get resourceVersionRelease;

  /// Beta provider release channel.
  ///
  /// In en, this message translates to:
  /// **'Beta'**
  String get resourceVersionBeta;

  /// Alpha provider release channel.
  ///
  /// In en, this message translates to:
  /// **'Alpha'**
  String get resourceVersionAlpha;

  /// Fallback when a Java version is unknown.
  ///
  /// In en, this message translates to:
  /// **'Unknown'**
  String get javaStatusUnknownVersion;

  /// Instance summary in the create dialog.
  ///
  /// In en, this message translates to:
  /// **'{loader} · Minecraft {version}'**
  String instanceCreateSummary(String loader, String version);

  /// Resource search target for a base game.
  ///
  /// In en, this message translates to:
  /// **'Minecraft {version}'**
  String resourceTargetGame(String version);

  /// Resource search target with a loader.
  ///
  /// In en, this message translates to:
  /// **'Minecraft {version} · {loader}'**
  String resourceTargetGameLoader(String version, String loader);

  /// Resource search target for shader packs.
  ///
  /// In en, this message translates to:
  /// **'Minecraft {version} · Iris'**
  String resourceTargetShader(String version);

  /// Loader install processor completion note.
  ///
  /// In en, this message translates to:
  /// **'Forge/NeoForge processors completed with the selected Java.'**
  String get loaderProcessorsDone;

  /// Instance count label.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =0{No instances} =1{1 instance} other{{count} instances}}'**
  String instancesCount(int count);

  /// Instance list load failure.
  ///
  /// In en, this message translates to:
  /// **'Could not load instances: {message}'**
  String instancesLoadFailed(String message);

  /// Account list load failure.
  ///
  /// In en, this message translates to:
  /// **'Could not load accounts: {message}'**
  String accountsLoadFailed(String message);

  /// Global Modrinth search target label.
  ///
  /// In en, this message translates to:
  /// **'All versions · all loaders'**
  String get downloadGlobalTarget;

  /// Instance overflow menu tooltip.
  ///
  /// In en, this message translates to:
  /// **'Manage'**
  String get instanceManage;

  /// Open the loader installation dialog for an instance.
  ///
  /// In en, this message translates to:
  /// **'Install / update loader'**
  String get instanceManageLoader;

  /// Instance overflow action that opens the mod manager.
  ///
  /// In en, this message translates to:
  /// **'Manage mods'**
  String get instanceManageMods;

  /// Title of the instance mod manager.
  ///
  /// In en, this message translates to:
  /// **'Installed mods'**
  String get instanceModsTitle;

  /// Empty mod manager message.
  ///
  /// In en, this message translates to:
  /// **'No mods in this instance.'**
  String get instanceModsEmpty;

  /// Mod list load failure.
  ///
  /// In en, this message translates to:
  /// **'Could not load mods: {message}'**
  String instanceModsLoadFailed(String message);

  /// Delete a mod file.
  ///
  /// In en, this message translates to:
  /// **'Delete mod'**
  String get instanceModDelete;

  /// Confirm deleting one mod file.
  ///
  /// In en, this message translates to:
  /// **'Delete “{name}”? This removes the mod file.'**
  String instanceModDeleteConfirm(String name);

  /// Mod is disabled and renamed with a .disabled suffix.
  ///
  /// In en, this message translates to:
  /// **'Disabled'**
  String get instanceModDisabled;

  /// Enable/disable failure.
  ///
  /// In en, this message translates to:
  /// **'Could not update the mod.'**
  String get instanceModsToggleFailed;

  /// Delete failure.
  ///
  /// In en, this message translates to:
  /// **'Could not remove the mod.'**
  String get instanceModsRemoveFailed;

  /// Shown when an account has no skin texture.
  ///
  /// In en, this message translates to:
  /// **'Default appearance'**
  String get accountDefaultSkin;

  /// Memory summary heading.
  ///
  /// In en, this message translates to:
  /// **'System memory'**
  String get settingsMemorySystem;

  /// Total physical memory label.
  ///
  /// In en, this message translates to:
  /// **'Total'**
  String get settingsMemoryTotal;

  /// Available physical memory label.
  ///
  /// In en, this message translates to:
  /// **'Available'**
  String get settingsMemoryAvailable;

  /// Estimated memory use after launching the game.
  ///
  /// In en, this message translates to:
  /// **'After launch'**
  String get settingsMemoryEstimated;

  /// Memory information is unavailable.
  ///
  /// In en, this message translates to:
  /// **'Unavailable'**
  String get settingsMemoryUnavailable;

  /// Memory warning.
  ///
  /// In en, this message translates to:
  /// **'Estimated usage exceeds available memory.'**
  String get settingsMemoryOverAvailable;

  /// Search result count.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =0{No results} =1{1 result} other{{count} results}}'**
  String downloadResults(num count);

  /// Search result sort label.
  ///
  /// In en, this message translates to:
  /// **'Sort'**
  String get downloadSort;

  /// Sort by relevance.
  ///
  /// In en, this message translates to:
  /// **'Relevance'**
  String get downloadSortRelevance;

  /// Sort by download count.
  ///
  /// In en, this message translates to:
  /// **'Downloads'**
  String get downloadSortDownloads;

  /// Sort by follower count.
  ///
  /// In en, this message translates to:
  /// **'Follows'**
  String get downloadSortFollows;

  /// Sort by newest project.
  ///
  /// In en, this message translates to:
  /// **'Newest'**
  String get downloadSortNewest;

  /// Sort by recently updated.
  ///
  /// In en, this message translates to:
  /// **'Recently updated'**
  String get downloadSortUpdated;

  /// Project gallery heading.
  ///
  /// In en, this message translates to:
  /// **'Screenshots'**
  String get downloadScreenshots;

  /// Version dependency count.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =0{No dependencies} =1{1 dependency} other{{count} dependencies}}'**
  String downloadDependencies(num count);

  /// Load the next search page.
  ///
  /// In en, this message translates to:
  /// **'Load more'**
  String get downloadLoadMore;

  /// Accessible label for a gallery image.
  ///
  /// In en, this message translates to:
  /// **'Image preview'**
  String get downloadImagePreview;

  /// Project author line.
  ///
  /// In en, this message translates to:
  /// **'by {author}'**
  String downloadByAuthor(Object author);

  /// Open the loader-version picker.
  ///
  /// In en, this message translates to:
  /// **'Select loader version'**
  String get loaderVersionSelect;

  /// Filter the loader-version list.
  ///
  /// In en, this message translates to:
  /// **'Search versions'**
  String get loaderVersionSearch;

  /// Latest loader build.
  ///
  /// In en, this message translates to:
  /// **'Latest'**
  String get loaderVersionLatest;

  /// Recommended loader build.
  ///
  /// In en, this message translates to:
  /// **'Recommended'**
  String get loaderVersionRecommended;

  /// Beta loader build.
  ///
  /// In en, this message translates to:
  /// **'Beta'**
  String get loaderVersionBeta;

  /// Loader-version request failed.
  ///
  /// In en, this message translates to:
  /// **'Could not load loader versions.'**
  String get loaderVersionLoadFailed;

  /// Empty loader-version list.
  ///
  /// In en, this message translates to:
  /// **'No loader versions were found for this game version.'**
  String get loaderVersionEmpty;

  /// Project long description heading.
  ///
  /// In en, this message translates to:
  /// **'Description'**
  String get downloadDescription;

  /// Instance menu action that installs vanilla game files.
  ///
  /// In en, this message translates to:
  /// **'Download game files'**
  String get instanceInstallGame;

  /// Dialog title for vanilla installation.
  ///
  /// In en, this message translates to:
  /// **'Install game files'**
  String get instanceInstallGameTitle;

  /// Explanation for vanilla installation.
  ///
  /// In en, this message translates to:
  /// **'Downloads the vanilla client, libraries and assets, then writes the version JSON used by launch.'**
  String get instanceInstallGameHint;

  /// Installation progress status.
  ///
  /// In en, this message translates to:
  /// **'Downloading game files…'**
  String get instanceInstallGameRunning;

  /// Installation completed.
  ///
  /// In en, this message translates to:
  /// **'Game files are ready.'**
  String get instanceInstallGameDone;

  /// Installation failed.
  ///
  /// In en, this message translates to:
  /// **'Game file installation failed.'**
  String get instanceInstallGameFailed;

  /// Retry a failed operation.
  ///
  /// In en, this message translates to:
  /// **'Retry'**
  String get commonRetry;

  /// Task center title.
  ///
  /// In en, this message translates to:
  /// **'Tasks'**
  String get taskCenterTitle;

  /// Empty task center.
  ///
  /// In en, this message translates to:
  /// **'No active tasks.'**
  String get taskCenterEmpty;

  /// Active task count.
  ///
  /// In en, this message translates to:
  /// **'{count} active'**
  String taskCenterActive(Object count);

  /// Completed task label.
  ///
  /// In en, this message translates to:
  /// **'Done'**
  String get taskCenterCompleted;

  /// Failed task label.
  ///
  /// In en, this message translates to:
  /// **'Failed'**
  String get taskCenterFailed;

  /// Cancelled task label.
  ///
  /// In en, this message translates to:
  /// **'Cancelled'**
  String get taskCenterCancelled;

  /// Cancel task action.
  ///
  /// In en, this message translates to:
  /// **'Cancel'**
  String get taskCenterCancel;

  /// Clear completed tasks.
  ///
  /// In en, this message translates to:
  /// **'Clear completed'**
  String get taskCenterClear;

  /// Download task label.
  ///
  /// In en, this message translates to:
  /// **'Download'**
  String get taskCenterDownload;

  /// Loader task label.
  ///
  /// In en, this message translates to:
  /// **'Loader'**
  String get taskCenterLoader;

  /// Task centre label for .mrpack installation.
  ///
  /// In en, this message translates to:
  /// **'Modpack'**
  String get taskCenterModpack;

  /// Vanilla install task label.
  ///
  /// In en, this message translates to:
  /// **'Game files'**
  String get taskCenterGameFiles;

  /// Java scan task label.
  ///
  /// In en, this message translates to:
  /// **'Java scan'**
  String get taskCenterJavaScan;

  /// Generic task label.
  ///
  /// In en, this message translates to:
  /// **'Task'**
  String get taskCenterTask;

  /// Transfer rate label.
  ///
  /// In en, this message translates to:
  /// **'{rate}/s'**
  String taskCenterSpeed(Object rate);

  /// Task running without known total.
  ///
  /// In en, this message translates to:
  /// **'Working'**
  String get taskCenterWorking;

  /// Mod install requires a mod-loader instance.
  ///
  /// In en, this message translates to:
  /// **'Select a Fabric/Forge/NeoForge/Quilt instance before installing mods.'**
  String get resourceNeedsLoader;

  /// Create instance under versions/ with shared libraries/assets.
  ///
  /// In en, this message translates to:
  /// **'Version isolation'**
  String get instanceVersionIsolation;

  /// Version isolation explanation.
  ///
  /// In en, this message translates to:
  /// **'The game data lives in versions/<name>; libraries and assets are shared with the parent game folder.'**
  String get instanceVersionIsolationHint;

  /// Release-mode account policy error.
  ///
  /// In en, this message translates to:
  /// **'Sign in with a Microsoft account that owns Minecraft first.'**
  String get accountPremiumRequired;

  /// Subtitle shown when offline accounts are gated.
  ///
  /// In en, this message translates to:
  /// **'A verified Microsoft account is required before adding or selecting other accounts.'**
  String get accountPremiumRequiredHint;

  /// Runtime regional download gate title.
  ///
  /// In en, this message translates to:
  /// **'Downloads are disabled in this region'**
  String get downloadUnavailableTitle;

  /// Runtime regional download gate explanation.
  ///
  /// In en, this message translates to:
  /// **'Downloads are disabled by the local region policy for the detected mainland-China network. Add the required fields to the configuration file, then re-check. Existing local instances and launch features remain available.'**
  String get downloadUnavailableMessage;

  /// Download gate config path.
  ///
  /// In en, this message translates to:
  /// **'Configuration file: {path}'**
  String downloadConfigPath(Object path);

  /// Fields required to enable downloads.
  ///
  /// In en, this message translates to:
  /// **'Required fields: developerMode=true, acceptRisk=true, downloadLevel=L2, acknowledgement=ENABLE-DOWNLOADS 1'**
  String get downloadConfigFields;

  /// Reload the local download gate.
  ///
  /// In en, this message translates to:
  /// **'Re-check configuration'**
  String get downloadConfigReload;

  /// Region policy error.
  ///
  /// In en, this message translates to:
  /// **'Only a verified Microsoft account is allowed in this region.'**
  String get accountMicrosoftOnly;

  /// Region policy hint.
  ///
  /// In en, this message translates to:
  /// **'This region only allows Microsoft-verified accounts. Offline and third-party accounts are disabled.'**
  String get accountMicrosoftOnlyHint;

  /// Accounts subtitle when only Microsoft accounts are allowed.
  ///
  /// In en, this message translates to:
  /// **'Microsoft-verified accounts only in this region.'**
  String get accountsSubtitleMicrosoftOnly;

  /// Empty account message when only Microsoft accounts are allowed.
  ///
  /// In en, this message translates to:
  /// **'Add a Microsoft account to get started.'**
  String get accountNoAccountsMicrosoftOnly;

  /// Online launch reserved message.
  ///
  /// In en, this message translates to:
  /// **'Online launch is not enabled in this build yet.'**
  String get launchOnlineNotReady;

  /// Persistent development-build badge.
  ///
  /// In en, this message translates to:
  /// **'DEV'**
  String get developmentBuildBadge;

  /// Persistent development-build warning banner.
  ///
  /// In en, this message translates to:
  /// **'Development build — incomplete and may corrupt data. Do not use it for production or important instances.'**
  String get developmentBuildWarning;

  /// Online launch session expired.
  ///
  /// In en, this message translates to:
  /// **'Your Minecraft session expired. Sign in again.'**
  String get launchOnlineSessionExpired;

  /// Online launch requires a Microsoft account.
  ///
  /// In en, this message translates to:
  /// **'Select a Microsoft account to launch online.'**
  String get launchMicrosoftAccountRequired;

  /// Online launch client ID missing.
  ///
  /// In en, this message translates to:
  /// **'Online launch is not configured in this build.'**
  String get launchOnlineNotConfigured;

  /// Prominent release policy notice on the accounts page.
  ///
  /// In en, this message translates to:
  /// **'This launcher requires a verified Microsoft account to start a game. You can still download content and manage instance files, but offline/local accounts stay locked until a verified Minecraft account is present.'**
  String get accountVerifiedRequiredNotice;

  /// No description provided for @accountLoginSuccessNotificationTitle.
  ///
  /// In en, this message translates to:
  /// **'ArchoeraCX signed in'**
  String get accountLoginSuccessNotificationTitle;

  /// No description provided for @accountLoginSuccessNotificationBody.
  ///
  /// In en, this message translates to:
  /// **'Signed in as {account}.'**
  String accountLoginSuccessNotificationBody(String account);

  /// No description provided for @settingsAppearanceNote.
  ///
  /// In en, this message translates to:
  /// **'Theme, accent color and interface effects apply immediately.'**
  String get settingsAppearanceNote;

  /// No description provided for @settingsAppearancePersonalization.
  ///
  /// In en, this message translates to:
  /// **'Personalization'**
  String get settingsAppearancePersonalization;

  /// No description provided for @settingsAppearanceLanguage.
  ///
  /// In en, this message translates to:
  /// **'Language & region'**
  String get settingsAppearanceLanguage;

  /// No description provided for @settingsDensity.
  ///
  /// In en, this message translates to:
  /// **'Interface density'**
  String get settingsDensity;

  /// No description provided for @settingsDensityDesc.
  ///
  /// In en, this message translates to:
  /// **'Adjust control spacing and list height.'**
  String get settingsDensityDesc;

  /// No description provided for @settingsDensityCompact.
  ///
  /// In en, this message translates to:
  /// **'Compact'**
  String get settingsDensityCompact;

  /// No description provided for @settingsDensityStandard.
  ///
  /// In en, this message translates to:
  /// **'Standard'**
  String get settingsDensityStandard;

  /// No description provided for @settingsDensityComfortable.
  ///
  /// In en, this message translates to:
  /// **'Comfortable'**
  String get settingsDensityComfortable;

  /// No description provided for @settingsThemeMode.
  ///
  /// In en, this message translates to:
  /// **'Color mode'**
  String get settingsThemeMode;

  /// No description provided for @settingsThemeModeDesc.
  ///
  /// In en, this message translates to:
  /// **'Use light, dark, or match the operating system.'**
  String get settingsThemeModeDesc;

  /// No description provided for @settingsThemeLight.
  ///
  /// In en, this message translates to:
  /// **'Light'**
  String get settingsThemeLight;

  /// No description provided for @settingsThemeDark.
  ///
  /// In en, this message translates to:
  /// **'Dark'**
  String get settingsThemeDark;

  /// No description provided for @settingsThemeAccent.
  ///
  /// In en, this message translates to:
  /// **'Accent color'**
  String get settingsThemeAccent;

  /// No description provided for @settingsThemeAccentDesc.
  ///
  /// In en, this message translates to:
  /// **'Use the system accent, or pick a color for the launcher.'**
  String get settingsThemeAccentDesc;

  /// No description provided for @settingsThemeAccentSystem.
  ///
  /// In en, this message translates to:
  /// **'System'**
  String get settingsThemeAccentSystem;

  /// No description provided for @settingsThemeAccentCustom.
  ///
  /// In en, this message translates to:
  /// **'Custom'**
  String get settingsThemeAccentCustom;

  /// No description provided for @settingsThemeAccentCustomHint.
  ///
  /// In en, this message translates to:
  /// **'Enter a hex color such as #4F7CFF.'**
  String get settingsThemeAccentCustomHint;

  /// No description provided for @settingsThemeAccentInvalid.
  ///
  /// In en, this message translates to:
  /// **'Enter a valid hex color.'**
  String get settingsThemeAccentInvalid;

  /// No description provided for @settingsGlobalTint.
  ///
  /// In en, this message translates to:
  /// **'Global tint'**
  String get settingsGlobalTint;

  /// No description provided for @settingsGlobalTintDesc.
  ///
  /// In en, this message translates to:
  /// **'Shift panels and backgrounds slightly toward the accent color.'**
  String get settingsGlobalTintDesc;

  /// No description provided for @settingsResetAppearance.
  ///
  /// In en, this message translates to:
  /// **'Reset appearance'**
  String get settingsResetAppearance;

  /// No description provided for @settingsAppearanceResetDone.
  ///
  /// In en, this message translates to:
  /// **'Appearance reset.'**
  String get settingsAppearanceResetDone;

  /// No description provided for @settingsAboutDesc.
  ///
  /// In en, this message translates to:
  /// **'Version, runtime, licenses and community links.'**
  String get settingsAboutDesc;

  /// No description provided for @settingsAboutTagline.
  ///
  /// In en, this message translates to:
  /// **'Cross-platform Minecraft: Java Edition launcher'**
  String get settingsAboutTagline;

  /// No description provided for @settingsAboutEnvironment.
  ///
  /// In en, this message translates to:
  /// **'Environment'**
  String get settingsAboutEnvironment;

  /// No description provided for @settingsAboutBuildInfo.
  ///
  /// In en, this message translates to:
  /// **'Build'**
  String get settingsAboutBuildInfo;

  /// No description provided for @settingsAboutPlatform.
  ///
  /// In en, this message translates to:
  /// **'Platform'**
  String get settingsAboutPlatform;

  /// No description provided for @settingsAboutRuntime.
  ///
  /// In en, this message translates to:
  /// **'Runtime'**
  String get settingsAboutRuntime;

  /// No description provided for @settingsAboutCore.
  ///
  /// In en, this message translates to:
  /// **'Native core'**
  String get settingsAboutCore;

  /// No description provided for @settingsAboutCoreUnknown.
  ///
  /// In en, this message translates to:
  /// **'Not connected'**
  String get settingsAboutCoreUnknown;

  /// No description provided for @settingsAboutCopyEnvironment.
  ///
  /// In en, this message translates to:
  /// **'Copy environment'**
  String get settingsAboutCopyEnvironment;

  /// No description provided for @settingsAboutCopied.
  ///
  /// In en, this message translates to:
  /// **'Environment copied to the clipboard.'**
  String get settingsAboutCopied;

  /// No description provided for @settingsAboutCommunity.
  ///
  /// In en, this message translates to:
  /// **'Community & support'**
  String get settingsAboutCommunity;

  /// No description provided for @settingsAboutRepository.
  ///
  /// In en, this message translates to:
  /// **'Source code'**
  String get settingsAboutRepository;

  /// No description provided for @settingsAboutIssues.
  ///
  /// In en, this message translates to:
  /// **'Report an issue'**
  String get settingsAboutIssues;

  /// No description provided for @settingsAboutThanks.
  ///
  /// In en, this message translates to:
  /// **'Thanks'**
  String get settingsAboutThanks;

  /// No description provided for @settingsAboutThanksDesc.
  ///
  /// In en, this message translates to:
  /// **'Open-source projects and services this build relies on.'**
  String get settingsAboutThanksDesc;

  /// No description provided for @settingsAboutLegal.
  ///
  /// In en, this message translates to:
  /// **'Legal'**
  String get settingsAboutLegal;

  /// No description provided for @settingsAboutPrivacy.
  ///
  /// In en, this message translates to:
  /// **'Privacy policy'**
  String get settingsAboutPrivacy;

  /// No description provided for @settingsAboutSecurity.
  ///
  /// In en, this message translates to:
  /// **'Security policy'**
  String get settingsAboutSecurity;

  /// No description provided for @settingsAboutThirdParty.
  ///
  /// In en, this message translates to:
  /// **'Third-party licenses'**
  String get settingsAboutThirdParty;

  /// No description provided for @settingsAboutThirdPartyDesc.
  ///
  /// In en, this message translates to:
  /// **'Open-source packages bundled with this build.'**
  String get settingsAboutThirdPartyDesc;

  /// No description provided for @settingsAboutDisclaimer.
  ///
  /// In en, this message translates to:
  /// **'NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.'**
  String get settingsAboutDisclaimer;

  /// No description provided for @settingsAboutOpenFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not open the link.'**
  String get settingsAboutOpenFailed;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['en', 'zh'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'en':
      return AppLocalizationsEn();
    case 'zh':
      return AppLocalizationsZh();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
