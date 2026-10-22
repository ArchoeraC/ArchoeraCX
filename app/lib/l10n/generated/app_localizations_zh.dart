// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Chinese (`zh`).
class AppLocalizationsZh extends AppLocalizations {
  AppLocalizationsZh([String locale = 'zh']) : super(locale);

  @override
  String get appName => 'ArchoeraCX';

  @override
  String get appTagline => '跨平台 Minecraft: Java Edition 启动器';

  @override
  String get appPreview => '界面预览';

  @override
  String get navDashboard => '主页';

  @override
  String get navInstances => '实例';

  @override
  String get navDownload => '下载';

  @override
  String get navAccounts => '账户';

  @override
  String get navSettings => '设置';

  @override
  String get coreOffline => '核心未连接';

  @override
  String get coreOfflineHint => '原生核心尚未接线。';

  @override
  String get accountOffline => '离线';

  @override
  String get accountLocal => '本地账户';

  @override
  String get launch => '启动';

  @override
  String get selectInstance => '请先选择实例';

  @override
  String get commonMockData => '模拟数据 · UI 外壳';

  @override
  String get commonCoreNotConnected => '原生核心尚未连接。';

  @override
  String get dashboardTitle => '主页';

  @override
  String get dashboardSubtitle => '启动、安装和管理你的世界。';

  @override
  String get dashboardWorkspace => '工作区';

  @override
  String get dashboardQuickActions => '快捷操作';

  @override
  String get dashboardBrowseContent => '浏览内容';

  @override
  String get dashboardEnvironment => '运行环境';

  @override
  String get quickLaunch => '快速启动';

  @override
  String get quickLaunchSubtitle => '最近的实例已经准备好。';

  @override
  String get recentInstances => '最近实例';

  @override
  String get viewAll => '查看全部';

  @override
  String get statInstances => '实例';

  @override
  String get statJava => 'Java';

  @override
  String get statDownloads => '下载';

  @override
  String get statAccounts => '账户';

  @override
  String get instanceName1 => '1.21.4 · Fabric';

  @override
  String get instanceMeta1 => 'Fabric 0.16.10 · 12 个 Mod';

  @override
  String get instanceName2 => '1.20.1 · Forge';

  @override
  String get instanceMeta2 => 'Forge 47.2.0 · 8 个 Mod';

  @override
  String get lastPlayedToday => '今天玩过';

  @override
  String get lastPlayedYesterday => '昨天玩过';

  @override
  String get instancesTitle => '实例';

  @override
  String get instancesSubtitle => '实例目录隔离，库与资源按 hash 共享。';

  @override
  String get searchInstances => '搜索实例';

  @override
  String get filterAll => '全部';

  @override
  String get createInstance => '创建实例';

  @override
  String get importPack => '导入整合包';

  @override
  String get packInstallTitle => '安装整合包';

  @override
  String get packImportTitle => '导入整合包';

  @override
  String get packInstallHint =>
      '将按整合包依赖新建实例，安装 Minecraft 与加载器，再合并包内 overrides。';

  @override
  String get packInstallRunning => '正在安装整合包…';

  @override
  String get packInstallDone => '整合包安装完成。';

  @override
  String get packInstallFailed => '整合包安装失败。';

  @override
  String packInspectMeta(String game, String loader, String loaderVersion) {
    return '$game · $loader $loaderVersion';
  }

  @override
  String packInspectFiles(int count, int overrides) {
    return '$count 个索引文件 · $overrides 个包内文件';
  }

  @override
  String get packInspectFailed => '该文件不是有效的 Modrinth 整合包。';

  @override
  String get packLoaderUnsupported =>
      '暂不支持该整合包加载器。支持原版、Fabric、Quilt、Forge 与 NeoForge。';

  @override
  String get packJavaMissing =>
      '该整合包需要 Java 来运行加载器 processors。请先在设置中安装或选择匹配的 Java。';

  @override
  String get packCancelling => '正在取消…';

  @override
  String get packCancelled => '整合包安装已取消。';

  @override
  String get packTargetNewInstance => '将新建实例';

  @override
  String packResultSummary(int downloaded, int bundled) {
    return '已下载 $downloaded 个文件 · 包内自带 $bundled 个';
  }

  @override
  String get instanceCreateTitle => '创建实例';

  @override
  String get instanceNameLabel => '名称';

  @override
  String get instanceNameHint => '例如 1.21.4 · Fabric';

