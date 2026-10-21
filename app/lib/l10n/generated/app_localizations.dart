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
  /// **'Add an offline account before launching.'**
  String get launchNoAccount;

  /// No description provided for @launchOfflineOnly.
  ///
  /// In en, this message translates to:
  /// **'Basic launch supports offline accounts only.'**
  String get launchOfflineOnly;

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

  /// No description provided for @downloadSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Versions, loaders, mods, packs, resource packs, and shaders.'**
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

  /// No description provided for @tabLoaders.
  ///
  /// In en, this message translates to:
  /// **'Loaders'**
  String get tabLoaders;

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

  /// No description provided for @loaderInstallNoInstance.
  ///
  /// In en, this message translates to:
  /// **'Select an instance first.'**
  String get loaderInstallNoInstance;

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
  /// **'Sign in with Microsoft device code.'**
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
  /// **'Appearance, Java, network, compliance, and diagnostics.'**
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

  /// No description provided for @settingsMemoryValue.
  ///
  /// In en, this message translates to:
  /// **'{min} – {max} MB'**
  String settingsMemoryValue(int min, int max);

  /// No description provided for @settingsJvmArgs.
  ///
  /// In en, this message translates to:
  /// **'JVM arguments'**
  String get settingsJvmArgs;

  /// No description provided for @settingsJvmArgsValue.
  ///
  /// In en, this message translates to:
  /// **'Default'**
  String get settingsJvmArgsValue;

  /// No description provided for @settingsRuntimeTitle.
  ///
  /// In en, this message translates to:
  /// **'Java and memory'**
  String get settingsRuntimeTitle;

  /// No description provided for @settingsRuntimeHint.
  ///
  /// In en, this message translates to:
  /// **'Memory is applied to the next launch.'**
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

  /// No description provided for @settingsGameArgsValue.
  ///
  /// In en, this message translates to:
  /// **'Default'**
  String get settingsGameArgsValue;

  /// No description provided for @settingsGameArgsHint.
  ///
  /// In en, this message translates to:
  /// **'Optional, appended after the version arguments'**
  String get settingsGameArgsHint;

  /// No description provided for @settingsRuntimeSaved.
  ///
  /// In en, this message translates to:
  /// **'Runtime settings saved.'**
  String get settingsRuntimeSaved;

  /// No description provided for @settingsMemoryInvalid.
  ///
  /// In en, this message translates to:
  /// **'Memory must be 128–65536 MB and min ≤ max.'**
  String get settingsMemoryInvalid;

  /// No description provided for @settingsNetwork.
  ///
  /// In en, this message translates to:
  /// **'Network'**
  String get settingsNetwork;

  /// No description provided for @settingsCompliance.
  ///
  /// In en, this message translates to:
  /// **'Compliance'**
  String get settingsCompliance;

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
  /// **'Motion'**
  String get settingsMotion;

  /// No description provided for @settingsMotionDesc.
  ///
  /// In en, this message translates to:
  /// **'Reduce transitions and background effects.'**
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

  /// Network proxy setting label.
  ///
  /// In en, this message translates to:
  /// **'Proxy'**
  String get settingsProxy;

  /// Proxy follows the operating system.
  ///
  /// In en, this message translates to:
  /// **'System'**
  String get settingsProxySystem;

  /// Download concurrency setting label.
  ///
  /// In en, this message translates to:
  /// **'Downloads'**
  String get settingsDownloads;

  /// Number of parallel download streams.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 stream} other{{count} streams}}'**
  String settingsDownloadsStreams(int count);

  /// Content provider setting label.
  ///
  /// In en, this message translates to:
  /// **'Provider'**
  String get settingsProvider;

  /// Download compliance level label.
  ///
  /// In en, this message translates to:
  /// **'Download level'**
  String get settingsDownloadLevel;

  /// Current download compliance level.
  ///
  /// In en, this message translates to:
  /// **'L0 · official-cn'**
  String get settingsDownloadLevelValue;

  /// Telemetry/consent setting label.
  ///
  /// In en, this message translates to:
  /// **'Consent'**
  String get settingsConsent;

  /// Consent has not been accepted.
  ///
  /// In en, this message translates to:
  /// **'Not accepted'**
  String get settingsConsentNotAccepted;

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
