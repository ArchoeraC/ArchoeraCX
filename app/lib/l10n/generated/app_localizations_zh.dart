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
  String get navTasks => '任务';

  @override
  String get navNotifications => '通知';

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
  String get commonNoTasks => '暂无进行中的任务。';

  @override
  String get commonNoNotifications => '暂无通知。';

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
  String get launchNoAccount => '请先添加离线账户。';

  @override
  String get launchOfflineOnly => '当前基础启动仅支持离线账户。';

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
  String get downloadSubtitle => '游戏版本、加载器、Mod、整合包、资源包与光影。';

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
  String get tabLoaders => '加载器';

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
  String get loaderInstallNoInstance => '请先选择实例。';

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
  String get accountMicrosoftDesc => '使用微软设备码登录。';

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
  String get settingsSubtitle => '外观、Java、网络、合规与诊断。';

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
  String settingsMemoryValue(int min, int max) {
    return '$min – $max MB';
  }

  @override
  String get settingsJvmArgs => 'JVM 参数';

  @override
  String get settingsJvmArgsValue => '默认';

  @override
  String get settingsRuntimeTitle => 'Java 与内存';

  @override
  String get settingsRuntimeHint => '内存设置将在下次启动时生效。';

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
  String get settingsGameArgsValue => '默认';

  @override
  String get settingsGameArgsHint => '可选，追加在版本参数之后';

  @override
  String get settingsRuntimeSaved => '运行参数已保存。';

  @override
  String get settingsMemoryInvalid => '内存需在 128–65536 MB 之间，且最小值不能大于最大值。';

  @override
  String get settingsNetwork => '网络';

  @override
  String get settingsCompliance => '合规';

  @override
  String get settingsAbout => '关于';

  @override
  String get settingsTheme => '主题';

  @override
  String get settingsThemeSystem => '跟随系统';

  @override
  String get settingsLanguage => '语言';

  @override
  String get settingsMotion => '动效';

  @override
  String get settingsMotionDesc => '减少页面过渡与背景效果。';

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
  String get coreStatusTitle => '核心状态';

  @override
  String get coreStatusIdle => '空闲';

  @override
  String get coreStatusConnecting => '探测中';

  @override
  String get coreStatusConnected => '已连接';

  @override
  String get coreStatusUnavailable => '核心不可用';

  @override
  String get coreStatusIdleHint => '原生核心已加载，点击探测以验证 ABI。';

  @override
  String get coreStatusPingingHint => '正在调用 system.pingAsync…';

  @override
  String coreStatusProgress(int done, int total) {
    return '进度：$done/$total';
  }

  @override
  String get coreStatusUnavailableHint => '未找到原生核心库，或无法加载。';

  @override
  String get coreStatusPing => '探测核心';

  @override
  String coreStatusPong(String value) {
    return 'pong：$value';
  }

  @override
  String coreStatusMethod(String method) {
    return '方法：$method';
  }

  @override
  String coreStatusAbiVersion(int version) {
    return 'ABI v$version';
  }

  @override
  String coreStatusBuildInfo(String build) {
    return '构建：$build';
  }

  @override
  String coreStatusPingFailed(String message) {
    return '探测失败：$message';
  }

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
  String get settingsProxy => '代理';

  @override
  String get settingsProxySystem => '跟随系统';

  @override
  String get settingsDownloads => '下载';

  @override
  String settingsDownloadsStreams(int count) {
    return '$count 个并发';
  }

  @override
  String get settingsProvider => '内容源';

  @override
  String get settingsDownloadLevel => '下载级别';

  @override
  String get settingsDownloadLevelValue => 'L0 · 官方中国';

  @override
  String get settingsConsent => '用户同意';

  @override
  String get settingsConsentNotAccepted => '未接受';

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
}