  @override
  String get instanceGameVersionLabel => '游戏版本';

  @override
  String get instanceLocationLabel => '实例目录';

  @override
  String get instanceChooseLocation => '选择目录';

  @override
  String get instanceLocationHint => '留空则使用全局实例目录';

  @override
  String get instanceRootLabel => '存储位置';

  @override
  String get instanceCustomRoot => '自定义目录';

  @override
  String get instanceMissing => '目录缺失';

  @override
  String get instanceCustom => '自定义';

  @override
  String get instanceSyncDone => '实例目录已同步。';

  @override
  String instanceSyncAdopted(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '已加入 $count 个实例',
    );
    return '$_temp0';
  }

  @override
  String instanceSyncMissing(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count 个实例目录缺失',
    );
    return '$_temp0';
  }

  @override
  String get instanceLoaderLabel => '加载器';

  @override
  String instanceMods(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count 个 Mod',
      zero: '无 Mod',
    );
    return '$_temp0';
  }

  @override
  String get instanceLastPlayedNever => '从未启动';

  @override
  String get instanceEmptyTitle => '还没有实例';

  @override
  String get instanceEmptyMessage => '创建一个原版或带加载器的实例开始吧。';

  @override
  String get instanceNoMatches => '没有匹配的实例。';

  @override
  String get instanceScanAction => '扫描实例';

  @override
  String get instanceScanning => '正在扫描实例目录…';

  @override
  String get instanceDelete => '删除实例';

  @override
  String instanceDeleteConfirm(String name) {
    return '删除“$name”？';
  }

  @override
  String get instanceCheckQuick => '快速检查';

  @override
  String get instanceCheckFull => '完整检查';

  @override
  String get instanceCheckTitle => '依赖检查';

  @override
  String get instanceCheckOk => '依赖完整。';

  @override
  String instanceCheckMissing(int count) {
    return '缺失：$count';
  }

  @override
  String instanceCheckMismatched(int count) {
    return '校验失败：$count';
  }

  @override
  String get instanceCheckFailed => '依赖检查失败。';

  @override
  String instanceCheckLaunchFiles(String issue) {
    return '启动文件：$issue';
  }

  @override
  String get instanceCheckRepair => '修复依赖';

  @override
  String get instanceCheckRepairHint => '补全缺失的库/资源文件，必要时重跑加载器 processors。';

  @override
  String get instanceCheckRepairing => '正在修复依赖…';

  @override
  String get instanceCheckRepairDone => '依赖已修复。';

  @override
  String get instanceCheckRepairFailed => '依赖修复失败。';

  @override
  String get instanceManifestMissing => '实例中未找到加载器安装包。';

  @override
  String get instanceLaunchNotReady => '启动流程尚未接线。';

  @override
  String launchStarted(int pid) {
    return '游戏已启动（PID $pid）。';
  }

  @override
  String get launchFailed => '启动失败。';

  @override
  String get launchStarting => '正在启动游戏…';

  @override
  String get launchNoAccount => '请先添加账户。';

  @override
  String get launchOfflineOnly => '当前基础启动仅支持离线账户。';

  @override
  String get launchAutoRepairFailed => '启动文件缺失，自动修复失败。请在实例菜单中重新安装加载器。';

  @override
  String get launchAutoRepairBlocked => '启动文件缺失，但下载已禁用，无法自动补全。请先启用下载。';

  @override
  String get launchJavaMissing => '没有找到合适的 Java 运行时。';

  @override
  String launchLog(String path) {
    return '日志：$path';
  }

  @override
  String get instanceCreated => '实例已创建。';

  @override
  String get commonCancel => '取消';

  @override
  String get commonCreate => '创建';

  @override
  String get commonDelete => '删除';

  @override
  String get commonSave => '保存';

  @override
  String get downloadTitle => '下载';

  @override
  String get downloadSubtitle => '游戏版本、Mod、整合包、资源包与光影。';

  @override
  String get sourceLabel => '内容来源';

  @override
  String get tabVersions => '游戏版本';

  @override
  String get versionListTitle => 'Minecraft 版本';

  @override
  String get versionSearchHint => '搜索版本';

  @override
  String get versionIncludeSnapshots => '包含快照';

  @override
  String get versionUse => '使用此版本';

  @override
  String get versionLoadFailed => '版本列表加载失败。';

  @override
  String get versionLoading => '正在加载版本…';

  @override
  String get versionEmpty => '没有匹配的版本。';

  @override
  String get tabMods => 'Mod';

  @override
  String get tabPacks => '整合包';

  @override
  String get tabResourcePacks => '资源包';

  @override
  String get tabShaders => '光影';

  @override
  String get sourceModrinth => 'Modrinth';

  @override
  String get sourceCurseForge => 'CurseForge';

  @override
  String get sourceCurseForgeKeyHint => '请在设置中配置 CurseForge API Key 后再使用该源。';

  @override
  String get providerNotWired => '下载源接线是下一步；当前为 UI 结构。';

  @override
  String get loaderInstallTitle => '安装 Forge / NeoForge';

  @override
  String get loaderKindLabel => '加载器';

  @override
  String get loaderGameVersionLabel => '游戏版本';

  @override
  String get loaderVersionLabel => '加载器版本';

  @override
  String get loaderInstallerLabel => '安装包 jar';

  @override
  String get loaderChooseInstaller => '选择安装包';

  @override
  String get loaderInstallAction => '下载并写入版本';

  @override
  String get loaderInstalling => '下载并写入中…';

  @override
  String get loaderInstallDone => '加载器文件已下载，版本 JSON 已写入，processors 已完成。';

  @override
  String get loaderInstallPickInstaller => '请先选择 Forge/NeoForge 安装包。';

  @override
  String get loaderInstallFailed => '加载器安装失败。';

  @override
  String get searchDownload => '搜索资源';

  @override
  String get downloadSearchAction => '搜索';

  @override
  String get downloadSearchEmpty => '没有找到兼容的项目。';

  @override
  String get downloadSearchError => '搜索失败';

  @override
  String get downloadInstall => '安装';

  @override
  String get downloadInstalling => '下载中…';

  @override
  String get downloadInstalled => '已安装';

  @override
  String get downloadSelectVersion => '选择版本';

  @override
  String get downloadNoVersions => '没有兼容版本。';

  @override
  String downloadResult(String status) {
    return '结果：$status';
  }

  @override
  String get downloadRootLabel => '安装目录';

  @override
  String get commonClose => '关闭';

  @override
  String get comingSoon => '即将支持';

  @override
  String get versionStable => '正式版';

  @override
  String get versionSnapshot => '快照';

  @override
  String get accountsTitle => '账户';

  @override
  String get accountsSubtitle => '微软、离线与第三方 authlib 账户。';

  @override
  String get currentAccount => '当前账户';

  @override
  String get addAccount => '添加账户';

  @override
  String get accountMicrosoftDesc => '使用浏览器登录微软账户。';

  @override
  String get accountOfflineDesc => '用于单人和局域网的本地档案。';

  @override
  String get accountThirdPartyDesc => '通过 authlib-injector 接入 Yggdrasil。';

  @override
  String get refresh => '刷新';

  @override
  String get logout => '退出登录';

  @override
  String get settingsTitle => '设置';

  @override
  String get settingsSubtitle => '外观、存储、Java 与关于。';

  @override
  String get settingsGeneral => '通用';

  @override
  String get settingsAppearance => '外观';

  @override
  String get settingsStorage => '存储';

  @override
  String get settingsInstancesRoot => '实例目录';

  @override
  String get settingsInstanceRoots => '实例存储位置';

  @override
  String get settingsAddRoot => '添加目录';

  @override
  String get settingsDefaultRoot => '默认';

  @override
  String get settingsMakeDefault => '设为默认';

  @override
  String get settingsRemoveRoot => '移除目录';

  @override
  String get settingsRootsUpdated => '实例目录已更新。';

  @override
  String get settingsDataRoot => '应用数据目录';

  @override
  String get settingsChooseFolder => '选择目录';

  @override
  String get settingsSaved => '已保存';

  @override
  String get settingsJava => 'Java';

  @override
  String get settingsJavaAuto => '自动选择';

  @override
  String get settingsJavaAutoHint => '按游戏版本选择最合适的 Java 运行时。';

  @override
  String get settingsJavaCurrent => '当前 Java';

  @override
  String get settingsJavaAutoValue => '自动（按游戏版本）';

  @override
  String get settingsJavaUseAuto => '改用自动选择';

  @override
  String get settingsJavaSelected => 'Java 选择已保存。';

  @override
  String get settingsMemory => '内存';

  @override
  String get settingsJvmArgs => 'JVM 参数';

  @override
  String get settingsRuntimeHint => '运行参数将在下次启动时生效，修改会自动保存。';

  @override
  String get settingsMemoryMin => '最小内存（MB）';

  @override
  String get settingsMemoryMax => '最大内存（MB）';

  @override
  String get settingsJvmArgsHint => '可选，空格分隔';

  @override
  String get settingsGc => '垃圾回收器';

  @override
  String get settingsGcAuto => '自动';

  @override
  String get settingsGcG1 => 'G1GC';

  @override
  String get settingsGcZgc => 'ZGC（Java 21+）';

  @override
  String get settingsGcParallel => 'Parallel';

  @override
  String get settingsGcSerial => 'Serial';

  @override
  String get settingsGcShenandoah => 'Shenandoah';

  @override
  String get settingsGcEpsilon => 'Epsilon（测试）';

  @override
  String get settingsGameArgs => '附加游戏参数';

  @override
  String get settingsGameArgsHint => '可选，追加在版本参数之后';

  @override
  String get settingsMemoryInvalid => '内存需在 128–65536 MB 之间，且最小值不能大于最大值。';

  @override
  String get settingsAbout => '关于';

  @override
  String get settingsTheme => '主题';

  @override
  String get settingsThemeSystem => '跟随系统';

  @override
  String get settingsLanguage => '语言';

  @override
  String get settingsMotion => '减少动效';

  @override
  String get settingsMotionDesc => '降低页面过渡与背景效果的强度。';

  @override
  String get settingsDiagnostics => '诊断';

  @override
  String get settingsDiagnosticsDesc => '日志、模块版本与崩溃报告。';

  @override
  String get settingsVersion => '版本';

  @override
  String get settingsLicense => '许可证';

  @override
  String get settingsSource => '源代码';

  @override
  String get javaStatusTitle => 'Java';

  @override
  String get javaStatusIdle => '空闲';

  @override
  String get javaStatusScanning => '扫描中';

  @override
  String get javaStatusReady => '就绪';

  @override
  String get javaStatusUnavailable => '不可用';

  @override
  String get javaStatusIdleHint => '扫描系统中已安装的 Java 运行时。';

  @override
  String get javaStatusScanningHint => '正在调用 java.scan…';

  @override
  String javaStatusProgress(int done, int total) {
    return '进度：$done/$total';
  }

  @override
  String get javaStatusUnavailableHint => 'Java 扫描不可用，业务模块可能尚未构建。';

  @override
  String get javaStatusScan => '扫描 Java';

  @override
  String get javaStatusSelect => '使用此 Java';

  @override
  String javaStatusCount(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '找到 $count 个 Java 运行时',
      zero: '未发现 Java 运行时',
    );
    return '$_temp0';
  }

  @override
  String javaStatusMeta(String version, String arch) {
    return '$version · $arch';
  }

  @override
  String javaStatusScanFailed(String message) {
    return '扫描失败：$message';
  }

  @override
  String get javaStatusEmptyHint => '请安装 JDK 或设置 JAVA_HOME，然后重新扫描。';

  @override
  String get javaStatusNoResultsTitle => '未找到 Java';

  @override
  String get javaStatusNoResultsHint => '在默认位置未找到 Java。请搜索文件夹或选择 java 可执行文件。';

  @override
  String get javaStatusSearchFolder => '搜索文件夹';

  @override
  String get javaStatusSelectExecutable => '选择 java 可执行文件';

  @override
  String get javaStatusAddFolder => '添加文件夹';

  @override
  String get javaStatusStillNotFound => '仍未找到 — 请选择 java 可执行文件。';

  @override
  String javaStatusNoValidExecutable(String path) {
    return '此路径下没有有效的 Java：$path';
  }

  @override
  String get javaStatusNoValidExecutableHint =>
      '请选择 JDK 目录中的 bin/java（Windows 为 bin\\java.exe）。';

  @override
  String get javaStatusCustomPaths => '自定义路径';

  @override
  String javaStatusRemovePath(String path) {
    return '移除 $path';
  }

  @override
  String get homeFootnote =>
      'ArchoeraCX 不是官方 Minecraft 产品，与 Mojang 或 Microsoft 无关联，也未获其批准。';

  @override
  String javaStatusViewAll(int count) {
    return '查看全部 $count 个';
  }

  @override
  String javaStatusAllTitle(int count) {
    return 'Java 运行时（$count）';
  }

  @override
  String get close => '关闭';

  @override
  String get javaStatusSelectJdkHome => '选择 JDK 目录';

  @override
  String get accountMicrosoftTitle => '微软登录';

  @override
  String get accountDeviceCodeTitle => '输入此代码';

  @override
  String accountDeviceCodeHint(String uri) {
    return '在浏览器打开 $uri，然后输入下方代码。';
  }

  @override
  String get accountDeviceCodeWaiting => '等待微软授权…';

  @override
  String get accountDeviceCodeCopied => '代码已复制。';

  @override
  String accountDeviceCodeExpires(int minutes) {
    return '代码约 $minutes 分钟后过期。';
  }

  @override
  String get accountBrowserSignInHint => '已在浏览器中打开登录页，请使用微软账户完成登录。';

  @override
  String get accountBrowserWaitingCallback => '等待浏览器回调…';

  @override
  String get accountOpenBrowser => '打开浏览器';

  @override
  String get accountBrowserOpenFailed => '无法自动打开浏览器，请手动打开下方链接。';

  @override
  String get accountAddOffline => '添加离线账户';

  @override
  String get accountOfflineNameLabel => '玩家名';

  @override
  String get accountOfflineNameHint => '3–16 个字符';

  @override
  String get accountRemove => '移除账户';

  @override
  String accountRemoveConfirm(String name) {
    return '移除“$name”？不会删除游戏文件。';
  }

  @override
  String get accountRefresh => '刷新会话';

  @override
  String get accountNoAccounts => '还没有账户。添加微软或离线账户后即可启动游戏。';

  @override
  String accountLoginFailed(String message) {
    return '登录失败：$message';
  }

  @override
  String get accountWaitingAuthorization => '等待授权';

  @override
  String get accountSignIn => '登录';

  @override
  String get accountCancelSignIn => '取消登录';

  @override
  String get accountSessionRefreshed => '会话已刷新。';

  @override
  String get accountRemoved => '账户已移除。';

  @override
  String get accountSelected => '已切换当前账户。';

  @override
  String get commonCopy => '复制';

  @override
  String get commonAdd => '添加';

  @override
  String get instanceSelected => '当前';

  @override
  String get loaderVanilla => '原版';

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
  String get accountMicrosoft => '微软';

  @override
  String get accountAuthlib => 'Authlib';

  @override
  String get settingsDiagnosticsOpen => '打开';

  @override
  String get settingsLanguageSystem => '跟随系统';

  @override
  String get settingsLanguageEnglish => 'English';

  @override
  String get settingsLanguageChinese => '简体中文';

  @override
  String get settingsLanguageChanged => '语言已切换。';

  @override
  String get resourceVersionRelease => '正式版';

  @override
  String get resourceVersionBeta => 'Beta';

  @override
  String get resourceVersionAlpha => 'Alpha';

  @override
  String get javaStatusUnknownVersion => '未知';

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
      '已使用选定的 Java 执行 Forge/NeoForge processors。';

  @override
  String instancesCount(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count 个实例',
    );
    return '$_temp0';
  }

  @override
  String instancesLoadFailed(String message) {
    return '实例列表加载失败：$message';
  }

  @override
  String accountsLoadFailed(String message) {
    return '账户列表加载失败：$message';
  }

  @override
  String get downloadGlobalTarget => '全部版本 · 全部加载器';

  @override
  String get instanceManage => '管理';

  @override
  String get instanceManageLoader => '安装 / 更新加载器';

  @override
  String get instanceManageMods => '管理 Mod';

  @override
  String get instanceModsTitle => '已安装 Mod';

  @override
  String get instanceModsEmpty => '此实例没有 Mod。';

  @override
  String instanceModsLoadFailed(String message) {
    return 'Mod 列表加载失败：$message';
  }

  @override
  String get instanceModDelete => '删除 Mod';

  @override
  String instanceModDeleteConfirm(String name) {
    return '删除“$name”？这会移除 Mod 文件。';
  }

  @override
  String get instanceModDisabled => '已禁用';

  @override
  String get instanceModsToggleFailed => '无法更新 Mod 状态。';

  @override
  String get instanceModsRemoveFailed => '无法删除 Mod。';

  @override
  String get accountDefaultSkin => '默认外观';

  @override
  String get settingsMemorySystem => '系统内存';

  @override
  String get settingsMemoryTotal => '总内存';

  @override
  String get settingsMemoryAvailable => '可用';

  @override
  String get settingsMemoryEstimated => '启动后预计';

  @override
  String get settingsMemoryUnavailable => '暂不可用';

  @override
  String get settingsMemoryOverAvailable => '预计占用超过当前可用内存。';

  @override
  String downloadResults(num count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count 个结果',
      one: '1 个结果',
      zero: '没有结果',
    );
    return '$_temp0';
  }

  @override
  String get downloadSort => '排序';

  @override
  String get downloadSortRelevance => '相关度';

  @override
  String get downloadSortDownloads => '下载量';

  @override
  String get downloadSortFollows => '关注数';

  @override
  String get downloadSortNewest => '最新发布';

  @override
  String get downloadSortUpdated => '最近更新';

  @override
  String get downloadScreenshots => '图片';

  @override
  String downloadDependencies(num count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count 个依赖',
      one: '1 个依赖',
      zero: '无依赖',
    );
    return '$_temp0';
  }

  @override
  String get downloadLoadMore => '加载更多';

  @override
  String get downloadImagePreview => '图片预览';

  @override
  String downloadByAuthor(Object author) {
    return '作者：$author';
  }

  @override
  String get loaderVersionSelect => '选择加载器版本';

  @override
  String get loaderVersionSearch => '搜索版本';

  @override
  String get loaderVersionLatest => '最新';

  @override
  String get loaderVersionRecommended => '推荐';

  @override
  String get loaderVersionBeta => '测试版';

  @override
  String get loaderVersionLoadFailed => '无法加载加载器版本。';

  @override
  String get loaderVersionEmpty => '该游戏版本没有可用的加载器版本。';

  @override
  String get downloadDescription => '描述';

  @override
  String get instanceInstallGame => '下载游戏本体';

  @override
  String get instanceInstallGameTitle => '安装游戏本体';

  @override
  String get instanceInstallGameHint => '下载原版客户端、库与资源文件，并写入启动所需的版本 JSON。';

  @override
  String get instanceInstallGameRunning => '正在下载游戏文件…';

  @override
  String get instanceInstallGameDone => '游戏文件已就绪。';

  @override
  String get instanceInstallGameFailed => '游戏本体安装失败。';

  @override
  String get commonRetry => '重试';

  @override
  String get taskCenterTitle => '任务';

  @override
  String get taskCenterEmpty => '暂无进行中的任务。';

  @override
  String taskCenterActive(Object count) {
    return '$count 个进行中';
  }

  @override
  String get taskCenterCompleted => '已完成';

  @override
  String get taskCenterFailed => '失败';

  @override
  String get taskCenterCancelled => '已取消';

  @override
  String get taskCenterCancel => '取消';

  @override
  String get taskCenterClear => '清除已完成';

  @override
  String get taskCenterDownload => '下载';

  @override
  String get taskCenterLoader => '加载器';

  @override
  String get taskCenterModpack => '整合包';

  @override
  String get taskCenterGameFiles => '游戏文件';

  @override
  String get taskCenterJavaScan => 'Java 扫描';

  @override
  String get taskCenterTask => '任务';

  @override
  String taskCenterSpeed(Object rate) {
    return '$rate/s';
  }

  @override
  String get taskCenterWorking => '处理中';

  @override
  String get resourceNeedsLoader =>
      '请先选择一个 Fabric/Forge/NeoForge/Quilt 实例，再安装 Mod。';

  @override
  String get instanceVersionIsolation => '版本隔离';

  @override
  String get instanceVersionIsolationHint =>
      '游戏数据放在 versions/<name>，库和资源文件与父级游戏目录共享。';

  @override
  String get accountPremiumRequired => '请先登录拥有 Minecraft 的微软账户，再添加或选择其它账户。';

  @override
  String get accountPremiumRequiredHint => '发布版需要先登录已拥有 Minecraft 的正版微软账户。';

  @override
  String get downloadUnavailableTitle => '该地区下载已禁用';

  @override
  String get downloadUnavailableMessage =>
      '检测到当前网络位于中国大陆，下载已按本地地区策略禁用。请在配置文件中添加所需字段，然后重新检查。已有实例和本地启动功能仍可使用。';

  @override
  String downloadConfigPath(Object path) {
    return '配置文件：$path';
  }

  @override
  String get downloadConfigFields =>
      '需要字段：developerMode=true、acceptRisk=true、downloadLevel=L2、acknowledgement=ENABLE-DOWNLOADS 1';

  @override
  String get downloadConfigReload => '重新检查配置';

  @override
  String get accountMicrosoftOnly => '该地区只允许使用已验证的微软账户。';

  @override
  String get accountMicrosoftOnlyHint => '该地区仅允许微软验证账户，离线账户和第三方账户已禁用。';

  @override
  String get accountsSubtitleMicrosoftOnly => '该地区仅支持微软验证账户。';

  @override
  String get accountNoAccountsMicrosoftOnly => '添加微软账户后即可开始使用。';

  @override
  String get launchOnlineNotReady => '当前版本尚未启用在线启动。';

  @override
  String get developmentBuildBadge => '开发版';

  @override
  String get developmentBuildWarning => '开发版：功能不完整且可能损坏数据，请勿用于正式环境或重要实例。';

  @override
  String get launchOnlineSessionExpired => 'Minecraft 会话已过期，请重新登录。';

  @override
  String get launchMicrosoftAccountRequired => '请选择微软账户进行在线启动。';

  @override
  String get launchOnlineNotConfigured => '当前构建未配置在线启动。';

  @override
  String get accountVerifiedRequiredNotice =>
      '当前启动器需要已验证的 Microsoft 账户才能启动游戏。你仍然可以下载和管理实例文件；但离线/本地账户不会在未验证正版账户时解锁。';

  @override
  String get accountLoginSuccessNotificationTitle => 'ArchoeraCX 登录成功';

  @override
  String accountLoginSuccessNotificationBody(String account) {
    return '已登录为 $account。';
  }

  @override
  String get settingsAppearanceNote => '主题、主题色与界面效果会即时生效。';

  @override
  String get settingsAppearancePersonalization => '个性化';

  @override
  String get settingsAppearanceLanguage => '语言与区域';

  @override
  String get settingsDensity => '界面密度';

  @override
  String get settingsDensityDesc => '调整控件间距与列表高度。';

  @override
  String get settingsDensityCompact => '紧凑';

  @override
  String get settingsDensityStandard => '标准';

  @override
  String get settingsDensityComfortable => '宽松';

  @override
  String get settingsThemeMode => '颜色模式';

  @override
  String get settingsThemeModeDesc => '使用浅色、深色，或跟随系统外观。';

  @override
  String get settingsThemeLight => '浅色';

  @override
  String get settingsThemeDark => '深色';

  @override
  String get settingsThemeAccent => '主题色';

  @override
  String get settingsThemeAccentDesc => '使用系统强调色，或为启动器选择一种颜色。';

  @override
  String get settingsThemeAccentSystem => '系统';

  @override
  String get settingsThemeAccentCustom => '自定义';

  @override
  String get settingsThemeAccentCustomHint => '输入十六进制颜色，例如 #4F7CFF。';

  @override
  String get settingsThemeAccentInvalid => '请输入有效的十六进制颜色。';

  @override
  String get settingsGlobalTint => '全局着色';

  @override
  String get settingsGlobalTintDesc => '让面板与背景轻微偏向主题色。';

  @override
  String get settingsResetAppearance => '恢复默认外观';

  @override
  String get settingsAppearanceResetDone => '已恢复默认外观。';

  @override
  String get settingsAboutDesc => '版本、运行环境、许可证与社区链接。';

  @override
  String get settingsAboutTagline => '跨平台 Minecraft: Java Edition 启动器';

  @override
  String get settingsAboutEnvironment => '环境信息';

  @override
  String get settingsAboutBuildInfo => '构建';

  @override
  String get settingsAboutPlatform => '系统';

  @override
  String get settingsAboutRuntime => '运行时';

  @override
  String get settingsAboutCore => '原生核心';

  @override
  String get settingsAboutCoreUnknown => '未连接';

  @override
  String get settingsAboutCopyEnvironment => '复制环境信息';

  @override
  String get settingsAboutCopied => '环境信息已复制到剪贴板。';

  @override
  String get settingsAboutCommunity => '社区与支持';

  @override
  String get settingsAboutRepository => '源代码';

  @override
  String get settingsAboutIssues => '问题反馈';

  @override
  String get settingsAboutThanks => '特别致谢';

  @override
  String get settingsAboutThanksDesc => '本构建依赖的开源项目与服务。';

  @override
  String get settingsAboutLegal => '法律与许可';

  @override
  String get settingsAboutPrivacy => '隐私政策';

  @override
  String get settingsAboutSecurity => '安全策略';

  @override
  String get settingsAboutThirdParty => '第三方许可';

  @override
  String get settingsAboutThirdPartyDesc => '查看随本构建分发的开源组件。';

  @override
  String get settingsAboutDisclaimer =>
      'NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.';

  @override
  String get settingsAboutOpenFailed => '无法打开链接。';

  @override
  String get instanceViewLogs => '查看日志';

  @override
  String get gameLogTitle => '游戏日志';

  @override
  String get gameLogEmpty => '暂无日志。启动一次该实例后，输出会显示在这里。';

  @override
  String get gameLogNoOutput => '该日志为空。';

  @override
  String get gameLogLoadFailed => '读取游戏日志失败。';

  @override
  String get gameLogRunning => '运行中';

  @override
  String get gameLogStopped => '已结束';

  @override
  String get gameLogFollow => '跟随';

  @override
  String get gameLogFollowHint => '游戏运行时持续接收新日志行。';

  @override
  String get gameLogCopy => '复制全部';

  @override
  String get gameLogCopied => '日志已复制到剪贴板。';

  @override
  String get gameLogFileLabel => '日志文件';

  @override
  String gameLogTruncated(int count) {
    return '仅显示最后 $count 行。';
  }

  @override
  String get taskCenterLaunch => '启动';

  @override
  String taskCenterTaskWithTarget(String category, String target) {
    return '$category · $target';
  }

  @override
  String get navWiki => 'Wiki';

  @override
  String get wikiSubtitle => 'Minecraft 社区动态与系统负载。';

  @override
  String get wikiSystemTitle => '系统负载';

  @override
  String get wikiFeedTitle => '社区动态';

  @override
  String get wikiFeedSubtitle => '官方新闻、Java 版本与 Wiki 编辑。';

  @override
  String get wikiRefresh => '刷新';

  @override
  String wikiUpdatedAt(String time) {
    return '更新于 $time';
  }

  @override
  String get wikiSourceNews => '官方新闻';

  @override
  String get wikiSourceJava => 'Java 版本';

  @override
  String get wikiSourceWiki => 'Wiki 编辑';

  @override
  String get wikiKindNews => '新闻';

  @override
  String get wikiKindRelease => '正式版';

  @override
  String get wikiKindSnapshot => '快照';

  @override
  String get wikiKindEdit => '编辑';

  @override
  String get wikiKindNew => '新建';

  @override
  String get wikiFeedEmpty => '暂时没有内容。';

  @override
  String get wikiFeedFailed => '无法加载社区动态';

  @override
  String get wikiStatusFailed => '无法读取系统负载';

  @override
  String get wikiUnavailable => '不可用';

  @override
  String get wikiCpuLabel => 'CPU';

  @override
  String get wikiMemoryLabel => '内存';

  @override
  String get wikiDiskLabel => '磁盘';

  @override
  String get wikiLauncherLabel => '启动器';

  @override
  String wikiCoresCount(int count) {
    return '$count 线程';
  }

  @override
  String wikiLoadAverage(String value) {
    return '负载 $value';
  }

  @override
  String wikiMemoryUsage(String used, String total) {
    return '$used / $total GB';
  }

  @override
  String wikiDiskFree(String free) {
    return '可用 $free';
  }

  @override
  String wikiUptime(String duration) {
    return '已运行 $duration';
  }

  @override
  String wikiSystemUptime(String duration) {
    return '系统已运行 $duration';
  }

  @override
  String get wikiTimeJustNow => '刚刚';

  @override
  String wikiTimeMinutesAgo(int count) {
    return '$count 分钟前';
  }

  @override
  String wikiTimeHoursAgo(int count) {
    return '$count 小时前';
  }

  @override
  String wikiTimeDaysAgo(int count) {
    return '$count 天前';
  }

  @override
  String get wikiOpenLinkFailed => '无法打开链接';

  @override
  String wikiSourceError(String error) {
    return '获取失败：$error';
  }

  @override
  String get wikiRetry => '重试';

  @override
  String get wikiNoSourceData => '该来源暂无内容';

  @override
  String get wikiOpenInBrowser => '在浏览器中打开';

  @override
  String get javaSearchHint => '按版本、厂商、架构或路径筛选';

  @override
  String get javaSelectedBadge => '使用中';

  @override
  String get javaFilteredEmpty => '没有匹配的 Java。';

  @override
  String get back => '返回';

  @override
  String get javaPageSubtitle => '本机扫描到的全部 Java 运行环境，选择启动游戏时使用哪一个。';

  @override
  String get javaPageManage => 'Java 运行环境';

  @override
  String get javaPageManageHint => '查看全部扫描结果，添加目录或指定 java 可执行文件。';
}
