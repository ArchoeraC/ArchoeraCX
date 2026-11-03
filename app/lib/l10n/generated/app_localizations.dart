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

  /// Tooltip on the command bar launch button naming the selected instance.
  ///
  /// In en, this message translates to:
  /// **'Launch “{name}”'**
  String launchWithInstance(String name);

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

  /// Toast shown when a background pack install finishes.
  ///
  /// In en, this message translates to:
  /// **'Modpack installed.'**
  String get packInstallDone;

  /// Toast shown when a background pack install fails.
  ///
  /// In en, this message translates to:
  /// **'Modpack installation failed.'**
  String get packInstallFailed;

  /// Title of the blocking dialog shown for a destructive archive.
  ///
  /// In en, this message translates to:
  /// **'Unsafe modpack blocked'**
  String get packBlockedTitle;

  /// Explains why a destructive modpack was rejected; no blame on the user.
  ///
  /// In en, this message translates to:
  /// **'This pack tried to write outside its folder, create a link, or expand to an unreasonable size (zip bomb). The launcher stopped before installing it. Do not install it unless you trust its source.'**
  String get packBlockedBody;

  /// shown when a local .mrpack cannot be parsed.
  ///
  /// In en, this message translates to:
  /// **'This file is not a supported modpack.'**
  String get packInspectFailed;

  /// Toast shown when a local .mrpack was automatically repaired before install.
  ///
  /// In en, this message translates to:
  /// **'The pack used Windows path separators, so the launcher saved a backup to {path} and repaired the archive.'**
  String packRepaired(String path);

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

  /// Modpack search header: no existing instance is used.
  ///
  /// In en, this message translates to:
  /// **'Creates a new instance'**
  String get packTargetNewInstance;

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

  /// No description provided for @instanceNameTooLong.
  ///
  /// In en, this message translates to:
  /// **'Name must be {max} characters or fewer.'**
  String instanceNameTooLong(int max);

  /// No description provided for @instanceNameInvalidCharacters.
  ///
  /// In en, this message translates to:
  /// **'Name cannot contain \\ / : * ? \" < > | or control characters.'**
  String get instanceNameInvalidCharacters;

  /// No description provided for @instanceNameInvalidEdges.
  ///
  /// In en, this message translates to:
  /// **'Name cannot start or end with a dot.'**
  String get instanceNameInvalidEdges;

  /// No description provided for @instanceNameReserved.
  ///
  /// In en, this message translates to:
  /// **'Name is a reserved Windows device name. Choose another name.'**
  String get instanceNameReserved;

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

  /// No description provided for @instanceDelete.
  ///
  /// In en, this message translates to:
  /// **'Delete instance'**
  String get instanceDelete;

  /// No description provided for @instanceDuplicate.
  ///
  /// In en, this message translates to:
  /// **'Duplicate instance'**
  String get instanceDuplicate;

  /// No description provided for @instanceDuplicateTitle.
  ///
  /// In en, this message translates to:
  /// **'Duplicate instance'**
  String get instanceDuplicateTitle;

  /// No description provided for @instanceDuplicateName.
  ///
  /// In en, this message translates to:
  /// **'New name'**
  String get instanceDuplicateName;

  /// No description provided for @instanceDuplicateNameHint.
  ///
  /// In en, this message translates to:
  /// **'e.g. 1.21.4 · Fabric copy'**
  String get instanceDuplicateNameHint;

  /// No description provided for @instanceDuplicateSuffix.
  ///
  /// In en, this message translates to:
  /// **'copy'**
  String get instanceDuplicateSuffix;

  /// No description provided for @instanceDuplicateHint.
  ///
  /// In en, this message translates to:
  /// **'Copies the game folder, mods and configs (logs excluded) and keeps the instance settings.'**
  String get instanceDuplicateHint;

  /// No description provided for @instanceDuplicateAction.
  ///
  /// In en, this message translates to:
  /// **'Duplicate'**
  String get instanceDuplicateAction;

  /// Toast after duplicating an instance.
  ///
  /// In en, this message translates to:
  /// **'Created “{name}”.'**
  String instanceDuplicateDone(String name);

  /// No description provided for @instanceDuplicateFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not duplicate the instance.'**
  String get instanceDuplicateFailed;

  /// No description provided for @instanceDuplicateInvalidName.
  ///
  /// In en, this message translates to:
  /// **'Enter an instance name.'**
  String get instanceDuplicateInvalidName;

  /// Confirmation before removing an instance.
  ///
  /// In en, this message translates to:
  /// **'Delete “{name}”?'**
  String instanceDeleteConfirm(String name);

  /// No description provided for @instanceDeleteBody.
  ///
  /// In en, this message translates to:
  /// **'The instance is removed from the launcher list.'**
  String get instanceDeleteBody;

  /// No description provided for @instanceDeleteFiles.
  ///
  /// In en, this message translates to:
  /// **'Also delete local files'**
  String get instanceDeleteFiles;

  /// No description provided for @instanceDeleteFilesHint.
  ///
  /// In en, this message translates to:
  /// **'Removes game files, mods, logs and configs in the instance folder. This cannot be undone.'**
  String get instanceDeleteFilesHint;

  /// No description provided for @instanceDeleteFilesConfirmTitle.
  ///
  /// In en, this message translates to:
  /// **'Delete local files?'**
  String get instanceDeleteFilesConfirmTitle;

  /// Second confirmation showing the instance folder that will be deleted.
  ///
  /// In en, this message translates to:
  /// **'This permanently deletes the folder and everything inside it:\n{path}'**
  String instanceDeleteFilesConfirmBody(String path);

  /// No description provided for @instanceDeleteFilesConfirmAction.
  ///
  /// In en, this message translates to:
  /// **'Delete permanently'**
  String get instanceDeleteFilesConfirmAction;

  /// No description provided for @instanceDeleteDone.
  ///
  /// In en, this message translates to:
  /// **'Instance deleted.'**
  String get instanceDeleteDone;

  /// No description provided for @instanceDeleteDoneFiles.
  ///
  /// In en, this message translates to:
  /// **'Instance and local files deleted.'**
  String get instanceDeleteDoneFiles;

  /// No description provided for @instanceDeleteFilesKeptMissing.
  ///
  /// In en, this message translates to:
  /// **'Instance deleted; the local folder was already gone.'**
  String get instanceDeleteFilesKeptMissing;

  /// No description provided for @instanceDeleteFilesKeptShared.
  ///
  /// In en, this message translates to:
  /// **'Instance deleted, but the folder is shared with another instance or instance root, so it was kept.'**
  String get instanceDeleteFilesKeptShared;

  /// No description provided for @instanceDeleteFilesKeptUnsafe.
  ///
  /// In en, this message translates to:
  /// **'Instance deleted, but the folder path is unsafe, so it was kept.'**
  String get instanceDeleteFilesKeptUnsafe;

  /// No description provided for @instanceDeleteFilesKeptFailed.
  ///
  /// In en, this message translates to:
  /// **'Instance deleted, but the local files could not be removed. Clean them up manually.'**
  String get instanceDeleteFilesKeptFailed;

  /// No description provided for @instanceDeleteFilesKeptOutsideRoot.
  ///
  /// In en, this message translates to:
  /// **'Instance deleted, but the folder is outside the managed instances directory, so it was kept.'**
  String get instanceDeleteFilesKeptOutsideRoot;

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

  /// Shown after a dependency repair was handed to the task center.
  ///
  /// In en, this message translates to:
  /// **'Repair started in the background. Track it in Tasks.'**
  String get instanceCheckRepairQueued;

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

  /// Re-runs the read-only verification after a background repair.
  ///
  /// In en, this message translates to:
  /// **'Check again'**
  String get instanceCheckRecheck;

  /// Status while the dialog re-runs the dependency check.
  ///
  /// In en, this message translates to:
  /// **'Checking again…'**
  String get instanceCheckRechecking;

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

  /// No description provided for @launchLoaderNotInstalled.
  ///
  /// In en, this message translates to:
  /// **'This instance has no loader installed. Install Forge/NeoForge from the instance menu, then launch again.'**
  String get launchLoaderNotInstalled;

  /// No description provided for @launchMemoryWarningTitle.
  ///
  /// In en, this message translates to:
  /// **'Low memory'**
  String get launchMemoryWarningTitle;

  /// No description provided for @launchMemoryWarningLow.
  ///
  /// In en, this message translates to:
  /// **'Only {max} MB is allocated but {recommended} MB is recommended. The game may stutter or crash.'**
  String launchMemoryWarningLow(int max, int recommended);

  /// No description provided for @launchMemoryWarningOver.
  ///
  /// In en, this message translates to:
  /// **'A {max} MB heap leaves little room for the system ({available} MB available).'**
  String launchMemoryWarningOver(int max, int available);

  /// No description provided for @launchMemoryWarningSystem.
  ///
  /// In en, this message translates to:
  /// **'This machine has {total} MB of RAM, so the heap is capped at {max} MB. Heavy modpacks may not fit.'**
  String launchMemoryWarningSystem(int total, int max);

  /// No description provided for @launchAnyway.
  ///
  /// In en, this message translates to:
  /// **'Launch anyway'**
  String get launchAnyway;

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

  /// No description provided for @sourceCurseForgePending.
  ///
  /// In en, this message translates to:
  /// **'CurseForge is unavailable until API access is configured; use Modrinth for now.'**
  String get sourceCurseForgePending;

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

  /// Explicit warning shown when a loader install needs a JVM but none is available.
  ///
  /// In en, this message translates to:
  /// **'{loader} requires Java to install: some processors are not implemented natively yet. Install or select a Java runtime in Java settings first.'**
  String loaderJavaRequired(String loader);

  /// Checkbox in the Forge/NeoForge install dialog; overrides the global preference for this install only.
  ///
  /// In en, this message translates to:
  /// **'Share loader files'**
  String get loaderShareFiles;

  /// No description provided for @loaderShareFilesHint.
  ///
  /// In en, this message translates to:
  /// **'Reuses one libraries copy in the game folder for every instance of this loader build; the merged launch profile always stays in this instance. Untick to keep the libraries inside this instance.'**
  String get loaderShareFilesHint;

  /// Checkbox in the Forge/NeoForge install dialog, on by default; the vendor installer performs the client install.
  ///
  /// In en, this message translates to:
  /// **'Use the official installer (pure Java)'**
  String get loaderPureJava;

  /// No description provided for @loaderPureJavaHint.
  ///
  /// In en, this message translates to:
  /// **'The Forge/NeoForge installer handles downloads and processors; works with 1.12.2 and newer installers.'**
  String get loaderPureJavaHint;

  /// Group label above the two loader install modes.
  ///
  /// In en, this message translates to:
  /// **'Install method'**
  String get loaderInstallMode;

  /// The launcher builds the plan, downloads files and runs covered processors natively; uncovered steps use Java.
  ///
  /// In en, this message translates to:
  /// **'Standard Java flow (launcher-native)'**
  String get loaderStandardJava;

  /// No description provided for @loaderStandardJavaHint.
  ///
  /// In en, this message translates to:
  /// **'The launcher builds the plan and runs covered processors natively; steps that still need Java use the selected runtime. Choose this for 1.7.10 and older installers.'**
  String get loaderStandardJavaHint;

  /// Shown when pure Java mode meets an installer without --installClient.
  ///
  /// In en, this message translates to:
  /// **'This installer has no client CLI (Forge 1.7.10 and older), so pure Java mode cannot drive it. Choose \"Standard Java flow (launcher-native)\" instead.'**
  String get loaderJavaInstallUnsupported;

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

  /// No description provided for @downloadInstallTarget.
  ///
  /// In en, this message translates to:
  /// **'Install to'**
  String get downloadInstallTarget;

  /// No description provided for @downloadTargetExisting.
  ///
  /// In en, this message translates to:
  /// **'An existing instance'**
  String get downloadTargetExisting;

  /// No description provided for @downloadTargetNew.
  ///
  /// In en, this message translates to:
  /// **'A new instance'**
  String get downloadTargetNew;

  /// No description provided for @downloadTargetDirectory.
  ///
  /// In en, this message translates to:
  /// **'A custom folder'**
  String get downloadTargetDirectory;

  /// No description provided for @downloadTargetNewHint.
  ///
  /// In en, this message translates to:
  /// **'A new instance matching the selected game version and loader is created when you install.'**
  String get downloadTargetNewHint;

  /// No description provided for @downloadTargetDirectoryHint.
  ///
  /// In en, this message translates to:
  /// **'Choose a folder; the download is written to the matching content subfolder.'**
  String get downloadTargetDirectoryHint;

  /// No description provided for @downloadDirectoryNotSelected.
  ///
  /// In en, this message translates to:
  /// **'No folder chosen yet.'**
  String get downloadDirectoryNotSelected;

  /// No description provided for @downloadVersionFacetsHint.
  ///
  /// In en, this message translates to:
  /// **'Select a game version and loader first; compatible downloads are listed here.'**
  String get downloadVersionFacetsHint;

  /// No description provided for @downloadInstallSelected.
  ///
  /// In en, this message translates to:
  /// **'Install selected version'**
  String get downloadInstallSelected;

  /// No description provided for @downloadInstalling.
  ///
  /// In en, this message translates to:
  /// **'Installing…'**
  String get downloadInstalling;

  /// Footer summary of the version the user picked on a project page.
  ///
  /// In en, this message translates to:
  /// **'Selected: {version}'**
  String downloadSelectedVersion(String version);

  /// No description provided for @downloadVersionPick.
  ///
  /// In en, this message translates to:
  /// **'Pick a version below to install.'**
  String get downloadVersionPick;

  /// No description provided for @downloadVersionIncompatible.
  ///
  /// In en, this message translates to:
  /// **'This version does not match the target instance. Switch instances or pick another build.'**
  String get downloadVersionIncompatible;

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

  /// Toast after a background download starts.
  ///
  /// In en, this message translates to:
  /// **'“{name}” queued. Track it in Tasks or the queue strip below.'**
  String downloadQueued(String name);

  /// Toast when a background download finishes.
  ///
  /// In en, this message translates to:
  /// **'“{name}” finished downloading.'**
  String downloadFinished(String name);

  /// Toast when a background download fails.
  ///
  /// In en, this message translates to:
  /// **'“{name}” failed to download.'**
  String downloadFailed(String name);

  /// Toast when the user cancels a background download.
  ///
  /// In en, this message translates to:
  /// **'“{name}” download cancelled.'**
  String downloadCancelled(String name);

  /// Active transfers shown in the download page queue strip.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 download in progress} other{{count} downloads in progress}}'**
  String downloadQueueActive(int count);

  /// Byte progress shown in the download page queue strip.
  ///
  /// In en, this message translates to:
  /// **'{done} / {total}'**
  String downloadQueueProgress(String done, String total);

  /// Opens the task center from the queue strip.
  ///
  /// In en, this message translates to:
  /// **'View tasks'**
  String get downloadQueueOpen;

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

  /// How the launcher starts the game: window and extra arguments.
  ///
  /// In en, this message translates to:
  /// **'Launch'**
  String get settingsSectionLaunch;

  /// Settings navigation label for detailed launch defaults.
  ///
  /// In en, this message translates to:
  /// **'Advanced'**
  String get settingsAdvanced;

  /// Explains that the advanced page holds global defaults.
  ///
  /// In en, this message translates to:
  /// **'Defaults for instances without their own overrides; per-instance overrides still win.'**
  String get settingsAdvancedNote;

  /// Advanced section title for memory, GC and JVM defaults.
  ///
  /// In en, this message translates to:
  /// **'Java & memory defaults'**
  String get settingsSectionJavaMemory;

  /// Java settings row that opens the advanced section.
  ///
  /// In en, this message translates to:
  /// **'Advanced launch defaults'**
  String get settingsAdvancedJavaMemory;

  /// Explains where memory and JVM defaults moved.
  ///
  /// In en, this message translates to:
  /// **'Memory, GC and JVM arguments live in the Advanced section.'**
  String get settingsAdvancedJavaMemoryHint;

  /// No description provided for @settingsSectionWindow.
  ///
  /// In en, this message translates to:
  /// **'Game window'**
  String get settingsSectionWindow;

  /// No description provided for @settingsWindowNote.
  ///
  /// In en, this message translates to:
  /// **'Window mode is written as vanilla launch arguments (--width / --height / --fullscreen) and applies to versions that accept them. Maximized is approximated with the primary display\'s usable size.'**
  String get settingsWindowNote;

  /// Desktop-only section title for window behavior around a game launch.
  ///
  /// In en, this message translates to:
  /// **'Launcher behavior'**
  String get settingsSectionBehavior;

  /// Desktop switch that hides the launcher window after a successful game start.
  ///
  /// In en, this message translates to:
  /// **'Hide launcher after start'**
  String get settingsHideAfterLaunch;

  /// Explains how to restore a hidden launcher.
  ///
  /// In en, this message translates to:
  /// **'Hides this window once the game has started; reopen it from the tray icon.'**
  String get settingsHideAfterLaunchDesc;

  /// Desktop switch that quits the launcher when the started game process ends.
  ///
  /// In en, this message translates to:
  /// **'Quit after the game exits'**
  String get settingsQuitAfterGameExit;

  /// Warns that quitting also stops background work.
  ///
  /// In en, this message translates to:
  /// **'Closes the launcher when that game process ends; running downloads and tasks stop with it.'**
  String get settingsQuitAfterGameExitDesc;

  /// Note under the launcher behavior switches, pointing at the existing log viewer.
  ///
  /// In en, this message translates to:
  /// **'Applies to this desktop only. Game logs stay available from the instance menu (View logs) while the game runs.'**
  String get settingsBehaviorNote;

  /// Global selector for windowed, fullscreen or maximized launch.
  ///
  /// In en, this message translates to:
  /// **'Window mode'**
  String get settingsWindowMode;

  /// Explains that maximized is an approximation, since Minecraft has no maximized flag.
  ///
  /// In en, this message translates to:
  /// **'Maximized opens a window sized to the usable screen area; some versions may not strictly maximize it.'**
  String get settingsWindowModeDesc;

  /// Window mode option that opens a regular window.
  ///
  /// In en, this message translates to:
  /// **'Windowed'**
  String get settingsWindowModeWindowed;

  /// Window mode option that starts the game fullscreen.
  ///
  /// In en, this message translates to:
  /// **'Fullscreen'**
  String get settingsWindowModeFullscreen;

  /// Window mode option that approximates a maximized window.
  ///
  /// In en, this message translates to:
  /// **'Maximized'**
  String get settingsWindowModeMaximized;

  /// No description provided for @settingsWindowSize.
  ///
  /// In en, this message translates to:
  /// **'Window size'**
  String get settingsWindowSize;

  /// No description provided for @settingsWindowSizeDesc.
  ///
  /// In en, this message translates to:
  /// **'Initial size of the game window.'**
  String get settingsWindowSizeDesc;

  /// No description provided for @settingsWindowSizeFullscreen.
  ///
  /// In en, this message translates to:
  /// **'Ignored while starting fullscreen.'**
  String get settingsWindowSizeFullscreen;

  /// Hint under the window size selector while maximized is selected.
  ///
  /// In en, this message translates to:
  /// **'Set from the primary display\'s usable size when you choose maximized.'**
  String get settingsWindowSizeMaximized;

  /// Global Linux-only performance switches header.
  ///
  /// In en, this message translates to:
  /// **'Performance (Linux)'**
  String get settingsSectionPerformance;

  /// Explains that the performance switches are convenience wrapper/env presets.
  ///
  /// In en, this message translates to:
  /// **'Applied through a wrapper or environment variables. A custom wrapper or environment value with the same effect takes precedence; these switches do not change the download or compliance settings.'**
  String get settingsPerformanceNote;

  /// Global switch that runs the game through gamemoderun.
  ///
  /// In en, this message translates to:
  /// **'Feral GameMode'**
  String get settingsGameMode;

  /// No description provided for @settingsGameModeDesc.
  ///
  /// In en, this message translates to:
  /// **'Runs the game through gamemoderun when GameMode is installed.'**
  String get settingsGameModeDesc;

  /// Global switch that injects the MangoHud overlay.
  ///
  /// In en, this message translates to:
  /// **'MangoHud'**
  String get settingsMangoHud;

  /// No description provided for @settingsMangoHudDesc.
  ///
  /// In en, this message translates to:
  /// **'Runs the game through the mangohud wrapper (install it first) and also sets MANGOHUD=1 for Vulkan layers.'**
  String get settingsMangoHudDesc;

  /// Global switch that requests the Zink OpenGL-on-Vulkan driver.
  ///
  /// In en, this message translates to:
  /// **'Zink'**
  String get settingsZink;

  /// No description provided for @settingsZinkDesc.
  ///
  /// In en, this message translates to:
  /// **'Requests the Zink OpenGL-on-Vulkan driver through the environment.'**
  String get settingsZinkDesc;

  /// Linux-only developer section title for native library overrides.
  ///
  /// In en, this message translates to:
  /// **'Advanced native libraries (developer)'**
  String get settingsSectionAdvancedNative;

  /// Warning shown for advanced native library overrides.
  ///
  /// In en, this message translates to:
  /// **'Only affects the local game launch and is unrelated to the download/compliance developer mode. Set these paths only when you know the ABI of the library; a mismatched GLFW, OpenAL or SDL build can crash the game before the main menu.'**
  String get settingsNativeWarning;

  /// Switch that reveals GLFW/OpenAL/SDL path overrides.
  ///
  /// In en, this message translates to:
  /// **'Enable native library overrides'**
  String get settingsDeveloperMode;

  /// Explains developer mode scope and that stored paths stay parked while off.
  ///
  /// In en, this message translates to:
  /// **'Runs only for the local game. Off keeps the stored paths but does not apply them.'**
  String get settingsDeveloperModeDesc;

  /// Absolute path field for a custom GLFW shared library.
  ///
  /// In en, this message translates to:
  /// **'GLFW library'**
  String get settingsNativeGlfw;

  /// Absolute path field for a custom OpenAL shared library.
  ///
  /// In en, this message translates to:
  /// **'OpenAL library'**
  String get settingsNativeOpenAL;

  /// Absolute path field for a custom SDL shared library.
  ///
  /// In en, this message translates to:
  /// **'SDL library'**
  String get settingsNativeSdl;

  /// Placeholder in the native library path fields.
  ///
  /// In en, this message translates to:
  /// **'/absolute/path/to/library.so'**
  String get settingsNativePathHint;

  /// Inline validation for a relative native library path.
  ///
  /// In en, this message translates to:
  /// **'Enter an absolute file path.'**
  String get settingsNativePathInvalid;

  /// Tooltip on the native library file picker button.
  ///
  /// In en, this message translates to:
  /// **'Choose file'**
  String get settingsNativePickFile;

  /// File picker type group label for native libraries.
  ///
  /// In en, this message translates to:
  /// **'Native library'**
  String get settingsNativePickerLabel;

  /// Global default for the Forge/NeoForge install dialog checkbox.
  ///
  /// In en, this message translates to:
  /// **'Share loader files'**
  String get settingsShareLoaderFiles;

  /// No description provided for @settingsShareLoaderFilesDesc.
  ///
  /// In en, this message translates to:
  /// **'Forge/NeoForge libraries and processor output are installed once in the game folder and reused by every instance of that loader build. Each instance keeps its merged launch profile in its own folder. Untick to keep the libraries inside each instance.'**
  String get settingsShareLoaderFilesDesc;

  /// Global default for the loader install mode.
  ///
  /// In en, this message translates to:
  /// **'Prefer the official installer (pure Java)'**
  String get settingsPreferPureJavaLoaderInstall;

  /// No description provided for @settingsPreferPureJavaLoaderInstallDesc.
  ///
  /// In en, this message translates to:
  /// **'The loader install dialog starts with \"Use the official installer (pure Java)\" selected; each install can still switch to the standard Java flow. Installers without a client CLI (Forge 1.7.10 and older) fall back automatically.'**
  String get settingsPreferPureJavaLoaderInstallDesc;

  /// No description provided for @settingsSectionGameArgs.
  ///
  /// In en, this message translates to:
  /// **'Extra game arguments'**
  String get settingsSectionGameArgs;

  /// No description provided for @settingsGameArgsNote.
  ///
  /// In en, this message translates to:
  /// **'Appended after the vanilla arguments and split on spaces; leave empty for none.'**
  String get settingsGameArgsNote;

  /// Global lifecycle command section title in the Launch settings.
  ///
  /// In en, this message translates to:
  /// **'Commands'**
  String get settingsSectionCommands;

  /// Note under the global lifecycle command section.
  ///
  /// In en, this message translates to:
  /// **'Run once per launch unless an instance overrides them; empty fields run nothing.'**
  String get settingsCommandsNote;

  /// Label for the global command that runs before the instance is prepared.
  ///
  /// In en, this message translates to:
  /// **'Pre-load command'**
  String get settingsPreLoadCommand;

  /// Helper text under the global pre-load command field.
  ///
  /// In en, this message translates to:
  /// **'Runs before the instance is prepared.'**
  String get settingsPreLoadCommandDesc;

  /// Label for the global command that runs immediately before Java starts.
  ///
  /// In en, this message translates to:
  /// **'Pre-launch command'**
  String get settingsPreLaunchCommand;

  /// Helper text under the global pre-launch command field.
  ///
  /// In en, this message translates to:
  /// **'Runs after preparation, immediately before Java starts. A failure aborts the launch.'**
  String get settingsPreLaunchCommandDesc;

  /// Label for the global command that runs after the game exits.
  ///
  /// In en, this message translates to:
  /// **'Post-exit command'**
  String get settingsPostExitCommand;

  /// Helper text under the global post-exit command field.
  ///
  /// In en, this message translates to:
  /// **'Runs after the game exits. A failure is only logged.'**
  String get settingsPostExitCommandDesc;

  /// Hint shown inside an empty lifecycle command field.
  ///
  /// In en, this message translates to:
  /// **'Leave empty to run nothing.'**
  String get settingsCommandEmptyHint;

  /// Safety and environment help under the global lifecycle command fields.
  ///
  /// In en, this message translates to:
  /// **'Commands run as your user, without a shell, in the instance folder. \$INST_NAME, \$INST_ID, \$INST_DIR, \$INST_MC_DIR, \$INST_JAVA and \$INST_JAVA_ARGS (account secrets redacted) are available as environment variables. Do not put account tokens in commands.'**
  String get settingsCommandsHelp;

  /// Global launch-process options: wrapper command and child environment.
  ///
  /// In en, this message translates to:
  /// **'Wrapper & environment'**
  String get settingsSectionProcess;

  /// Safety note under the global wrapper/environment section.
  ///
  /// In en, this message translates to:
  /// **'Applied to the game process only. Wrapper commands run as your user; environment values are stored locally and never logged.'**
  String get settingsProcessNote;

  /// Label for the global process wrapper command field.
  ///
  /// In en, this message translates to:
  /// **'Wrapper command'**
  String get settingsWrapperCommand;

  /// Example wrapper commands shown inside the global wrapper field.
  ///
  /// In en, this message translates to:
  /// **'e.g. gamemoderun, mangohud, prime-run'**
  String get settingsWrapperCommandHint;

  /// Helper text under the global wrapper command field.
  ///
  /// In en, this message translates to:
  /// **'Prepended to the Java command; leave empty for none.'**
  String get settingsWrapperCommandHelp;

  /// Label for the global child environment field.
  ///
  /// In en, this message translates to:
  /// **'Environment variables'**
  String get settingsEnvironment;

  /// Format hint for the multiline environment field.
  ///
  /// In en, this message translates to:
  /// **'KEY=VALUE'**
  String get settingsEnvironmentHint;

  /// Helper text under the global environment field.
  ///
  /// In en, this message translates to:
  /// **'One KEY=VALUE per line. Blank lines are ignored.'**
  String get settingsEnvironmentHelp;

  /// Validation error when a wrapper command exceeds the contract limit.
  ///
  /// In en, this message translates to:
  /// **'Wrapper commands may be at most 4096 characters.'**
  String get launchWrapperCommandTooLong;

  /// Validation error when a lifecycle command exceeds the contract limit.
  ///
  /// In en, this message translates to:
  /// **'Commands may be at most 4096 characters.'**
  String get launchCommandTooLong;

  /// Validation error for NUL or CR/LF inside a lifecycle command.
  ///
  /// In en, this message translates to:
  /// **'Commands cannot contain NUL or line breaks.'**
  String get launchCommandInvalidCharacter;

  /// Validation error for a lifecycle command with an unclosed quote.
  ///
  /// In en, this message translates to:
  /// **'Unclosed {quote} quote.'**
  String launchCommandUnclosedQuote(String quote);

  /// Environment block line without an equals sign.
  ///
  /// In en, this message translates to:
  /// **'Line {line}: enter KEY=VALUE.'**
  String launchEnvironmentMissingSeparator(int line);

  /// Environment key does not match the identifier pattern.
  ///
  /// In en, this message translates to:
  /// **'Line {line}: \"{key}\" is not a valid variable name.'**
  String launchEnvironmentInvalidKey(int line, String key);

  /// Environment block repeats a key.
  ///
  /// In en, this message translates to:
  /// **'Line {line}: \"{key}\" is already defined.'**
  String launchEnvironmentDuplicateKey(int line, String key);

  /// Environment key is owned by the JVM/launcher and cannot be overridden.
  ///
  /// In en, this message translates to:
  /// **'Line {line}: \"{key}\" cannot be set here.'**
  String launchEnvironmentReservedKey(int line, String key);

  /// Environment block exceeds the entry limit.
  ///
  /// In en, this message translates to:
  /// **'At most {count} environment variables are allowed.'**
  String launchEnvironmentTooMany(int count);

  /// Environment value exceeds the length limit or contains a NUL.
  ///
  /// In en, this message translates to:
  /// **'Line {line}: values must be at most 4096 characters and cannot contain NUL.'**
  String launchEnvironmentInvalidValue(int line);

  /// No description provided for @settingsSectionTasks.
  ///
  /// In en, this message translates to:
  /// **'Tasks & logs'**
  String get settingsSectionTasks;

  /// No description provided for @settingsTasksNote.
  ///
  /// In en, this message translates to:
  /// **'These preferences only affect the interface and are not written to settings.json.'**
  String get settingsTasksNote;

  /// No description provided for @settingsTaskHistory.
  ///
  /// In en, this message translates to:
  /// **'Task history'**
  String get settingsTaskHistory;

  /// No description provided for @settingsTaskHistoryDesc.
  ///
  /// In en, this message translates to:
  /// **'How many finished tasks stay in the task center; running tasks are never pruned.'**
  String get settingsTaskHistoryDesc;

  /// No description provided for @settingsLogLines.
  ///
  /// In en, this message translates to:
  /// **'Default log lines'**
  String get settingsLogLines;

  /// No description provided for @settingsLogLinesDesc.
  ///
  /// In en, this message translates to:
  /// **'Tail size loaded when the log viewer opens.'**
  String get settingsLogLinesDesc;

  /// No description provided for @settingsSectionNotifications.
  ///
  /// In en, this message translates to:
  /// **'Notifications'**
  String get settingsSectionNotifications;

  /// No description provided for @settingsNotificationsNote.
  ///
  /// In en, this message translates to:
  /// **'Desktop notifications use the system notification center, which can mute them separately.'**
  String get settingsNotificationsNote;

  /// No description provided for @settingsNotifyDesktop.
  ///
  /// In en, this message translates to:
  /// **'Desktop notifications'**
  String get settingsNotifyDesktop;

  /// No description provided for @settingsNotifyDesktopDesc.
  ///
  /// In en, this message translates to:
  /// **'Notify on sign-in and similar events.'**
  String get settingsNotifyDesktopDesc;

  /// No description provided for @settingsNotifyDownloads.
  ///
  /// In en, this message translates to:
  /// **'Download notifications'**
  String get settingsNotifyDownloads;

  /// No description provided for @settingsNotifyDownloadsDesc.
  ///
  /// In en, this message translates to:
  /// **'Notify when a background download finishes or fails.'**
  String get settingsNotifyDownloadsDesc;

  /// No description provided for @settingsToastDuration.
  ///
  /// In en, this message translates to:
  /// **'Toast duration'**
  String get settingsToastDuration;

  /// No description provided for @settingsToastDurationDesc.
  ///
  /// In en, this message translates to:
  /// **'How long success/info toasts stay; errors stay longer.'**
  String get settingsToastDurationDesc;

  /// No description provided for @settingsStorage.
  ///
  /// In en, this message translates to:
  /// **'Storage'**
  String get settingsStorage;

  /// No description provided for @settingsDownload.
  ///
  /// In en, this message translates to:
  /// **'Downloads'**
  String get settingsDownload;

  /// No description provided for @settingsDownloadSource.
  ///
  /// In en, this message translates to:
  /// **'Source priority'**
  String get settingsDownloadSource;

  /// No description provided for @settingsDownloadTuning.
  ///
  /// In en, this message translates to:
  /// **'Concurrency and connections'**
  String get settingsDownloadTuning;

  /// No description provided for @settingsDownloadSourceDesc.
  ///
  /// In en, this message translates to:
  /// **'Which host is tried first. The other side stays as a fallback.'**
  String get settingsDownloadSourceDesc;

  /// No description provided for @settingsDownloadSourceAuto.
  ///
  /// In en, this message translates to:
  /// **'Automatic'**
  String get settingsDownloadSourceAuto;

  /// No description provided for @settingsDownloadSourceAutoDesc.
  ///
  /// In en, this message translates to:
  /// **'Follows the detected region; mirrors are used when the official host is slow or blocked.'**
  String get settingsDownloadSourceAutoDesc;

  /// No description provided for @settingsDownloadSourceOfficial.
  ///
  /// In en, this message translates to:
  /// **'Official first'**
  String get settingsDownloadSourceOfficial;

  /// No description provided for @settingsDownloadAutoResolvedMirror.
  ///
  /// In en, this message translates to:
  /// **'Automatic · using mirrors now (mainland China)'**
  String get settingsDownloadAutoResolvedMirror;

  /// No description provided for @settingsDownloadAutoResolvedOfficial.
  ///
  /// In en, this message translates to:
  /// **'Automatic · using official hosts now'**
  String get settingsDownloadAutoResolvedOfficial;

  /// No description provided for @settingsDownloadAutoDetecting.
  ///
  /// In en, this message translates to:
  /// **'Automatic · detecting the region…'**
  String get settingsDownloadAutoDetecting;

  /// No description provided for @settingsDownloadSourceMirror.
  ///
  /// In en, this message translates to:
  /// **'Mirror first'**
  String get settingsDownloadSourceMirror;

  /// No description provided for @settingsDownloadConcurrency.
  ///
  /// In en, this message translates to:
  /// **'Parallel downloads'**
  String get settingsDownloadConcurrency;

  /// No description provided for @settingsDownloadConcurrencyDesc.
  ///
  /// In en, this message translates to:
  /// **'Files (and large-file chunks) fetched at the same time, 1–64.'**
  String get settingsDownloadConcurrencyDesc;

  /// No description provided for @settingsDownloadPerHost.
  ///
  /// In en, this message translates to:
  /// **'Connections per host'**
  String get settingsDownloadPerHost;

  /// No description provided for @settingsDownloadPerHostDesc.
  ///
  /// In en, this message translates to:
  /// **'Caps how many streams one host may use, 0 = unlimited.'**
  String get settingsDownloadPerHostDesc;

  /// No description provided for @settingsDownloadHighConcurrencyTitle.
  ///
  /// In en, this message translates to:
  /// **'Enable very high concurrency?'**
  String get settingsDownloadHighConcurrencyTitle;

  /// No description provided for @settingsDownloadHighConcurrencyWarning.
  ///
  /// In en, this message translates to:
  /// **'Unless you know what you are doing, do not do this: more than 32 streams sharply increases CPU, memory and network usage, and heavy chunking accelerates disk wear.'**
  String get settingsDownloadHighConcurrencyWarning;

  /// No description provided for @settingsDownloadHighConcurrencyContinue.
  ///
  /// In en, this message translates to:
  /// **'Enable anyway'**
  String get settingsDownloadHighConcurrencyContinue;

  /// No description provided for @settingsDownloadHighConcurrencyActive.
  ///
  /// In en, this message translates to:
  /// **'High concurrency enabled ({streams} streams, {perHost} per host): expect much higher resource usage and disk wear.'**
  String settingsDownloadHighConcurrencyActive(int streams, int perHost);

  /// No description provided for @settingsDownloadProxy.
  ///
  /// In en, this message translates to:
  /// **'Download proxy'**
  String get settingsDownloadProxy;

  /// Files larger than this use ranged chunks; turn it off to always download a single stream.
  ///
  /// In en, this message translates to:
  /// **'Range threshold'**
  String get settingsDownloadRangeThreshold;

  /// No description provided for @settingsDownloadRangeThresholdDesc.
  ///
  /// In en, this message translates to:
  /// **'Size above which a file is split into parallel chunks.'**
  String get settingsDownloadRangeThresholdDesc;

  /// No description provided for @settingsRangeDisabled.
  ///
  /// In en, this message translates to:
  /// **'Chunking off'**
  String get settingsRangeDisabled;

  /// Size of each ranged request; smaller chunks resume more cheaply but need more requests.
  ///
  /// In en, this message translates to:
  /// **'Chunk size'**
  String get settingsDownloadChunk;

  /// No description provided for @settingsDownloadChunkDesc.
  ///
  /// In en, this message translates to:
  /// **'Size of one ranged request.'**
  String get settingsDownloadChunkDesc;

  /// Launch logs kept per instance.
  ///
  /// In en, this message translates to:
  /// **'Logs kept'**
  String get settingsLogRetentionFiles;

  /// No description provided for @settingsLogRetentionFilesDesc.
  ///
  /// In en, this message translates to:
  /// **'Oldest logs are deleted first; the newest one is always kept.'**
  String get settingsLogRetentionFilesDesc;

  /// Total size of all launch logs per instance.
  ///
  /// In en, this message translates to:
  /// **'Log storage cap'**
  String get settingsLogRetentionTotal;

  /// No description provided for @settingsLogRetentionTotalDesc.
  ///
  /// In en, this message translates to:
  /// **'Pruning also starts from the oldest log.'**
  String get settingsLogRetentionTotalDesc;

  /// No description provided for @settingsDownloadProxySystem.
  ///
  /// In en, this message translates to:
  /// **'Follow the system proxy'**
  String get settingsDownloadProxySystem;

  /// No description provided for @settingsDownloadProxyHint.
  ///
  /// In en, this message translates to:
  /// **'http://host:port or socks5h://host:port. Leave empty to follow the system proxy.'**
  String get settingsDownloadProxyHint;

  /// No description provided for @settingsDownloadProxyInvalid.
  ///
  /// In en, this message translates to:
  /// **'Enter an http(s) or socks5 proxy URL, e.g. http://127.0.0.1:7890.'**
  String get settingsDownloadProxyInvalid;

  /// No description provided for @settingsDownloadNote.
  ///
  /// In en, this message translates to:
  /// **'Higher values finish faster on fast links but use more bandwidth and connections; lower them on metered or unstable networks.'**
  String get settingsDownloadNote;

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

  /// Opens the application data folder in the system file manager.
  ///
  /// In en, this message translates to:
  /// **'Open data folder'**
  String get settingsOpenDataRoot;

  /// No description provided for @settingsOpenDataRootDesc.
  ///
  /// In en, this message translates to:
  /// **'Show settings, logs and cached files.'**
  String get settingsOpenDataRootDesc;

  /// Opens the default instance root in the system file manager.
  ///
  /// In en, this message translates to:
  /// **'Open default instances folder'**
  String get settingsOpenInstanceRoot;

  /// No description provided for @settingsOpenInstanceRootDesc.
  ///
  /// In en, this message translates to:
  /// **'Show the folder that holds your instances.'**
  String get settingsOpenInstanceRootDesc;

  /// No description provided for @settingsOpenFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not open that folder.'**
  String get settingsOpenFailed;

  /// Page opened when the launcher starts; applied on the next launch.
  ///
  /// In en, this message translates to:
  /// **'Startup page'**
  String get settingsStartPage;

  /// No description provided for @settingsStartPageDesc.
  ///
  /// In en, this message translates to:
  /// **'Applied the next time the launcher starts.'**
  String get settingsStartPageDesc;

  /// No description provided for @startPageDashboard.
  ///
  /// In en, this message translates to:
  /// **'Dashboard'**
  String get startPageDashboard;

  /// No description provided for @startPageInstances.
  ///
  /// In en, this message translates to:
  /// **'Instances'**
  String get startPageInstances;

  /// No description provided for @startPageDownload.
  ///
  /// In en, this message translates to:
  /// **'Download'**
  String get startPageDownload;

  /// No description provided for @startPageAccounts.
  ///
  /// In en, this message translates to:
  /// **'Accounts'**
  String get startPageAccounts;

  /// No description provided for @startPageWiki.
  ///
  /// In en, this message translates to:
  /// **'Wiki'**
  String get startPageWiki;

  /// No description provided for @startPageSettings.
  ///
  /// In en, this message translates to:
  /// **'Settings'**
  String get startPageSettings;

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

  /// No description provided for @settingsCloseToTray.
  ///
  /// In en, this message translates to:
  /// **'Close to tray'**
  String get settingsCloseToTray;

  /// No description provided for @settingsCloseToTrayDesc.
  ///
  /// In en, this message translates to:
  /// **'Hide the main window in the system tray instead of quitting.'**
  String get settingsCloseToTrayDesc;

  /// No description provided for @trayShow.
  ///
  /// In en, this message translates to:
  /// **'Show window'**
  String get trayShow;

  /// No description provided for @trayQuit.
  ///
  /// In en, this message translates to:
  /// **'Quit'**
  String get trayQuit;

  /// No description provided for @settingsCleanPartials.
  ///
  /// In en, this message translates to:
  /// **'Clean partial downloads'**
  String get settingsCleanPartials;

  /// No description provided for @settingsCleanPartialsDesc.
  ///
  /// In en, this message translates to:
  /// **'Removes `.part` files untouched for more than three days; active downloads are kept.'**
  String get settingsCleanPartialsDesc;

  /// No description provided for @settingsCleanPartialsBody.
  ///
  /// In en, this message translates to:
  /// **'Every partial download untouched for more than three days is deleted from the instance folders. This cannot be undone.'**
  String get settingsCleanPartialsBody;

  /// Toast after cleaning partial downloads.
  ///
  /// In en, this message translates to:
  /// **'Deleted {count} partial files, freed {size}.'**
  String settingsCleanPartialsDone(int count, String size);

  /// No description provided for @settingsCleanPartialsNone.
  ///
  /// In en, this message translates to:
  /// **'No stale partial downloads found.'**
  String get settingsCleanPartialsNone;

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

  /// No description provided for @javaInstallAction.
  ///
  /// In en, this message translates to:
  /// **'Install Java'**
  String get javaInstallAction;

  /// No description provided for @javaInstallPageSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Choose a JDK distribution, version and install location.'**
  String get javaInstallPageSubtitle;

  /// No description provided for @javaInstallProviderLabel.
  ///
  /// In en, this message translates to:
  /// **'Distribution'**
  String get javaInstallProviderLabel;

  /// No description provided for @javaInstallMajorLabel.
  ///
  /// In en, this message translates to:
  /// **'Java version'**
  String get javaInstallMajorLabel;

  /// No description provided for @javaInstallVersionsLabel.
  ///
  /// In en, this message translates to:
  /// **'Available builds'**
  String get javaInstallVersionsLabel;

  /// No description provided for @javaInstallSecurityHint.
  ///
  /// In en, this message translates to:
  /// **'Only the latest patch of each major is recommended; historical builds may contain known CVEs.'**
  String get javaInstallSecurityHint;

  /// No description provided for @javaInstallOutdated.
  ///
  /// In en, this message translates to:
  /// **'Historical'**
  String get javaInstallOutdated;

  /// No description provided for @javaInstallOutdatedSelected.
  ///
  /// In en, this message translates to:
  /// **'This build is not the latest patch and may contain known CVEs.'**
  String get javaInstallOutdatedSelected;

  /// No description provided for @javaInstallAllowOutdated.
  ///
  /// In en, this message translates to:
  /// **'Allow historical builds'**
  String get javaInstallAllowOutdated;

  /// No description provided for @javaInstallAllowOutdatedHint.
  ///
  /// In en, this message translates to:
  /// **'Only enable when you need this exact build.'**
  String get javaInstallAllowOutdatedHint;

  /// No description provided for @javaInstallCveTitle.
  ///
  /// In en, this message translates to:
  /// **'Security advisories'**
  String get javaInstallCveTitle;

  /// No description provided for @javaInstallCveChecking.
  ///
  /// In en, this message translates to:
  /// **'Checking NVD/OSV for known CVEs…'**
  String get javaInstallCveChecking;

  /// No description provided for @javaInstallCveNone.
  ///
  /// In en, this message translates to:
  /// **'No known CVEs affect this build.'**
  String get javaInstallCveNone;

  /// No description provided for @javaInstallCveUnavailable.
  ///
  /// In en, this message translates to:
  /// **'CVE data is unavailable; this build was not checked.'**
  String get javaInstallCveUnavailable;

  /// No description provided for @javaInstallCveCount.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 known CVE affects this build.} other{{count} known CVEs affect this build.}}'**
  String javaInstallCveCount(int count);

  /// No description provided for @javaInstallCveAcknowledge.
  ///
  /// In en, this message translates to:
  /// **'I understand this build has known CVEs'**
  String get javaInstallCveAcknowledge;

  /// No description provided for @javaInstallCveAcknowledgeHint.
  ///
  /// In en, this message translates to:
  /// **'Install stays disabled until you acknowledge the advisories.'**
  String get javaInstallCveAcknowledgeHint;

  /// No description provided for @javaInstallCveScore.
  ///
  /// In en, this message translates to:
  /// **'CVSS {score}'**
  String javaInstallCveScore(String score);

  /// No description provided for @javaInstallCveOpen.
  ///
  /// In en, this message translates to:
  /// **'Open advisory'**
  String get javaInstallCveOpen;

  /// No description provided for @javaInstallLatestOnly.
  ///
  /// In en, this message translates to:
  /// **'This vendor publishes only the latest build of each major.'**
  String get javaInstallLatestOnly;

  /// Target triple shown above the build list.
  ///
  /// In en, this message translates to:
  /// **'{os} · {arch} · {archive}'**
  String javaInstallPlatform(String os, String arch, String archive);

  /// No description provided for @javaInstallTargetPlatform.
  ///
  /// In en, this message translates to:
  /// **'Target platform'**
  String get javaInstallTargetPlatform;

  /// No description provided for @javaInstallCurrentDevice.
  ///
  /// In en, this message translates to:
  /// **'Current device: {os} · {arch}'**
  String javaInstallCurrentDevice(String os, String arch);

  /// No description provided for @javaInstallPlatformBadge.
  ///
  /// In en, this message translates to:
  /// **'Incompatible'**
  String get javaInstallPlatformBadge;

  /// No description provided for @javaInstallPlatformIncompatible.
  ///
  /// In en, this message translates to:
  /// **'This JDK targets a different platform and cannot run on this device.'**
  String get javaInstallPlatformIncompatible;

  /// No description provided for @javaInstallPlatformIncompatibleHint.
  ///
  /// In en, this message translates to:
  /// **'You can still download and keep it for another machine.'**
  String get javaInstallPlatformIncompatibleHint;

  /// No description provided for @javaInstallRiskTitle.
  ///
  /// In en, this message translates to:
  /// **'Confirm installation risk'**
  String get javaInstallRiskTitle;

  /// No description provided for @javaInstallRiskPlatform.
  ///
  /// In en, this message translates to:
  /// **'Target platform {targetOs} · {targetArch} does not match this device ({hostOs} · {hostArch}). The installed JDK cannot run here.'**
  String javaInstallRiskPlatform(
    String targetOs,
    String targetArch,
    String hostOs,
    String hostArch,
  );

  /// No description provided for @javaInstallRiskCve.
  ///
  /// In en, this message translates to:
  /// **'{count} known CVE(s) affect this build:\n{ids}'**
  String javaInstallRiskCve(int count, String ids);

  /// No description provided for @javaInstallRiskAction.
  ///
  /// In en, this message translates to:
  /// **'Install anyway'**
  String get javaInstallRiskAction;

  /// No description provided for @javaInstallForeignResult.
  ///
  /// In en, this message translates to:
  /// **'Downloaded for {os} · {arch}; it cannot run on this device.'**
  String javaInstallForeignResult(String os, String arch);

  /// No description provided for @javaInstallIncompatibleUse.
  ///
  /// In en, this message translates to:
  /// **'This JDK targets another platform and cannot be selected on this device.'**
  String get javaInstallIncompatibleUse;

  /// No description provided for @javaInstallRecommended.
  ///
  /// In en, this message translates to:
  /// **'Recommended'**
  String get javaInstallRecommended;

  /// No description provided for @javaInstallLts.
  ///
  /// In en, this message translates to:
  /// **'LTS'**
  String get javaInstallLts;

  /// Archive type and download size on one build row.
  ///
  /// In en, this message translates to:
  /// **'{archive} · {size}'**
  String javaInstallArchiveMeta(String archive, String size);

  /// No description provided for @javaInstallLoadFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not load the JDK catalogue.'**
  String get javaInstallLoadFailed;

  /// No description provided for @javaInstallFailed.
  ///
  /// In en, this message translates to:
  /// **'Installation failed.'**
  String get javaInstallFailed;

  /// No description provided for @javaInstallMajorsFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not load Java versions.'**
  String get javaInstallMajorsFailed;

  /// No description provided for @javaInstallVersionsFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not load builds.'**
  String get javaInstallVersionsFailed;

  /// No description provided for @javaInstallEmptyVersions.
  ///
  /// In en, this message translates to:
  /// **'No builds are available for this OS.'**
  String get javaInstallEmptyVersions;

  /// No description provided for @javaInstallEmptyVersionsHint.
  ///
  /// In en, this message translates to:
  /// **'Try another distribution or Java version.'**
  String get javaInstallEmptyVersionsHint;

  /// No description provided for @javaInstallDirectory.
  ///
  /// In en, this message translates to:
  /// **'Install directory'**
  String get javaInstallDirectory;

  /// No description provided for @javaInstallDirectoryHint.
  ///
  /// In en, this message translates to:
  /// **'The JDK is written directly into this folder. It must be empty or not exist yet.'**
  String get javaInstallDirectoryHint;

  /// No description provided for @javaInstallDefaultDirectory.
  ///
  /// In en, this message translates to:
  /// **'Default location'**
  String get javaInstallDefaultDirectory;

  /// No description provided for @javaInstallCustomDirectory.
  ///
  /// In en, this message translates to:
  /// **'Custom location'**
  String get javaInstallCustomDirectory;

  /// No description provided for @javaInstallChooseDirectory.
  ///
  /// In en, this message translates to:
  /// **'Choose folder'**
  String get javaInstallChooseDirectory;

  /// No description provided for @javaInstallUseDefault.
  ///
  /// In en, this message translates to:
  /// **'Use default'**
  String get javaInstallUseDefault;

  /// No description provided for @javaInstallStart.
  ///
  /// In en, this message translates to:
  /// **'Install JDK'**
  String get javaInstallStart;

  /// No description provided for @javaInstallDownloadOnlyAction.
  ///
  /// In en, this message translates to:
  /// **'Download archive'**
  String get javaInstallDownloadOnlyAction;

  /// No description provided for @javaInstallInstalling.
  ///
  /// In en, this message translates to:
  /// **'Installing…'**
  String get javaInstallInstalling;

  /// No description provided for @javaInstallDownloading.
  ///
  /// In en, this message translates to:
  /// **'Downloading the JDK archive…'**
  String get javaInstallDownloading;

  /// No description provided for @javaInstallExtracting.
  ///
  /// In en, this message translates to:
  /// **'Extracting the verified archive…'**
  String get javaInstallExtracting;

  /// No description provided for @javaInstallCancelling.
  ///
  /// In en, this message translates to:
  /// **'Cancelling…'**
  String get javaInstallCancelling;

  /// No description provided for @javaInstallCancelled.
  ///
  /// In en, this message translates to:
  /// **'Installation cancelled.'**
  String get javaInstallCancelled;

  /// Result banner after a JDK archive was installed.
  ///
  /// In en, this message translates to:
  /// **'Java {version} installed.'**
  String javaInstallSuccess(String version);

  /// No description provided for @javaInstallUseRuntime.
  ///
  /// In en, this message translates to:
  /// **'Use this Java'**
  String get javaInstallUseRuntime;

  /// No description provided for @javaInstallBackToJava.
  ///
  /// In en, this message translates to:
  /// **'Back to Java'**
  String get javaInstallBackToJava;

  /// No description provided for @javaInstallSelected.
  ///
  /// In en, this message translates to:
  /// **'Java runtime selected.'**
  String get javaInstallSelected;

  /// No description provided for @javaInstallDirectoryRequired.
  ///
  /// In en, this message translates to:
  /// **'Choose an install directory first.'**
  String get javaInstallDirectoryRequired;

  /// No description provided for @javaInstallVersionRequired.
  ///
  /// In en, this message translates to:
  /// **'Choose a build first.'**
  String get javaInstallVersionRequired;

  /// No description provided for @javaInstallProvidersEmpty.
  ///
  /// In en, this message translates to:
  /// **'No JDK vendors are registered.'**
  String get javaInstallProvidersEmpty;

  /// No description provided for @javaInstallExtractOptions.
  ///
  /// In en, this message translates to:
  /// **'Archive handling'**
  String get javaInstallExtractOptions;

  /// No description provided for @javaInstallAutoExtract.
  ///
  /// In en, this message translates to:
  /// **'Extract automatically'**
  String get javaInstallAutoExtract;

  /// No description provided for @javaInstallAutoExtractHint.
  ///
  /// In en, this message translates to:
  /// **'Extract into the install directory; when off, only the archive is downloaded next to it.'**
  String get javaInstallAutoExtractHint;

  /// No description provided for @javaInstallAutoDelete.
  ///
  /// In en, this message translates to:
  /// **'Delete the archive after extraction'**
  String get javaInstallAutoDelete;

  /// No description provided for @javaInstallAutoDeleteHint.
  ///
  /// In en, this message translates to:
  /// **'When off, the tar.gz/zip is kept next to the install directory.'**
  String get javaInstallAutoDeleteHint;

  /// No description provided for @javaInstallArchiveOnly.
  ///
  /// In en, this message translates to:
  /// **'Archive downloaded, not extracted.'**
  String get javaInstallArchiveOnly;

  /// Shown when the downloaded JDK archive was retained.
  ///
  /// In en, this message translates to:
  /// **'Archive kept: {path}'**
  String javaInstallArchiveKept(String path);

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
  /// **'ArchoeraCX is an independent launcher and is not an official Minecraft product. Minecraft is a trademark of Mojang Synergies AB.'**
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
  /// **'No accounts yet. Add a Microsoft or other account to launch the game.'**
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
  /// **'Third-party'**
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

  /// Opens the instance directory in the system file manager.
  ///
  /// In en, this message translates to:
  /// **'Open instance folder'**
  String get instanceOpenFolder;

  /// Tooltip of the marker shown when an instance overrides launch settings.
  ///
  /// In en, this message translates to:
  /// **'Overrides global settings'**
  String get instanceOverridesBadge;

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

  /// Switch that lets the launcher size the heap itself.
  ///
  /// In en, this message translates to:
  /// **'Automatic memory'**
  String get settingsMemoryAuto;

  /// Explains automatic heap sizing.
  ///
  /// In en, this message translates to:
  /// **'Picks the heap from the game version, loader, enabled mods and installed RAM.'**
  String get settingsMemoryAutoDesc;

  /// Shown while automatic sizing is on.
  ///
  /// In en, this message translates to:
  /// **'Each instance gets the heap it needs; the manual numbers above are kept but no longer applied.'**
  String get settingsMemoryAutoHint;

  /// Switch for launcher-added JVM tuning and discrete GPU preference.
  ///
  /// In en, this message translates to:
  /// **'Game optimization'**
  String get settingsGameOptimization;

  /// Explains the game optimization switch.
  ///
  /// In en, this message translates to:
  /// **'Adds tuned G1 flags and prefers the dedicated GPU when starting the game.'**
  String get settingsGameOptimizationDesc;

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

  /// Full project page could not load the provider record.
  ///
  /// In en, this message translates to:
  /// **'Could not load project details.'**
  String get downloadProjectLoadFailed;

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

  /// No description provided for @taskCenterInstanceDuplicate.
  ///
  /// In en, this message translates to:
  /// **'Duplicate instance'**
  String get taskCenterInstanceDuplicate;

  /// Vanilla install task label.
  ///
  /// In en, this message translates to:
  /// **'Game files'**
  String get taskCenterGameFiles;

  /// Task label while the version metadata is fetched before a game-file download.
  ///
  /// In en, this message translates to:
  /// **'Preparing game files'**
  String get taskCenterPreparingGameFiles;

  /// Progress text for version.plan, which has no byte counters.
  ///
  /// In en, this message translates to:
  /// **'Reading version information…'**
  String get taskCenterReadingVersionInfo;

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

  /// Development-build badge on the about page.
  ///
  /// In en, this message translates to:
  /// **'DEV'**
  String get developmentBuildBadge;

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

  /// No description provided for @settingsCheckUpdate.
  ///
  /// In en, this message translates to:
  /// **'Check for updates'**
  String get settingsCheckUpdate;

  /// No description provided for @settingsCheckUpdateDesc.
  ///
  /// In en, this message translates to:
  /// **'Compares against the newest GitHub release (check only, no auto-install).'**
  String get settingsCheckUpdateDesc;

  /// No description provided for @updateChecking.
  ///
  /// In en, this message translates to:
  /// **'Checking for updates…'**
  String get updateChecking;

  /// Toast when the build is current.
  ///
  /// In en, this message translates to:
  /// **'You are up to date ({version}).'**
  String updateUpToDate(String version);

  /// No description provided for @updateAvailableTitle.
  ///
  /// In en, this message translates to:
  /// **'Update available'**
  String get updateAvailableTitle;

  /// Shown when a newer release exists.
  ///
  /// In en, this message translates to:
  /// **'Current {current}, latest {latest}.'**
  String updateAvailableBody(String current, String latest);

  /// No description provided for @updateOpenRelease.
  ///
  /// In en, this message translates to:
  /// **'Open release page'**
  String get updateOpenRelease;

  /// No description provided for @updateCheckFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not check for updates.'**
  String get updateCheckFailed;

  /// No description provided for @updateCurrentVersion.
  ///
  /// In en, this message translates to:
  /// **'Current version'**
  String get updateCurrentVersion;

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
  /// **'INDEPENDENT LAUNCHER. NOT AN OFFICIAL MINECRAFT PRODUCT. MINECRAFT IS A TRADEMARK OF MOJANG SYNERGIES AB.'**
  String get settingsAboutDisclaimer;

  /// No description provided for @settingsAboutOpenFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not open the link.'**
  String get settingsAboutOpenFailed;

  /// No description provided for @instanceViewLogs.
  ///
  /// In en, this message translates to:
  /// **'View logs'**
  String get instanceViewLogs;

  /// No description provided for @gameLogTitle.
  ///
  /// In en, this message translates to:
  /// **'Game log'**
  String get gameLogTitle;

  /// No description provided for @gameLogEmpty.
  ///
  /// In en, this message translates to:
  /// **'No log yet. Launch this instance once and its output will appear here.'**
  String get gameLogEmpty;

  /// No description provided for @gameLogNoOutput.
  ///
  /// In en, this message translates to:
  /// **'This log is empty.'**
  String get gameLogNoOutput;

  /// No description provided for @gameLogLoadFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not read the game log.'**
  String get gameLogLoadFailed;

  /// No description provided for @gameLogRunning.
  ///
  /// In en, this message translates to:
  /// **'Running'**
  String get gameLogRunning;

  /// No description provided for @gameLogStopped.
  ///
  /// In en, this message translates to:
  /// **'Stopped'**
  String get gameLogStopped;

  /// No description provided for @gameLogFollow.
  ///
  /// In en, this message translates to:
  /// **'Follow'**
  String get gameLogFollow;

  /// No description provided for @gameLogFollowHint.
  ///
  /// In en, this message translates to:
  /// **'Stream new lines while the game is running.'**
  String get gameLogFollowHint;

  /// No description provided for @gameLogCopy.
  ///
  /// In en, this message translates to:
  /// **'Copy all'**
  String get gameLogCopy;

  /// No description provided for @gameLogCopied.
  ///
  /// In en, this message translates to:
  /// **'Log copied to the clipboard.'**
  String get gameLogCopied;

  /// No description provided for @gameLogFileLabel.
  ///
  /// In en, this message translates to:
  /// **'Log file'**
  String get gameLogFileLabel;

  /// Shown when the log is larger than the read window.
  ///
  /// In en, this message translates to:
  /// **'Showing the last {count} lines.'**
  String gameLogTruncated(int count);

  /// No description provided for @gameLogOpenFolder.
  ///
  /// In en, this message translates to:
  /// **'Open log folder'**
  String get gameLogOpenFolder;

  /// No description provided for @gameLogOpenFolderFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not open the log folder.'**
  String get gameLogOpenFolderFailed;

  /// No description provided for @gameLogClear.
  ///
  /// In en, this message translates to:
  /// **'Clear old logs'**
  String get gameLogClear;

  /// No description provided for @gameLogClearTitle.
  ///
  /// In en, this message translates to:
  /// **'Clear old launch logs?'**
  String get gameLogClearTitle;

  /// No description provided for @gameLogClearBody.
  ///
  /// In en, this message translates to:
  /// **'Every archived launch log of this instance is deleted; a running log is kept. This cannot be undone.'**
  String get gameLogClearBody;

  /// Toast after clearing launch logs.
  ///
  /// In en, this message translates to:
  /// **'Deleted {count} logs, freed {size}.'**
  String gameLogClearDone(int count, String size);

  /// Header action that terminates the running game.
  ///
  /// In en, this message translates to:
  /// **'Force stop'**
  String get gameLogForceStop;

  /// No description provided for @gameLogForceStopTitle.
  ///
  /// In en, this message translates to:
  /// **'Force stop the game?'**
  String get gameLogForceStopTitle;

  /// First of two warnings before force-stopping a game.
  ///
  /// In en, this message translates to:
  /// **'\"{name}\" is running. Force stopping it ends the game immediately and any unsaved world progress is lost.'**
  String gameLogForceStopBody(String name);

  /// No description provided for @gameLogForceStopContinue.
  ///
  /// In en, this message translates to:
  /// **'Continue'**
  String get gameLogForceStopContinue;

  /// No description provided for @gameLogForceStopAgainTitle.
  ///
  /// In en, this message translates to:
  /// **'Terminate anyway?'**
  String get gameLogForceStopAgainTitle;

  /// No description provided for @gameLogForceStopAgainBody.
  ///
  /// In en, this message translates to:
  /// **'This cannot be undone. The game process will be killed immediately.'**
  String get gameLogForceStopAgainBody;

  /// No description provided for @gameLogForceStopAgainBodyPid.
  ///
  /// In en, this message translates to:
  /// **'This cannot be undone. Process {pid} will be killed immediately.'**
  String gameLogForceStopAgainBodyPid(int pid);

  /// No description provided for @gameLogForceStopConfirm.
  ///
  /// In en, this message translates to:
  /// **'Force stop'**
  String get gameLogForceStopConfirm;

  /// No description provided for @gameLogForceStopped.
  ///
  /// In en, this message translates to:
  /// **'Game process terminated.'**
  String get gameLogForceStopped;

  /// No description provided for @gameLogAlreadyStopped.
  ///
  /// In en, this message translates to:
  /// **'The game had already stopped.'**
  String get gameLogAlreadyStopped;

  /// No description provided for @settingsClearLogs.
  ///
  /// In en, this message translates to:
  /// **'Clear launch logs'**
  String get settingsClearLogs;

  /// No description provided for @settingsClearLogsDesc.
  ///
  /// In en, this message translates to:
  /// **'Deletes archived launch logs of every instance; running logs are kept.'**
  String get settingsClearLogsDesc;

  /// No description provided for @settingsClearLogsBody.
  ///
  /// In en, this message translates to:
  /// **'Every archived launch log is deleted (running logs are kept). This cannot be undone.'**
  String get settingsClearLogsBody;

  /// Retention note under the log file picker.
  ///
  /// In en, this message translates to:
  /// **'{size} of logs on disk; only the latest {max} launches are kept.'**
  String gameLogRetentionInfo(String size, int max);

  /// Launch task label.
  ///
  /// In en, this message translates to:
  /// **'Launch'**
  String get taskCenterLaunch;

  /// Task row title combining the category and the specific target (pack, version, instance).
  ///
  /// In en, this message translates to:
  /// **'{category} · {target}'**
  String taskCenterTaskWithTarget(String category, String target);

  /// Bottom navigation entry for the community/wiki page.
  ///
  /// In en, this message translates to:
  /// **'Wiki'**
  String get navWiki;

  /// Subtitle under the Wiki page title.
  ///
  /// In en, this message translates to:
  /// **'Minecraft community news and machine load.'**
  String get wikiSubtitle;

  /// Section title for the machine load block.
  ///
  /// In en, this message translates to:
  /// **'System load'**
  String get wikiSystemTitle;

  /// Section title for the community feed.
  ///
  /// In en, this message translates to:
  /// **'Community'**
  String get wikiFeedTitle;

  /// Subtitle for the community feed section.
  ///
  /// In en, this message translates to:
  /// **'Official news, Java versions and wiki edits.'**
  String get wikiFeedSubtitle;

  /// Refresh button on the Wiki page.
  ///
  /// In en, this message translates to:
  /// **'Refresh'**
  String get wikiRefresh;

  /// Last refresh time.
  ///
  /// In en, this message translates to:
  /// **'Updated {time}'**
  String wikiUpdatedAt(String time);

  /// Feed source label: official Minecraft news.
  ///
  /// In en, this message translates to:
  /// **'Official news'**
  String get wikiSourceNews;

  /// Feed source label: Java patch notes.
  ///
  /// In en, this message translates to:
  /// **'Java versions'**
  String get wikiSourceJava;

  /// Feed source label: Minecraft Wiki recent changes.
  ///
  /// In en, this message translates to:
  /// **'Wiki edits'**
  String get wikiSourceWiki;

  /// Feed item kind chip.
  ///
  /// In en, this message translates to:
  /// **'News'**
  String get wikiKindNews;

  /// Feed item kind chip.
  ///
  /// In en, this message translates to:
  /// **'Release'**
  String get wikiKindRelease;

  /// Feed item kind chip.
  ///
  /// In en, this message translates to:
  /// **'Snapshot'**
  String get wikiKindSnapshot;

  /// Feed item kind chip for a wiki edit.
  ///
  /// In en, this message translates to:
  /// **'Edit'**
  String get wikiKindEdit;

  /// Feed item kind chip for a new wiki page.
  ///
  /// In en, this message translates to:
  /// **'New page'**
  String get wikiKindNew;

  /// Empty state for the community feed.
  ///
  /// In en, this message translates to:
  /// **'Nothing to show right now.'**
  String get wikiFeedEmpty;

  /// Community feed request failed.
  ///
  /// In en, this message translates to:
  /// **'Could not load the community feed'**
  String get wikiFeedFailed;

  /// System status request failed.
  ///
  /// In en, this message translates to:
  /// **'Could not read system load'**
  String get wikiStatusFailed;

  /// Metric value when the platform cannot report it.
  ///
  /// In en, this message translates to:
  /// **'Unavailable'**
  String get wikiUnavailable;

  /// System load metric label.
  ///
  /// In en, this message translates to:
  /// **'CPU'**
  String get wikiCpuLabel;

  /// System load metric label.
  ///
  /// In en, this message translates to:
  /// **'Memory'**
  String get wikiMemoryLabel;

  /// System load metric label.
  ///
  /// In en, this message translates to:
  /// **'Disk'**
  String get wikiDiskLabel;

  /// System load metric label for the launcher process.
  ///
  /// In en, this message translates to:
  /// **'Launcher'**
  String get wikiLauncherLabel;

  /// CPU caption.
  ///
  /// In en, this message translates to:
  /// **'{count} threads'**
  String wikiCoresCount(int count);

  /// Load average caption.
  ///
  /// In en, this message translates to:
  /// **'load {value}'**
  String wikiLoadAverage(String value);

  /// Memory caption in gigabytes.
  ///
  /// In en, this message translates to:
  /// **'{used} / {total} GB'**
  String wikiMemoryUsage(String used, String total);

  /// Disk caption.
  ///
  /// In en, this message translates to:
  /// **'{free} free'**
  String wikiDiskFree(String free);

  /// Launcher uptime caption.
  ///
  /// In en, this message translates to:
  /// **'up {duration}'**
  String wikiUptime(String duration);

  /// System uptime caption.
  ///
  /// In en, this message translates to:
  /// **'System up {duration}'**
  String wikiSystemUptime(String duration);

  /// Relative timestamp.
  ///
  /// In en, this message translates to:
  /// **'just now'**
  String get wikiTimeJustNow;

  /// Relative timestamp.
  ///
  /// In en, this message translates to:
  /// **'{count} min ago'**
  String wikiTimeMinutesAgo(int count);

  /// Relative timestamp.
  ///
  /// In en, this message translates to:
  /// **'{count} h ago'**
  String wikiTimeHoursAgo(int count);

  /// Relative timestamp.
  ///
  /// In en, this message translates to:
  /// **'{count} d ago'**
  String wikiTimeDaysAgo(int count);

  /// Snackbar when a feed link cannot be opened.
  ///
  /// In en, this message translates to:
  /// **'Could not open the link'**
  String get wikiOpenLinkFailed;

  /// Per-source failure shown next to a source chip.
  ///
  /// In en, this message translates to:
  /// **'Fetch failed: {error}'**
  String wikiSourceError(String error);

  /// Retry action for a failed request.
  ///
  /// In en, this message translates to:
  /// **'Retry'**
  String get wikiRetry;

  /// Shown when a source returned zero items.
  ///
  /// In en, this message translates to:
  /// **'No items from this source'**
  String get wikiNoSourceData;

  /// Tooltip for opening a feed item externally.
  ///
  /// In en, this message translates to:
  /// **'Open in browser'**
  String get wikiOpenInBrowser;

  /// Search field hint on the Java page.
  ///
  /// In en, this message translates to:
  /// **'Filter by version, vendor, architecture or path'**
  String get javaSearchHint;

  /// Badge on the selected Java runtime tile.
  ///
  /// In en, this message translates to:
  /// **'In use'**
  String get javaSelectedBadge;

  /// Empty state when the Java filter matches nothing.
  ///
  /// In en, this message translates to:
  /// **'No Java matches this filter.'**
  String get javaFilteredEmpty;

  /// Back navigation button tooltip.
  ///
  /// In en, this message translates to:
  /// **'Back'**
  String get back;

  /// Subtitle under the dedicated Java page title.
  ///
  /// In en, this message translates to:
  /// **'Every Java runtime found on this machine; pick the one used to launch games.'**
  String get javaPageSubtitle;

  /// Settings row that opens the dedicated Java page.
  ///
  /// In en, this message translates to:
  /// **'Java runtimes'**
  String get javaPageManage;

  /// Settings row subtitle for the Java page entry.
  ///
  /// In en, this message translates to:
  /// **'Browse every scan result, add a folder or pick a java executable.'**
  String get javaPageManageHint;

  /// Appearance settings section title.
  ///
  /// In en, this message translates to:
  /// **'Theme'**
  String get settingsSectionTheme;

  /// Appearance settings section title.
  ///
  /// In en, this message translates to:
  /// **'Accent'**
  String get settingsSectionAccent;

  /// Appearance settings section title.
  ///
  /// In en, this message translates to:
  /// **'Interface'**
  String get settingsSectionInterface;

  /// Accent row subtitle when following the system accent.
  ///
  /// In en, this message translates to:
  /// **'Use the desktop accent color.'**
  String get settingsThemeAccentSystemDesc;

  /// Accent row subtitle when a custom accent is set.
  ///
  /// In en, this message translates to:
  /// **'Pick a single accent color for the launcher.'**
  String get settingsThemeAccentCustomDesc;

  /// Hint that machine load is pushed by the core every five seconds.
  ///
  /// In en, this message translates to:
  /// **'Updates every 5 s'**
  String get wikiSystemLive;

  /// Hint shown when vanilla is selected in the loader picker.
  ///
  /// In en, this message translates to:
  /// **'No mod loader; the instance installs the vanilla game.'**
  String get loaderPickerVanillaHint;

  /// Number of loader builds matching the current filter.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =0{No builds} =1{1 build} other{{count} builds}}'**
  String loaderVersionCount(int count);

  /// Empty state when the loader version search matches nothing.
  ///
  /// In en, this message translates to:
  /// **'No builds match this search.'**
  String get loaderVersionNoMatch;

  /// Dialog title and instance menu entry for per-instance launch overrides.
  ///
  /// In en, this message translates to:
  /// **'Instance settings'**
  String get instanceSettingsTitle;

  /// Subtitle explaining that empty override fields inherit the global value.
  ///
  /// In en, this message translates to:
  /// **'Blank fields follow the global settings.'**
  String get instanceSettingsDesc;

  /// Label for renaming the instance.
  ///
  /// In en, this message translates to:
  /// **'Instance name'**
  String get instanceSettingsName;

  /// Explains that renaming does not move the instance directory.
  ///
  /// In en, this message translates to:
  /// **'Shown in the launcher; the game folder is not moved.'**
  String get instanceSettingsNameHint;

  /// Validation error for an empty instance name.
  ///
  /// In en, this message translates to:
  /// **'Enter an instance name.'**
  String get instanceSettingsNameRequired;

  /// Validation error for an overlong instance name.
  ///
  /// In en, this message translates to:
  /// **'Instance names may be at most 80 characters.'**
  String get instanceSettingsNameTooLong;

  /// Validation error for control characters in an instance name.
  ///
  /// In en, this message translates to:
  /// **'Instance names cannot contain control characters.'**
  String get instanceSettingsNameInvalid;

  /// Per-instance minimum heap size override.
  ///
  /// In en, this message translates to:
  /// **'Minimum memory (MB)'**
  String get instanceSettingsMemoryMin;

  /// Per-instance maximum heap size override.
  ///
  /// In en, this message translates to:
  /// **'Maximum memory (MB)'**
  String get instanceSettingsMemoryMax;

  /// Per-instance garbage collector override.
  ///
  /// In en, this message translates to:
  /// **'Garbage collector'**
  String get instanceSettingsGc;

  /// Per-instance JVM argument override.
  ///
  /// In en, this message translates to:
  /// **'JVM arguments'**
  String get instanceSettingsJvmArgs;

  /// Per-instance extra game argument override.
  ///
  /// In en, this message translates to:
  /// **'Game arguments'**
  String get instanceSettingsGameArgs;

  /// Switch that enables a per-instance wrapper command override.
  ///
  /// In en, this message translates to:
  /// **'Override wrapper command'**
  String get instanceSettingsWrapperOverride;

  /// Per-instance wrapper command field.
  ///
  /// In en, this message translates to:
  /// **'Wrapper command'**
  String get instanceSettingsWrapperCommand;

  /// Hint explaining that an enabled empty wrapper override disables the global wrapper.
  ///
  /// In en, this message translates to:
  /// **'Leave empty to run the game without a wrapper.'**
  String get instanceSettingsWrapperCommandHint;

  /// Switch that enables a per-instance child environment override.
  ///
  /// In en, this message translates to:
  /// **'Override environment variables'**
  String get instanceSettingsEnvironmentOverride;

  /// Per-instance multiline environment field.
  ///
  /// In en, this message translates to:
  /// **'Environment variables'**
  String get instanceSettingsEnvironment;

  /// Format hint for the per-instance environment field.
  ///
  /// In en, this message translates to:
  /// **'KEY=VALUE'**
  String get instanceSettingsEnvironmentHint;

  /// Helper text under the per-instance environment field.
  ///
  /// In en, this message translates to:
  /// **'An empty list clears the global variables for this instance.'**
  String get instanceSettingsEnvironmentHelp;

  /// Switch that enables a per-instance pre-load command override.
  ///
  /// In en, this message translates to:
  /// **'Override pre-load command'**
  String get instanceSettingsPreLoadOverride;

  /// Switch that enables a per-instance pre-launch command override.
  ///
  /// In en, this message translates to:
  /// **'Override pre-launch command'**
  String get instanceSettingsPreLaunchOverride;

  /// Switch that enables a per-instance post-exit command override.
  ///
  /// In en, this message translates to:
  /// **'Override post-exit command'**
  String get instanceSettingsPostExitOverride;

  /// Per-instance pre-load command field.
  ///
  /// In en, this message translates to:
  /// **'Pre-load command'**
  String get instanceSettingsPreLoadCommand;

  /// Per-instance pre-launch command field.
  ///
  /// In en, this message translates to:
  /// **'Pre-launch command'**
  String get instanceSettingsPreLaunchCommand;

  /// Per-instance post-exit command field.
  ///
  /// In en, this message translates to:
  /// **'Post-exit command'**
  String get instanceSettingsPostExitCommand;

  /// Hint explaining that an enabled empty command override runs nothing.
  ///
  /// In en, this message translates to:
  /// **'Empty disables the command.'**
  String get instanceSettingsCommandEmptyHint;

  /// Safety and environment help under the per-instance command overrides.
  ///
  /// In en, this message translates to:
  /// **'Runs as your user without a shell in the instance folder; \$INST_* variables are available. A failed pre-launch command aborts the launch; a failed post-exit command is only logged. Do not put account tokens in commands.'**
  String get instanceSettingsCommandsHelp;

  /// Switch that enables joining a server or loading a world on launch.
  ///
  /// In en, this message translates to:
  /// **'Auto-join'**
  String get instanceSettingsAutoJoin;

  /// Subtitle under the auto-join switch.
  ///
  /// In en, this message translates to:
  /// **'Join a server or load a singleplayer world after the game starts.'**
  String get instanceSettingsAutoJoinDesc;

  /// Per-instance auto-join server address field.
  ///
  /// In en, this message translates to:
  /// **'Server address'**
  String get instanceSettingsJoinServer;

  /// Accepted server address shapes.
  ///
  /// In en, this message translates to:
  /// **'host, host:port, [IPv6], or [IPv6]:port'**
  String get instanceSettingsJoinServerHint;

  /// Per-instance auto-join singleplayer world field.
  ///
  /// In en, this message translates to:
  /// **'Singleplayer world'**
  String get instanceSettingsJoinWorld;

  /// Hint for the singleplayer world folder name.
  ///
  /// In en, this message translates to:
  /// **'World folder name'**
  String get instanceSettingsJoinWorldHint;

  /// Validation error when auto-join is enabled without a target.
  ///
  /// In en, this message translates to:
  /// **'Enter a server address or a singleplayer world.'**
  String get instanceSettingsAutoJoinNeedsTarget;

  /// No description provided for @instanceSettingsAutoJoinConflict.
  ///
  /// In en, this message translates to:
  /// **'Choose either a server address or a singleplayer world, not both.'**
  String get instanceSettingsAutoJoinConflict;

  /// Per-instance wrapper validation error.
  ///
  /// In en, this message translates to:
  /// **'Wrapper commands may be at most 4096 characters.'**
  String get instanceSettingsInvalidWrapper;

  /// Per-instance server address validation error.
  ///
  /// In en, this message translates to:
  /// **'Enter host, host:port, [IPv6], or [IPv6]:port.'**
  String get instanceSettingsInvalidServerAddress;

  /// Per-instance singleplayer world name validation error.
  ///
  /// In en, this message translates to:
  /// **'World names must be 1–128 characters without /, \\ or ...'**
  String get instanceSettingsInvalidWorldName;

  /// Per-instance Java runtime override.
  ///
  /// In en, this message translates to:
  /// **'Java executable'**
  String get instanceSettingsJava;

  /// Helper under the per-instance Java path field.
  ///
  /// In en, this message translates to:
  /// **'Absolute path to the java binary; leave blank to follow the global selection.'**
  String get instanceSettingsJavaHint;

  /// Tooltip on the dropdown that lists scanned Java runtimes.
  ///
  /// In en, this message translates to:
  /// **'Choose a scanned Java runtime'**
  String get instanceSettingsJavaPick;

  /// Menu entry that clears the per-instance Java path.
  ///
  /// In en, this message translates to:
  /// **'Use the global Java selection'**
  String get instanceSettingsJavaFollowGlobal;

  /// Shown inside the Java picker while the scanner is running.
  ///
  /// In en, this message translates to:
  /// **'Scanning for Java runtimes…'**
  String get instanceSettingsJavaScanning;

  /// Shown inside the Java picker when the scan reported an error.
  ///
  /// In en, this message translates to:
  /// **'Java scan failed; retry'**
  String get instanceSettingsJavaScanFailed;

  /// Shown inside the Java picker when the scan found nothing.
  ///
  /// In en, this message translates to:
  /// **'No Java runtime scanned yet'**
  String get instanceSettingsJavaNone;

  /// Menu entry that reruns the Java scan from the picker.
  ///
  /// In en, this message translates to:
  /// **'Rescan Java runtimes'**
  String get instanceSettingsJavaRescan;

  /// Per-instance window mode override.
  ///
  /// In en, this message translates to:
  /// **'Game window'**
  String get instanceSettingsWindow;

  /// Window mode option that clears the per-instance override.
  ///
  /// In en, this message translates to:
  /// **'Follow global'**
  String get instanceSettingsWindowInherit;

  /// Window mode option that always starts fullscreen.
  ///
  /// In en, this message translates to:
  /// **'Fullscreen'**
  String get instanceSettingsWindowFullscreen;

  /// Window mode option that always starts in a window.
  ///
  /// In en, this message translates to:
  /// **'Windowed'**
  String get instanceSettingsWindowWindowed;

  /// Window mode option that approximates a maximized window.
  ///
  /// In en, this message translates to:
  /// **'Maximized'**
  String get instanceSettingsWindowMaximized;

  /// Per-instance performance override section on Linux.
  ///
  /// In en, this message translates to:
  /// **'Performance'**
  String get instanceSettingsPerformance;

  /// Explains the inherit/on/off performance override choices.
  ///
  /// In en, this message translates to:
  /// **'Follow global, force on, or force off for this instance. Inherit keeps the global switch.'**
  String get instanceSettingsPerformanceHint;

  /// Per-instance GameMode override row.
  ///
  /// In en, this message translates to:
  /// **'Feral GameMode'**
  String get instanceSettingsGameMode;

  /// Per-instance MangoHud override row.
  ///
  /// In en, this message translates to:
  /// **'MangoHud'**
  String get instanceSettingsMangoHud;

  /// Per-instance Zink override row.
  ///
  /// In en, this message translates to:
  /// **'Zink'**
  String get instanceSettingsZink;

  /// Tri-state override option that clears the per-instance value.
  ///
  /// In en, this message translates to:
  /// **'Follow global'**
  String get instanceSettingsOverrideInherit;

  /// Tri-state override option that forces the feature on.
  ///
  /// In en, this message translates to:
  /// **'On'**
  String get instanceSettingsOverrideOn;

  /// Tri-state override option that forces the feature off.
  ///
  /// In en, this message translates to:
  /// **'Off'**
  String get instanceSettingsOverrideOff;

  /// Per-instance initial window width override.
  ///
  /// In en, this message translates to:
  /// **'Window width'**
  String get instanceSettingsWindowWidth;

  /// Per-instance initial window height override.
  ///
  /// In en, this message translates to:
  /// **'Window height'**
  String get instanceSettingsWindowHeight;

  /// Hint on empty override fields meaning the global setting is used.
  ///
  /// In en, this message translates to:
  /// **'Follow global'**
  String get instanceSettingsFollowGlobal;

  /// Save button in the instance settings dialog.
  ///
  /// In en, this message translates to:
  /// **'Save'**
  String get instanceSettingsSave;

  /// Toast after per-instance overrides are written.
  ///
  /// In en, this message translates to:
  /// **'Instance settings saved.'**
  String get instanceSettingsSaved;

  /// Shown as the hint while an instance override is empty.
  ///
  /// In en, this message translates to:
  /// **'Follow global ({value})'**
  String instanceSettingsFollowGlobalWith(String value);

  /// Hint for the memory fields while automatic sizing is on.
  ///
  /// In en, this message translates to:
  /// **'Automatic · {mb} MB recommended'**
  String instanceSettingsMemoryAuto(int mb);

  /// Inline warning when a manual heap override is too small.
  ///
  /// In en, this message translates to:
  /// **'Below the recommended {recommended} MB; the game may stutter or crash.'**
  String instanceSettingsMemoryLow(int recommended);

  /// No description provided for @instanceSettingsReset.
  ///
  /// In en, this message translates to:
  /// **'Reset to global'**
  String get instanceSettingsReset;

  /// No description provided for @instanceSettingsResetBody.
  ///
  /// In en, this message translates to:
  /// **'Clears every override so this instance follows the global settings again.'**
  String get instanceSettingsResetBody;

  /// No description provided for @instanceSettingsResetDone.
  ///
  /// In en, this message translates to:
  /// **'Reset to the global settings.'**
  String get instanceSettingsResetDone;

  /// No description provided for @instanceOverridesMemory.
  ///
  /// In en, this message translates to:
  /// **'memory'**
  String get instanceOverridesMemory;

  /// No description provided for @instanceOverridesGc.
  ///
  /// In en, this message translates to:
  /// **'GC'**
  String get instanceOverridesGc;

  /// No description provided for @instanceOverridesJvm.
  ///
  /// In en, this message translates to:
  /// **'JVM args'**
  String get instanceOverridesJvm;

  /// No description provided for @instanceOverridesGameArgs.
  ///
  /// In en, this message translates to:
  /// **'game args'**
  String get instanceOverridesGameArgs;

  /// Override badge label for a per-instance wrapper command.
  ///
  /// In en, this message translates to:
  /// **'wrapper'**
  String get instanceOverridesWrapper;

  /// Override badge label for a per-instance environment map.
  ///
  /// In en, this message translates to:
  /// **'environment'**
  String get instanceOverridesEnvironment;

  /// Override badge label for per-instance auto-join.
  ///
  /// In en, this message translates to:
  /// **'auto-join'**
  String get instanceOverridesAutoJoin;

  /// No description provided for @instanceOverridesJava.
  ///
  /// In en, this message translates to:
  /// **'Java'**
  String get instanceOverridesJava;

  /// No description provided for @instanceOverridesWindow.
  ///
  /// In en, this message translates to:
  /// **'window'**
  String get instanceOverridesWindow;

  /// Inline validation error for non-numeric override values.
  ///
  /// In en, this message translates to:
  /// **'Memory and window sizes must be whole numbers.'**
  String get instanceSettingsInvalidNumbers;

  /// Inline validation error for out-of-range override values.
  ///
  /// In en, this message translates to:
  /// **'Memory must be 128–65536 MB with min ≤ max; the window must be 640–7680 × 480–4320.'**
  String get instanceSettingsInvalidRange;

  /// Inline validation error for a relative Java path.
  ///
  /// In en, this message translates to:
  /// **'Enter an absolute path to the Java executable.'**
  String get instanceSettingsInvalidPath;

  /// Inline validation error for an unknown garbage collector.
  ///
  /// In en, this message translates to:
  /// **'Unsupported garbage collector.'**
  String get instanceSettingsInvalidGc;

  /// Failure toast when the override update call fails.
  ///
  /// In en, this message translates to:
  /// **'Could not save instance settings: {message}'**
  String instanceSettingsSaveFailed(String message);

  /// Title of the automatic crash diagnosis dialog.
  ///
  /// In en, this message translates to:
  /// **'The game exited unexpectedly'**
  String get crashDialogTitle;

  /// No description provided for @crashDialogExitCode.
  ///
  /// In en, this message translates to:
  /// **'Exit code: {code}'**
  String crashDialogExitCode(int code);

  /// Shown when the analyzer has no rule-specific finding.
  ///
  /// In en, this message translates to:
  /// **'No specific cause matched. Open the launch log for the last error lines.'**
  String get crashDialogNoDetails;

  /// Classic launcher reminder in the crash dialog: ask for help with the log, not a screenshot.
  ///
  /// In en, this message translates to:
  /// **'If you need help from others, share the error log — not a photo or screenshot of this window.'**
  String get crashDialogShareHint;

  /// Shown when the exit code is a POSIX signal (for example 137 = SIGKILL); force-stopping the game also lands in this dialog.
  ///
  /// In en, this message translates to:
  /// **'If you terminated the game yourself, you can ignore this message.'**
  String get crashDialogForceStopHint;

  /// Expandable list of redacted log lines.
  ///
  /// In en, this message translates to:
  /// **'Evidence'**
  String get crashDialogEvidence;

  /// No description provided for @crashDialogCopy.
  ///
  /// In en, this message translates to:
  /// **'Copy diagnosis'**
  String get crashDialogCopy;

  /// No description provided for @crashDialogCopied.
  ///
  /// In en, this message translates to:
  /// **'Diagnosis copied.'**
  String get crashDialogCopied;

  /// No description provided for @crashDialogClose.
  ///
  /// In en, this message translates to:
  /// **'Close'**
  String get crashDialogClose;

  /// No description provided for @crashRuleJavaTooOldTitle.
  ///
  /// In en, this message translates to:
  /// **'Java version is too old'**
  String get crashRuleJavaTooOldTitle;

  /// No description provided for @crashRuleJavaTooOldDetail.
  ///
  /// In en, this message translates to:
  /// **'The game needs Java {needed}, but this launch used Java {current}.'**
  String crashRuleJavaTooOldDetail(String needed, String current);

  /// No description provided for @crashRuleJavaTooOldFix.
  ///
  /// In en, this message translates to:
  /// **'Select Java {needed} or newer for this instance in Settings → Java.'**
  String crashRuleJavaTooOldFix(String needed);

  /// No description provided for @crashRuleJavaTooNewTitle.
  ///
  /// In en, this message translates to:
  /// **'Java version is too new'**
  String get crashRuleJavaTooNewTitle;

  /// No description provided for @crashRuleJavaTooNewDetail.
  ///
  /// In en, this message translates to:
  /// **'This old loader does not support the selected Java runtime.'**
  String get crashRuleJavaTooNewDetail;

  /// No description provided for @crashRuleJavaTooNewFix.
  ///
  /// In en, this message translates to:
  /// **'Use Java 8 for 1.12.2/1.16.5, Java 17 for 1.18–1.20.4, and Java 21 for newer versions.'**
  String get crashRuleJavaTooNewFix;

  /// No description provided for @crashRuleHeapTitle.
  ///
  /// In en, this message translates to:
  /// **'Java memory is insufficient'**
  String get crashRuleHeapTitle;

  /// No description provided for @crashRuleHeapDetail.
  ///
  /// In en, this message translates to:
  /// **'The JVM could not reserve the configured heap or ran out of heap space.'**
  String get crashRuleHeapDetail;

  /// No description provided for @crashRuleHeapFix.
  ///
  /// In en, this message translates to:
  /// **'Close other programs, adjust the allocated memory, and make sure the selected Java is 64-bit.'**
  String get crashRuleHeapFix;

  /// No description provided for @crashRuleMixinTitle.
  ///
  /// In en, this message translates to:
  /// **'Mixin injection failed'**
  String get crashRuleMixinTitle;

  /// No description provided for @crashRuleMixinDetail.
  ///
  /// In en, this message translates to:
  /// **'Mixin {config} failed to apply. This usually means the mod does not match this Minecraft or loader version.'**
  String crashRuleMixinDetail(String config);

  /// No description provided for @crashRuleMixinFix.
  ///
  /// In en, this message translates to:
  /// **'Update or remove the mod that owns this mixin; disable recently added mods to narrow it down.'**
  String get crashRuleMixinFix;

  /// No description provided for @crashRuleDependencyTitle.
  ///
  /// In en, this message translates to:
  /// **'Missing mod dependency'**
  String get crashRuleDependencyTitle;

  /// No description provided for @crashRuleDependencyDetail.
  ///
  /// In en, this message translates to:
  /// **'A mod requires another mod that is not installed or has an incompatible version.'**
  String get crashRuleDependencyDetail;

  /// No description provided for @crashRuleDependencyFix.
  ///
  /// In en, this message translates to:
  /// **'Install the missing dependency version that matches this Minecraft and loader version.'**
  String get crashRuleDependencyFix;

  /// No description provided for @crashRuleMainClassTitle.
  ///
  /// In en, this message translates to:
  /// **'Game or loader installation is incomplete'**
  String get crashRuleMainClassTitle;

  /// No description provided for @crashRuleMainClassDetail.
  ///
  /// In en, this message translates to:
  /// **'The main class {cls} could not be loaded.'**
  String crashRuleMainClassDetail(String cls);

  /// No description provided for @crashRuleMainClassFix.
  ///
  /// In en, this message translates to:
  /// **'Reinstall the loader or run loader repair, then try again.'**
  String get crashRuleMainClassFix;

  /// No description provided for @crashRuleGraphicsTitle.
  ///
  /// In en, this message translates to:
  /// **'Graphics/OpenGL initialization failed'**
  String get crashRuleGraphicsTitle;

  /// No description provided for @crashRuleGraphicsDetail.
  ///
  /// In en, this message translates to:
  /// **'The game could not create an OpenGL window.'**
  String get crashRuleGraphicsDetail;

  /// No description provided for @crashRuleGraphicsFix.
  ///
  /// In en, this message translates to:
  /// **'Update the graphics driver, run on the dedicated GPU, and avoid remote desktop or virtual machines.'**
  String get crashRuleGraphicsFix;

  /// No description provided for @crashRuleModTitle.
  ///
  /// In en, this message translates to:
  /// **'A mod failed to start'**
  String get crashRuleModTitle;

  /// No description provided for @crashRuleModDetail.
  ///
  /// In en, this message translates to:
  /// **'Mod {mod} threw an error during the {stage} stage.'**
  String crashRuleModDetail(String mod, String stage);

  /// No description provided for @crashRuleModFix.
  ///
  /// In en, this message translates to:
  /// **'Update or remove this mod; attach the crash report when reporting it to the author.'**
  String get crashRuleModFix;

  /// No description provided for @crashRuleFileLockedTitle.
  ///
  /// In en, this message translates to:
  /// **'A game file is locked or not writable'**
  String get crashRuleFileLockedTitle;

  /// No description provided for @crashRuleFileLockedDetail.
  ///
  /// In en, this message translates to:
  /// **'A file was in use by another process or permission was denied.'**
  String get crashRuleFileLockedDetail;

  /// No description provided for @crashRuleFileLockedFix.
  ///
  /// In en, this message translates to:
  /// **'Close other launchers or instances, move the game out of OneDrive, and check folder permissions.'**
  String get crashRuleFileLockedFix;

  /// No description provided for @crashRuleDriverTitle.
  ///
  /// In en, this message translates to:
  /// **'Graphics driver crashed'**
  String get crashRuleDriverTitle;

  /// No description provided for @crashRuleDriverDetail.
  ///
  /// In en, this message translates to:
  /// **'The JVM crashed inside {lib}.'**
  String crashRuleDriverDetail(String lib);

  /// No description provided for @crashRuleDriverFix.
  ///
  /// In en, this message translates to:
  /// **'Clean-install the graphics driver and disable overlays such as RTSS, Discord or Steam.'**
  String get crashRuleDriverFix;

  /// No description provided for @crashRuleNativeTitle.
  ///
  /// In en, this message translates to:
  /// **'Native JVM crash'**
  String get crashRuleNativeTitle;

  /// No description provided for @crashRuleNativeDetail.
  ///
  /// In en, this message translates to:
  /// **'The JVM crashed in native code: {frame}.'**
  String crashRuleNativeDetail(String frame);

  /// No description provided for @crashRuleNativeFix.
  ///
  /// In en, this message translates to:
  /// **'Check the hs_err log, remove native mods or agents, and verify the Java runtime.'**
  String get crashRuleNativeFix;

  /// No description provided for @crashRuleReportTitle.
  ///
  /// In en, this message translates to:
  /// **'The game reported a crash'**
  String get crashRuleReportTitle;

  /// No description provided for @crashRuleReportDetail.
  ///
  /// In en, this message translates to:
  /// **'Crash report description: {description}'**
  String crashRuleReportDetail(String description);

  /// No description provided for @crashRuleReportFix.
  ///
  /// In en, this message translates to:
  /// **'Check the crash report and mod list; disable recently added mods.'**
  String get crashRuleReportFix;

  /// No description provided for @crashRuleUnknownTitle.
  ///
  /// In en, this message translates to:
  /// **'The game exited unexpectedly'**
  String get crashRuleUnknownTitle;

  /// No description provided for @crashRuleUnknownDetail.
  ///
  /// In en, this message translates to:
  /// **'No known signature matched the exit code {code}.'**
  String crashRuleUnknownDetail(String code);

  /// No description provided for @crashRuleUnknownFix.
  ///
  /// In en, this message translates to:
  /// **'Open the launch log and crash report for the last error lines.'**
  String get crashRuleUnknownFix;

  /// No description provided for @accountAddAuthlib.
  ///
  /// In en, this message translates to:
  /// **'Third-party sign-in'**
  String get accountAddAuthlib;

  /// No description provided for @accountAuthlibDesc.
  ///
  /// In en, this message translates to:
  /// **'Sign in through a Yggdrasil server such as LittleSkin.'**
  String get accountAuthlibDesc;

  /// No description provided for @accountAuthTitle.
  ///
  /// In en, this message translates to:
  /// **'Third-party sign-in'**
  String get accountAuthTitle;

  /// No description provided for @accountAuthServerPreset.
  ///
  /// In en, this message translates to:
  /// **'Server'**
  String get accountAuthServerPreset;

  /// No description provided for @accountAuthCustomServer.
  ///
  /// In en, this message translates to:
  /// **'Custom server'**
  String get accountAuthCustomServer;

  /// No description provided for @accountAuthServerUrl.
  ///
  /// In en, this message translates to:
  /// **'Yggdrasil API URL'**
  String get accountAuthServerUrl;

  /// No description provided for @accountAuthTermsUrl.
  ///
  /// In en, this message translates to:
  /// **'Terms URL'**
  String get accountAuthTermsUrl;

  /// No description provided for @accountAuthTermsTitle.
  ///
  /// In en, this message translates to:
  /// **'Third-party terms'**
  String get accountAuthTermsTitle;

  /// No description provided for @accountAuthTermsBody.
  ///
  /// In en, this message translates to:
  /// **'You are about to sign in to {server}. Read and accept that server\'s terms and privacy policy before the launcher downloads authlib-injector or sends credentials.'**
  String accountAuthTermsBody(String server);

  /// No description provided for @accountAuthOpenTerms.
  ///
  /// In en, this message translates to:
  /// **'Open terms'**
  String get accountAuthOpenTerms;

  /// No description provided for @accountAuthOpenTermsFirst.
  ///
  /// In en, this message translates to:
  /// **'Open the terms before accepting.'**
  String get accountAuthOpenTermsFirst;

  /// No description provided for @accountAuthAcceptTerms.
  ///
  /// In en, this message translates to:
  /// **'I have read and accept the server\'s terms and privacy policy'**
  String get accountAuthAcceptTerms;

  /// No description provided for @accountAuthUsername.
  ///
  /// In en, this message translates to:
  /// **'Email or username'**
  String get accountAuthUsername;

  /// No description provided for @accountAuthPassword.
  ///
  /// In en, this message translates to:
  /// **'Password'**
  String get accountAuthPassword;

  /// No description provided for @accountAuthLogin.
  ///
  /// In en, this message translates to:
  /// **'Sign in'**
  String get accountAuthLogin;

  /// No description provided for @accountAuthImportLink.
  ///
  /// In en, this message translates to:
  /// **'Import from authlib link'**
  String get accountAuthImportLink;

  /// No description provided for @accountAuthImportFailed.
  ///
  /// In en, this message translates to:
  /// **'The clipboard does not contain an authlib server link.'**
  String get accountAuthImportFailed;

  /// No description provided for @platformSearchHint.
  ///
  /// In en, this message translates to:
  /// **'Search packs'**
  String get platformSearchHint;

  /// No description provided for @platformLoadFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not load packs.'**
  String get platformLoadFailed;

  /// No description provided for @platformEmpty.
  ///
  /// In en, this message translates to:
  /// **'No packs found.'**
  String get platformEmpty;

  /// No description provided for @platformEmptyHint.
  ///
  /// In en, this message translates to:
  /// **'Try another keyword, or switch the platform.'**
  String get platformEmptyHint;

  /// No description provided for @platformVersionsEmpty.
  ///
  /// In en, this message translates to:
  /// **'No installable versions.'**
  String get platformVersionsEmpty;

  /// No description provided for @platformInstall.
  ///
  /// In en, this message translates to:
  /// **'Install'**
  String get platformInstall;

  /// No description provided for @platformInstallUnsupported.
  ///
  /// In en, this message translates to:
  /// **'This platform pack format is not supported by the installer.'**
  String get platformInstallUnsupported;

  /// No description provided for @platformInstallMetadataFailed.
  ///
  /// In en, this message translates to:
  /// **'The platform returned incomplete pack metadata. Try again later or pick another version.'**
  String get platformInstallMetadataFailed;

  /// No description provided for @platformInstallNetworkFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not reach the platform or its download host. Try again, or use another pack/source.'**
  String get platformInstallNetworkFailed;

  /// No description provided for @platformDirectArchiveTitle.
  ///
  /// In en, this message translates to:
  /// **'Install without a published hash?'**
  String get platformDirectArchiveTitle;

  /// No description provided for @platformDirectArchiveWarning.
  ///
  /// In en, this message translates to:
  /// **'This platform publishes only a direct zip and no MD5/SHA digest. The installer will use HTTPS, validate every ZIP entry\'s CRC, and record the downloaded SHA-256. Continue?'**
  String get platformDirectArchiveWarning;

  /// No description provided for @platformDirectArchiveConfirm.
  ///
  /// In en, this message translates to:
  /// **'Continue install'**
  String get platformDirectArchiveConfirm;

  /// No description provided for @platformDirectArchiveBadge.
  ///
  /// In en, this message translates to:
  /// **'direct zip · no official hash'**
  String get platformDirectArchiveBadge;

  /// No description provided for @accountAuthTermsRequired.
  ///
  /// In en, this message translates to:
  /// **'Accept the third-party terms first.'**
  String get accountAuthTermsRequired;

  /// No description provided for @accountAuthServerInvalid.
  ///
  /// In en, this message translates to:
  /// **'Enter a valid https server URL.'**
  String get accountAuthServerInvalid;

  /// No description provided for @accountAuthInvalidCredentials.
  ///
  /// In en, this message translates to:
  /// **'The server rejected the username or password.'**
  String get accountAuthInvalidCredentials;

  /// No description provided for @accountAuthNoProfile.
  ///
  /// In en, this message translates to:
  /// **'This account has no Minecraft profile on the server.'**
  String get accountAuthNoProfile;

  /// No description provided for @accountAuthNetwork.
  ///
  /// In en, this message translates to:
  /// **'Cannot reach the auth server.'**
  String get accountAuthNetwork;

  /// No description provided for @accountAuthDownloadFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not download authlib-injector. Try again.'**
  String get accountAuthDownloadFailed;

  /// No description provided for @accountAuthTermsOpenFailed.
  ///
  /// In en, this message translates to:
  /// **'Could not open the terms page.'**
  String get accountAuthTermsOpenFailed;

  /// No description provided for @elevationTitle.
  ///
  /// In en, this message translates to:
  /// **'I know what you\'re trying to do'**
  String get elevationTitle;

  /// No description provided for @elevationBody.
  ///
  /// In en, this message translates to:
  /// **'ArchoeraCX detected that this process is running as {kind}. Starting as a superuser hands ownership of every instance, mod and game file to that account, and gives downloaded content far more damage potential.\n\nNeither the launcher nor Minecraft needs administrator rights. Relaunch with a normal account; continue only if you understand the consequences.'**
  String elevationBody(String kind);

  /// No description provided for @elevationContinue.
  ///
  /// In en, this message translates to:
  /// **'I know what I\'m doing'**
  String get elevationContinue;

  /// No description provided for @elevationExit.
  ///
  /// In en, this message translates to:
  /// **'Exit'**
  String get elevationExit;

  /// No description provided for @elevationKindRoot.
  ///
  /// In en, this message translates to:
  /// **'root'**
  String get elevationKindRoot;

  /// No description provided for @elevationKindAdministrator.
  ///
  /// In en, this message translates to:
  /// **'administrator'**
  String get elevationKindAdministrator;
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
