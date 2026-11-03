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
  String launchWithInstance(String name) {
    return '启动“$name”';
  }

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
  String get packInstallDone => '整合包安装完成。';

  @override
  String get packInstallFailed => '整合包安装失败。';

  @override
  String get packBlockedTitle => '已阻止不安全的整合包';

  @override
  String get packBlockedBody =>
      '该整合包试图写出实例目录、创建符号链接，或解压到不合理的大小（zip 炸弹）。启动器已在安装前停止。除非你信任来源，否则不要安装。';

  @override
  String get packInspectFailed => '该文件不是受支持的整合包。';

  @override
  String packRepaired(String path) {
    return '整合包使用了 Windows 路径分隔符，已备份到 $path 并自动修复。';
  }

  @override
  String get packLoaderUnsupported =>
      '暂不支持该整合包加载器。支持原版、Fabric、Quilt、Forge 与 NeoForge。';

  @override
  String get packJavaMissing =>
      '该整合包需要 Java 来运行加载器 processors。请先在设置中安装或选择匹配的 Java。';

  @override
  String get packTargetNewInstance => '将新建实例';

  @override
  String get instanceCreateTitle => '创建实例';

  @override
  String get instanceNameLabel => '名称';

  @override
  String get instanceNameHint => '例如 1.21.4 · Fabric';

  @override
  String instanceNameTooLong(int max) {
    return '名称最多 $max 个字符。';
  }

  @override
  String get instanceNameInvalidCharacters =>
      '名称不能包含 \\ / : * ? \" < > | 或控制字符。';

  @override
  String get instanceNameInvalidEdges => '名称不能以点开头或结尾。';

  @override
  String get instanceNameReserved => '该名称是 Windows 保留设备名，请换一个。';

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
  String get instanceDelete => '删除实例';

  @override
  String get instanceDuplicate => '复制实例';

  @override
  String get instanceDuplicateTitle => '复制实例';

  @override
  String get instanceDuplicateName => '新名称';

  @override
  String get instanceDuplicateNameHint => '例如 1.21.4 · Fabric 副本';

  @override
  String get instanceDuplicateSuffix => '副本';

  @override
  String get instanceDuplicateHint => '复制游戏目录、Mod 与配置（不含日志），并保留实例设置。';

  @override
  String get instanceDuplicateAction => '复制';

  @override
  String instanceDuplicateDone(String name) {
    return '已创建“$name”。';
  }

  @override
  String get instanceDuplicateFailed => '复制实例失败。';

  @override
  String get instanceDuplicateInvalidName => '请输入实例名称。';

  @override
  String instanceDeleteConfirm(String name) {
    return '删除“$name”？';
  }

  @override
  String get instanceDeleteBody => '实例将从启动器列表中移除。';

  @override
  String get instanceDeleteFiles => '同时删除本地文件';

  @override
  String get instanceDeleteFilesHint => '删除实例目录中的游戏文件、Mod、日志与配置，无法恢复。';

  @override
  String get instanceDeleteFilesConfirmTitle => '确认删除本地文件？';

  @override
  String instanceDeleteFilesConfirmBody(String path) {
    return '将永久删除以下目录及其全部内容：\n$path';
  }

  @override
  String get instanceDeleteFilesConfirmAction => '永久删除';

  @override
  String get instanceDeleteDone => '实例已删除。';

  @override
  String get instanceDeleteDoneFiles => '实例及其本地文件已删除。';

  @override
  String get instanceDeleteFilesKeptMissing => '实例已删除，但本地目录已不存在。';

  @override
  String get instanceDeleteFilesKeptShared => '实例已删除，但目录与其它实例或实例根共用，已保留。';

  @override
  String get instanceDeleteFilesKeptUnsafe => '实例已删除，但目录路径不安全，已保留。';

  @override
  String get instanceDeleteFilesKeptFailed => '实例已删除，但本地文件删除失败，请手动清理。';

  @override
  String get instanceDeleteFilesKeptOutsideRoot => '实例已删除，但目录不在受管理的实例根内，已保留。';

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
  String get instanceCheckRepairQueued => '修复已在后台开始，可在任务中心查看进度。';

  @override
  String get instanceCheckRepairDone => '依赖已修复。';

  @override
  String get instanceCheckRepairFailed => '依赖修复失败。';

  @override
  String get instanceCheckRecheck => '重新检查';

  @override
  String get instanceCheckRechecking => '正在重新检查…';

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
  String get launchLoaderNotInstalled =>
      '该实例尚未安装加载器：请先在实例菜单中安装 Forge/NeoForge，再启动。';

  @override
  String get launchMemoryWarningTitle => '内存偏低';

  @override
  String launchMemoryWarningLow(int max, int recommended) {
    return '当前只分配 $max MB，推荐 $recommended MB。继续启动可能卡顿或崩溃。';
  }

  @override
  String launchMemoryWarningOver(int max, int available) {
    return '分配 $max MB 后系统可用内存不足（当前可用 $available MB）。';
  }

  @override
  String launchMemoryWarningSystem(int total, int max) {
    return '本机内存仅 $total MB，自动分配已限制为 $max MB，重型整合包可能不够。';
  }

  @override
  String get launchAnyway => '仍然启动';

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
  String get sourceCurseForgePending =>
      'CurseForge 在配置 API 访问前不可用；请使用 Modrinth。';

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
  String loaderJavaRequired(String loader) {
    return '$loader 需要 Java 才能完成安装：部分 processor 尚未原生支持。请先在 Java 设置中安装或选择可用的 Java 运行时。';
  }

  @override
  String get loaderShareFiles => '共享加载器文件';

  @override
  String get loaderShareFilesHint =>
      '同一加载器版本的所有实例共用游戏目录下的一份 libraries；合并后的启动 profile 始终放在本实例。取消勾选则 library 也只装进本实例。';

  @override
  String get loaderPureJava => '使用官方安装器（纯 Java 模式）';

  @override
  String get loaderPureJavaHint =>
      '由 Forge/NeoForge 官方安装器完成下载与 processor；适用于 1.12.2 及更新的安装器。';

  @override
  String get loaderInstallMode => '安装方式';

  @override
  String get loaderStandardJava => '标准 Java 流程（启动器原生）';

  @override
  String get loaderStandardJavaHint =>
      '启动器构建计划并原生执行已覆盖的 processor；仍需 Java 的步骤使用所选运行时。1.7.10 及更早的安装器请选此项。';

  @override
  String get loaderJavaInstallUnsupported =>
      '该安装器没有命令行客户端安装（Forge 1.7.10 及更早），无法使用纯 Java 模式。请改选“标准 Java 流程（启动器原生）”。';

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
  String get downloadInstalled => '已安装';

  @override
  String get downloadSelectVersion => '选择版本';

  @override
  String get downloadInstallTarget => '安装到';

  @override
  String get downloadTargetExisting => '现有实例';

  @override
  String get downloadTargetNew => '新建实例';

  @override
  String get downloadTargetDirectory => '自定义目录';

  @override
  String get downloadTargetNewHint => '安装时创建与所选游戏版本、加载器匹配的新实例。';

  @override
  String get downloadTargetDirectoryHint => '选择任意目录，下载文件会写入对应的内容子目录。';

  @override
  String get downloadDirectoryNotSelected => '尚未选择目录。';

  @override
  String get downloadVersionFacetsHint => '先选择游戏版本与加载器，再在这里列出可用的下载方案。';

  @override
  String get downloadInstallSelected => '安装所选版本';

  @override
  String get downloadInstalling => '安装中…';

  @override
  String downloadSelectedVersion(String version) {
    return '已选择：$version';
  }

  @override
  String get downloadVersionPick => '先在下方选择一个要安装的版本。';

  @override
  String get downloadVersionIncompatible => '所选版本与目标实例不兼容，请切换实例或选择其他版本。';

  @override
  String get downloadNoVersions => '没有兼容版本。';

  @override
  String downloadResult(String status) {
    return '结果：$status';
  }

  @override
  String downloadQueued(String name) {
    return '“$name”已加入下载队列，可在任务或页面底部查看进度。';
  }

  @override
  String downloadFinished(String name) {
    return '“$name”下载完成。';
  }

  @override
  String downloadFailed(String name) {
    return '“$name”下载失败。';
  }

  @override
  String downloadCancelled(String name) {
    return '“$name”的下载已取消。';
  }

  @override
  String downloadQueueActive(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count 个下载进行中',
      one: '1 个下载进行中',
    );
    return '$_temp0';
  }

  @override
  String downloadQueueProgress(String done, String total) {
    return '$done / $total';
  }

  @override
  String get downloadQueueOpen => '查看任务';

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
  String get settingsSectionLaunch => '启动';

  @override
  String get settingsAdvanced => '高级';

  @override
  String get settingsAdvancedNote => '未单独覆盖的实例使用这些默认值；实例设置中的覆盖仍然优先。';

  @override
  String get settingsSectionJavaMemory => 'Java 与内存默认值';

  @override
  String get settingsAdvancedJavaMemory => '高级启动默认值';

  @override
  String get settingsAdvancedJavaMemoryHint => '内存、GC 与 JVM 参数已移到高级分区。';

  @override
  String get settingsSectionWindow => '游戏窗口';

  @override
  String get settingsWindowNote =>
      '窗口模式写入原版启动参数（--width / --height / --fullscreen），对支持这些参数的版本生效。最大化会用主显示器的可用尺寸近似。';

  @override
  String get settingsSectionBehavior => '启动器行为';

  @override
  String get settingsHideAfterLaunch => '启动后隐藏启动器';

  @override
  String get settingsHideAfterLaunchDesc => '游戏开始后隐藏此窗口；可从托盘图标重新打开。';

  @override
  String get settingsQuitAfterGameExit => '游戏退出后关闭启动器';

  @override
  String get settingsQuitAfterGameExitDesc => '该游戏进程结束后关闭启动器；正在进行的下载与任务也会随之停止。';

  @override
  String get settingsBehaviorNote => '仅对当前桌面生效。游戏运行期间仍可从实例菜单（查看日志）打开游戏日志。';

  @override
  String get settingsWindowMode => '窗口模式';

  @override
  String get settingsWindowModeDesc => '最大化会按屏幕可用区域设置窗口尺寸；部分版本未必会严格最大化。';

  @override
  String get settingsWindowModeWindowed => '窗口化';

  @override
  String get settingsWindowModeFullscreen => '全屏';

  @override
  String get settingsWindowModeMaximized => '最大化';

  @override
  String get settingsWindowSize => '窗口尺寸';

  @override
  String get settingsWindowSizeDesc => '游戏窗口的初始大小。';

  @override
  String get settingsWindowSizeFullscreen => '全屏启动时忽略该尺寸。';

  @override
  String get settingsWindowSizeMaximized => '选择最大化时会使用主显示器的可用尺寸。';

  @override
  String get settingsSectionPerformance => '性能（Linux）';

  @override
  String get settingsPerformanceNote =>
      '通过包装命令或环境变量生效；若自定义包装命令或环境变量已实现相同效果，则以你的配置为准。这些开关不会改变下载或合规设置。';

  @override
  String get settingsGameMode => 'Feral GameMode';

  @override
  String get settingsGameModeDesc => '安装 GameMode 时通过 gamemoderun 启动游戏。';

  @override
  String get settingsMangoHud => 'MangoHud';

  @override
  String get settingsMangoHudDesc =>
      '通过 mangohud 包装命令注入覆盖层（需系统已安装），并为 Vulkan 场景设置 MANGOHUD=1。';

  @override
  String get settingsZink => 'Zink';

  @override
  String get settingsZinkDesc => '通过环境变量请求 Zink OpenGL-on-Vulkan 驱动。';

  @override
  String get settingsSectionAdvancedNative => '高级原生库（开发者）';

  @override
  String get settingsNativeWarning =>
      '仅影响本地游戏启动，与下载/合规的开发者模式无关。只有确认库的 ABI 时才设置这些路径；GLFW、OpenAL 或 SDL 版本不匹配可能让游戏在主菜单前崩溃。';

  @override
  String get settingsDeveloperMode => '启用原生库覆盖';

  @override
  String get settingsDeveloperModeDesc => '只对本地游戏生效。关闭后仍保留已填路径，但不会应用。';

  @override
  String get settingsNativeGlfw => 'GLFW 库';

  @override
  String get settingsNativeOpenAL => 'OpenAL 库';

  @override
  String get settingsNativeSdl => 'SDL 库';

  @override
  String get settingsNativePathHint => '/绝对路径/到/库文件.so';

  @override
  String get settingsNativePathInvalid => '请输入绝对文件路径。';

  @override
  String get settingsNativePickFile => '选择文件';

  @override
  String get settingsNativePickerLabel => '原生库';

  @override
  String get settingsShareLoaderFiles => '共享加载器文件';

  @override
  String get settingsShareLoaderFilesDesc =>
      'Forge/NeoForge 的库与 processors 产物只装一份在游戏目录，同版本的实例共用；合并后的启动 profile 始终放在各实例自己的文件夹。取消勾选则库也只装进本实例。';

  @override
  String get settingsPreferPureJavaLoaderInstall => '优先使用官方安装器（纯 Java）';

  @override
  String get settingsPreferPureJavaLoaderInstallDesc =>
      '加载器安装弹窗默认选中“使用官方安装器（纯 Java 模式）”；仍可在单次安装中改为标准 Java 流程。1.7.10 及更早的安装器没有客户端命令行，会自动回退到标准流程。';

  @override
  String get settingsSectionGameArgs => '附加游戏参数';

  @override
  String get settingsGameArgsNote => '追加到原版参数之后，按空格拆分；留空表示不加。';

  @override
  String get settingsSectionCommands => '启动命令';

  @override
  String get settingsCommandsNote => '每次启动各执行一次，实例可单独覆盖；留空表示不执行。';

  @override
  String get settingsPreLoadCommand => '加载前命令';

  @override
  String get settingsPreLoadCommandDesc => '在准备实例之前运行。';

  @override
  String get settingsPreLaunchCommand => '启动前命令';

  @override
  String get settingsPreLaunchCommandDesc => '准备完成后、Java 启动前运行；失败会中止启动。';

  @override
  String get settingsPostExitCommand => '退出后命令';

  @override
  String get settingsPostExitCommandDesc => '游戏退出后运行；失败只写入日志。';

  @override
  String get settingsCommandEmptyHint => '留空表示不执行。';

  @override
  String get settingsCommandsHelp =>
      '命令以当前用户身份、不经过 shell、在实例目录中运行。环境变量中可使用 \$INST_NAME、\$INST_ID、\$INST_DIR、\$INST_MC_DIR、\$INST_JAVA 和 \$INST_JAVA_ARGS（账户令牌已脱敏）。请勿在命令中填写账户令牌。';

  @override
  String get settingsSectionProcess => '包装命令与环境变量';

  @override
  String get settingsProcessNote => '仅作用于游戏进程。包装命令以当前用户身份运行；环境变量保存在本地，不会写入日志。';

  @override
  String get settingsWrapperCommand => '包装命令';

  @override
  String get settingsWrapperCommandHint => '例如 gamemoderun、mangohud、prime-run';

  @override
  String get settingsWrapperCommandHelp => '会添加到 Java 命令之前；留空表示不启用。';

  @override
  String get settingsEnvironment => '环境变量';

  @override
  String get settingsEnvironmentHint => 'KEY=VALUE';

  @override
  String get settingsEnvironmentHelp => '每行一个 KEY=VALUE；空行会被忽略。';

  @override
  String get launchWrapperCommandTooLong => '包装命令最多 4096 个字符。';

  @override
  String get launchCommandTooLong => '命令最多 4096 个字符。';

  @override
  String get launchCommandInvalidCharacter => '命令不能包含 NUL 或换行。';

  @override
  String launchCommandUnclosedQuote(String quote) {
    return '引号 $quote 未闭合。';
  }

  @override
  String launchEnvironmentMissingSeparator(int line) {
    return '第 $line 行：请填写 KEY=VALUE。';
  }

  @override
  String launchEnvironmentInvalidKey(int line, String key) {
    return '第 $line 行：“$key”不是有效的变量名。';
  }

  @override
  String launchEnvironmentDuplicateKey(int line, String key) {
    return '第 $line 行：“$key”已定义。';
  }

  @override
  String launchEnvironmentReservedKey(int line, String key) {
    return '第 $line 行：不能在此设置“$key”。';
  }

  @override
  String launchEnvironmentTooMany(int count) {
    return '最多只能设置 $count 个环境变量。';
  }

  @override
  String launchEnvironmentInvalidValue(int line) {
    return '第 $line 行：值最多 4096 个字符且不能包含 NUL。';
  }

  @override
  String get settingsSectionTasks => '任务与日志';

  @override
  String get settingsTasksNote => '这些偏好只影响界面，不写入 settings.json。';

  @override
  String get settingsTaskHistory => '任务中心保留记录';

  @override
  String get settingsTaskHistoryDesc => '已完成任务最多保留多少条；进行中的任务永远不会被清理。';

  @override
  String get settingsLogLines => '日志默认行数';

  @override
  String get settingsLogLinesDesc => '打开日志查看器时加载的末尾行数。';

  @override
  String get settingsSectionNotifications => '通知';

  @override
  String get settingsNotificationsNote => '桌面通知使用系统通知中心，可在系统设置里单独静音。';

  @override
  String get settingsNotifyDesktop => '桌面通知';

  @override
  String get settingsNotifyDesktopDesc => '登录成功等事件发送系统通知。';

  @override
  String get settingsNotifyDownloads => '下载完成通知';

  @override
  String get settingsNotifyDownloadsDesc => '后台下载结束或失败时发送系统通知。';

  @override
  String get settingsToastDuration => '提示停留时长';

  @override
  String get settingsToastDurationDesc => '成功/提示类 Toast 的自动消失时间；错误提示会停留更久。';

  @override
  String get settingsStorage => '存储';

  @override
  String get settingsDownload => '下载';

  @override
  String get settingsDownloadSource => '来源优先';

  @override
  String get settingsDownloadTuning => '并发与连接';

  @override
  String get settingsDownloadSourceDesc => '优先尝试的下载源；另一侧保留为回退。';

  @override
  String get settingsDownloadSourceAuto => '自动';

  @override
  String get settingsDownloadSourceAutoDesc => '按当前地区判断：官方源慢或被阻断时使用镜像。';

  @override
  String get settingsDownloadSourceOfficial => '官方源优先';

  @override
  String get settingsDownloadAutoResolvedMirror => '自动 · 当前使用镜像源（中国大陆）';

  @override
  String get settingsDownloadAutoResolvedOfficial => '自动 · 当前使用官方源';

  @override
  String get settingsDownloadAutoDetecting => '自动 · 正在判断地区…';

  @override
  String get settingsDownloadSourceMirror => '镜像源优先';

  @override
  String get settingsDownloadConcurrency => '并发下载数';

  @override
  String get settingsDownloadConcurrencyDesc => '同时下载的文件（以及大文件分片）数量，1–64。';

  @override
  String get settingsDownloadPerHost => '每站点连接数';

  @override
  String get settingsDownloadPerHostDesc => '单个站点允许的并发流数量，0 表示不限。';

  @override
  String get settingsDownloadHighConcurrencyTitle => '确认启用超高并发？';

  @override
  String get settingsDownloadHighConcurrencyWarning =>
      '除非你知道自己在做什么，否则请不要这样做：超过 32 线程会大幅提高 CPU、内存与网络占用，大量分片还会加速硬盘磨损。';

  @override
  String get settingsDownloadHighConcurrencyContinue => '仍然启用';

  @override
  String settingsDownloadHighConcurrencyActive(int streams, int perHost) {
    return '已启用超高并发（并发 $streams / 每站点 $perHost）：资源占用与硬盘磨损会明显上升。';
  }

  @override
  String get settingsDownloadProxy => '下载代理';

  @override
  String get settingsDownloadRangeThreshold => '大文件分片阈值';

  @override
  String get settingsDownloadRangeThresholdDesc => '大文件改用并发分片下载的体积门槛。';

  @override
  String get settingsRangeDisabled => '关闭分片';

  @override
  String get settingsDownloadChunk => '分片大小';

  @override
  String get settingsDownloadChunkDesc => '单个分片请求的体积。';

  @override
  String get settingsLogRetentionFiles => '日志保留数量';

  @override
  String get settingsLogRetentionFilesDesc => '超出后删除最旧的日志，最新一份始终保留。';

  @override
  String get settingsLogRetentionTotal => '日志总量上限';

  @override
  String get settingsLogRetentionTotalDesc => '超过后同样从最旧开始删除。';

  @override
  String get settingsDownloadProxySystem => '跟随系统代理';

  @override
  String get settingsDownloadProxyHint =>
      'http://主机:端口 或 socks5h://主机:端口；留空则跟随系统代理。';

  @override
  String get settingsDownloadProxyInvalid =>
      '请输入 http(s) 或 socks5 代理地址，例如 http://127.0.0.1:7890。';

  @override
  String get settingsDownloadNote => '数值越大在线路良好时越快，但会占用更多带宽与连接；计费网络或不稳定网络建议调低。';

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
  String get settingsOpenDataRoot => '打开数据目录';

  @override
  String get settingsOpenDataRootDesc => '在文件管理器中查看设置、日志与缓存文件。';

  @override
  String get settingsOpenInstanceRoot => '打开默认实例目录';

  @override
  String get settingsOpenInstanceRootDesc => '在文件管理器中查看存放实例的目录。';

  @override
  String get settingsOpenFailed => '无法打开该目录。';

  @override
  String get settingsStartPage => '启动页';

  @override
  String get settingsStartPageDesc => '下次启动启动器时生效。';

  @override
  String get startPageDashboard => '主页';

  @override
  String get startPageInstances => '实例';

  @override
  String get startPageDownload => '下载';

  @override
  String get startPageAccounts => '账户';

  @override
  String get startPageWiki => 'Wiki';

  @override
  String get startPageSettings => '设置';

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
  String get settingsCloseToTray => '关闭窗口时最小化到托盘';

  @override
  String get settingsCloseToTrayDesc => '关闭主窗口时隐藏到系统托盘，而不是退出启动器。';

  @override
  String get trayShow => '显示主窗口';

  @override
  String get trayQuit => '退出';

  @override
  String get settingsCleanPartials => '清理未完成的下载';

  @override
  String get settingsCleanPartialsDesc =>
      '删除过期（默认 3 天未更新）的 .part 临时文件；进行中的下载会保留。';

  @override
  String get settingsCleanPartialsBody => '将删除所有实例目录中超过 3 天未更新的未完成下载临时文件，无法恢复。';

  @override
  String settingsCleanPartialsDone(int count, String size) {
    return '已删除 $count 个临时文件，释放 $size。';
  }

  @override
  String get settingsCleanPartialsNone => '没有需要清理的临时文件。';

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
  String get javaStatusScanningHint => '正在扫描设备上的 java…';

  @override
  String javaStatusProgress(int done, int total) {
    return '进度：$done/$total';
  }

  @override
  String get javaStatusUnavailableHint => '无法扫描 Java，业务模块可能尚未构建。';

  @override
  String get javaStatusScan => '扫描 Java';

  @override
  String get javaInstallAction => '安装 Java';

  @override
  String get javaInstallPageSubtitle => '选择 JDK 发行版、版本与安装位置。';

  @override
  String get javaInstallProviderLabel => '发行版';

  @override
  String get javaInstallMajorLabel => 'Java 版本';

  @override
  String get javaInstallVersionsLabel => '可用构建';

  @override
  String get javaInstallSecurityHint => '仅推荐每个大版本的最新补丁；历史构建可能包含已修复的 CVE。';

  @override
  String get javaInstallOutdated => '历史';

  @override
  String get javaInstallOutdatedSelected => '该构建不是最新补丁，可能包含已修复的 CVE。';

  @override
  String get javaInstallAllowOutdated => '允许安装历史构建';

  @override
  String get javaInstallAllowOutdatedHint => '仅在你明确需要该构建时开启。';

  @override
  String get javaInstallCveTitle => '安全公告';

  @override
  String get javaInstallCveChecking => '正在通过 NVD/OSV 检查已知 CVE…';

  @override
  String get javaInstallCveNone => '未发现影响该构建的已知 CVE。';

  @override
  String get javaInstallCveUnavailable => '无法获取 CVE 数据，该构建未完成漏洞检查。';

  @override
  String javaInstallCveCount(int count) {
    return '有 $count 个已知 CVE 对该二进制包体存在影响：';
  }

  @override
  String get javaInstallCveAcknowledge => '我了解该构建存在已知 CVE';

  @override
  String get javaInstallCveAcknowledgeHint =>
      '您必须勾选此附加选项后，启动器才能执行后续的 Java 部署程序。';

  @override
  String javaInstallCveScore(String score) {
    return 'CVSS $score';
  }

  @override
  String get javaInstallCveOpen => '查看公告';

  @override
  String get javaInstallLatestOnly => '该发行版只提供每个大版本的可能最新构建。';

  @override
  String javaInstallPlatform(String os, String arch, String archive) {
    return '$os · $arch · $archive';
  }

  @override
  String get javaInstallTargetPlatform => '目标平台';

  @override
  String javaInstallCurrentDevice(String os, String arch) {
    return '当前设备：$os · $arch';
  }

  @override
  String get javaInstallPlatformBadge => '不兼容';

  @override
  String get javaInstallPlatformIncompatible => '该 JDK 面向其他平台，无法在当前设备运行。';

  @override
  String get javaInstallPlatformIncompatibleHint => '仍可下载并保留，供其他设备使用。';

  @override
  String get javaInstallRiskTitle => '确认安装风险';

  @override
  String javaInstallRiskPlatform(
    String targetOs,
    String targetArch,
    String hostOs,
    String hostArch,
  ) {
    return '目标平台 $targetOs · $targetArch 与当前设备（$hostOs · $hostArch）不兼容，安装后无法在此设备运行。';
  }

  @override
  String javaInstallRiskCve(int count, String ids) {
    return '该构建存在 $count 个已知 CVE：\n$ids';
  }

  @override
  String get javaInstallRiskAction => '仍然安装';

  @override
  String javaInstallForeignResult(String os, String arch) {
    return '已下载 $os · $arch 版本；无法在当前设备运行。';
  }

  @override
  String get javaInstallIncompatibleUse => '该 JDK 面向其他平台，无法在此设备选用。';

  @override
  String get javaInstallRecommended => '推荐';

  @override
  String get javaInstallLts => 'LTS';

  @override
  String javaInstallArchiveMeta(String archive, String size) {
    return '$archive · $size';
  }

  @override
  String get javaInstallLoadFailed => '无法加载 JDK 目录。';

  @override
  String get javaInstallFailed => '安装失败。';

  @override
  String get javaInstallMajorsFailed => '无法加载 Java 版本。';

  @override
  String get javaInstallVersionsFailed => '无法加载构建列表。';

  @override
  String get javaInstallEmptyVersions => '当前系统没有可用构建。';

  @override
  String get javaInstallEmptyVersionsHint => '请尝试其他发行版或 Java 版本。';

  @override
  String get javaInstallDirectory => '安装目录';

  @override
  String get javaInstallDirectoryHint => 'JDK 会直接写入该目录；目录必须为空或尚不存在。';

  @override
  String get javaInstallDefaultDirectory => '默认位置';

  @override
  String get javaInstallCustomDirectory => '自定义位置';

  @override
  String get javaInstallChooseDirectory => '选择目录';

  @override
  String get javaInstallUseDefault => '使用默认位置';

  @override
  String get javaInstallStart => '安装 JDK';

  @override
  String get javaInstallDownloadOnlyAction => '下载安装包';

  @override
  String get javaInstallInstalling => '正在安装…';

  @override
  String get javaInstallDownloading => '正在下载 JDK 压缩包…';

  @override
  String get javaInstallExtracting => '正在解压校验后的压缩包…';

  @override
  String get javaInstallCancelling => '正在取消…';

  @override
  String get javaInstallCancelled => '已取消安装。';

  @override
  String javaInstallSuccess(String version) {
    return 'Java $version 安装完成。';
  }

  @override
  String get javaInstallUseRuntime => '使用此 Java';

  @override
  String get javaInstallBackToJava => '返回 Java 页面';

  @override
  String get javaInstallSelected => '已选择该 Java 运行时。';

  @override
  String get javaInstallDirectoryRequired => '请先选择安装目录。';

  @override
  String get javaInstallVersionRequired => '请先选择一个构建。';

  @override
  String get javaInstallProvidersEmpty => '没有注册任何 JDK 发行版。';

  @override
  String get javaInstallExtractOptions => '安装包处理';

  @override
  String get javaInstallAutoExtract => '自动解压';

  @override
  String get javaInstallAutoExtractHint => '解压到安装目录；取消后只把压缩包下载到安装目录旁。';

  @override
  String get javaInstallAutoDelete => '解压后删除安装包';

  @override
  String get javaInstallAutoDeleteHint => '取消后会在安装目录旁保留 tar.gz/zip。';

  @override
  String get javaInstallArchiveOnly => '安装包已下载，未解压。';

  @override
  String javaInstallArchiveKept(String path) {
    return '已保留安装包：$path';
  }

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
      'ArchoeraCX 是独立启动器，并非官方 Minecraft 产品。Minecraft 是 Mojang Synergies AB 的商标。';

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
  String get accountNoAccounts => '还没有添加账户。添加微软或其它账户后即可启动游戏。';

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
  String get accountAuthlib => '第三方';

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
  String get instanceOpenFolder => '打开实例目录';

  @override
  String get instanceOverridesBadge => '已覆盖全局设置';

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
  String get settingsMemoryAuto => '自动内存';

  @override
  String get settingsMemoryAutoDesc => '根据游戏版本、加载器、启用的模组与本机内存自动决定堆大小。';

  @override
  String get settingsMemoryAutoHint => '每个实例按自身需要分配；上面的手动数值会保留但不再生效。';

  @override
  String get settingsGameOptimization => '游戏优化';

  @override
  String get settingsGameOptimizationDesc => '启动时添加调优过的 G1 参数，并优先使用独立显卡。';

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
  String get downloadProjectLoadFailed => '无法加载项目详情。';

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
  String get taskCenterInstanceDuplicate => '复制实例';

  @override
  String get taskCenterGameFiles => '游戏文件';

  @override
  String get taskCenterPreparingGameFiles => '准备游戏文件';

  @override
  String get taskCenterReadingVersionInfo => '正在读取版本信息…';

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
  String get settingsCheckUpdate => '检查更新';

  @override
  String get settingsCheckUpdateDesc => '与 GitHub 最新发布比较（只检查，不自动安装）。';

  @override
  String get updateChecking => '正在检查更新…';

  @override
  String updateUpToDate(String version) {
    return '已是最新版本（$version）。';
  }

  @override
  String get updateAvailableTitle => '发现新版本';

  @override
  String updateAvailableBody(String current, String latest) {
    return '当前 $current，最新 $latest。';
  }

  @override
  String get updateOpenRelease => '打开下载页';

  @override
  String get updateCheckFailed => '检查更新失败。';

  @override
  String get updateCurrentVersion => '当前版本';

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
      '独立启动器。并非官方 Minecraft 产品。Minecraft 是 Mojang Synergies AB 的商标。';

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
  String get gameLogOpenFolder => '打开日志目录';

  @override
  String get gameLogOpenFolderFailed => '无法打开日志目录。';

  @override
  String get gameLogClear => '清理旧日志';

  @override
  String get gameLogClearTitle => '清理旧启动日志？';

  @override
  String get gameLogClearBody => '将删除该实例除当前运行日志外的全部启动日志，无法恢复。';

  @override
  String gameLogClearDone(int count, String size) {
    return '已删除 $count 个日志，释放 $size。';
  }

  @override
  String get gameLogForceStop => '强制终止';

  @override
  String get gameLogForceStopTitle => '强制终止游戏？';

  @override
  String gameLogForceStopBody(String name) {
    return '「$name」正在运行。强制终止会立即结束游戏，未保存的世界进度可能丢失。';
  }

  @override
  String get gameLogForceStopContinue => '继续';

  @override
  String get gameLogForceStopAgainTitle => '仍要强制终止？';

  @override
  String get gameLogForceStopAgainBody => '此操作不可撤销，游戏进程会立即被结束。';

  @override
  String gameLogForceStopAgainBodyPid(int pid) {
    return '此操作不可撤销，进程 $pid 会立即被结束。';
  }

  @override
  String get gameLogForceStopConfirm => '强制终止';

  @override
  String get gameLogForceStopped => '已终止游戏进程。';

  @override
  String get gameLogAlreadyStopped => '游戏已经结束。';

  @override
  String get settingsClearLogs => '清理启动日志';

  @override
  String get settingsClearLogsDesc => '删除所有实例的旧启动日志；运行中的日志会保留。';

  @override
  String get settingsClearLogsBody => '将删除全部实例的旧启动日志（运行中的日志保留），无法恢复。';

  @override
  String gameLogRetentionInfo(String size, int max) {
    return '日志共占用 $size；只保留最近 $max 次启动，超出后自动清理最旧的记录。';
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

  @override
  String get settingsSectionTheme => '主题';

  @override
  String get settingsSectionAccent => '主题色';

  @override
  String get settingsSectionInterface => '界面';

  @override
  String get settingsThemeAccentSystemDesc => '跟随桌面强调色。';

  @override
  String get settingsThemeAccentCustomDesc => '为启动器指定单一强调色。';

  @override
  String get wikiSystemLive => '每 5 秒自动更新';

  @override
  String get loaderPickerVanillaHint => '不安装加载器，直接安装原版游戏。';

  @override
  String loaderVersionCount(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count 个版本',
      zero: '无可用版本',
    );
    return '$_temp0';
  }

  @override
  String get loaderVersionNoMatch => '没有匹配的版本。';

  @override
  String get instanceSettingsTitle => '实例设置';

  @override
  String get instanceSettingsDesc => '留空的项跟随全局设置。';

  @override
  String get instanceSettingsName => '实例名称';

  @override
  String get instanceSettingsNameHint => '仅修改启动器内显示的名称，不移动游戏目录。';

  @override
  String get instanceSettingsNameRequired => '请输入实例名称。';

  @override
  String get instanceSettingsNameTooLong => '实例名称最多 80 个字符。';

  @override
  String get instanceSettingsNameInvalid => '实例名称不能包含控制字符。';

  @override
  String get instanceSettingsMemoryMin => '最小内存（MB）';

  @override
  String get instanceSettingsMemoryMax => '最大内存（MB）';

  @override
  String get instanceSettingsGc => '垃圾回收器';

  @override
  String get instanceSettingsJvmArgs => 'JVM 参数';

  @override
  String get instanceSettingsGameArgs => '游戏参数';

  @override
  String get instanceSettingsWrapperOverride => '覆盖包装命令';

  @override
  String get instanceSettingsWrapperCommand => '包装命令';

  @override
  String get instanceSettingsWrapperCommandHint => '留空表示不启用包装命令。';

  @override
  String get instanceSettingsEnvironmentOverride => '覆盖环境变量';

  @override
  String get instanceSettingsEnvironment => '环境变量';

  @override
  String get instanceSettingsEnvironmentHint => 'KEY=VALUE';

  @override
  String get instanceSettingsEnvironmentHelp => '空列表会清除该实例继承的全局环境变量。';

  @override
  String get instanceSettingsPreLoadOverride => '覆盖加载前命令';

  @override
  String get instanceSettingsPreLaunchOverride => '覆盖启动前命令';

  @override
  String get instanceSettingsPostExitOverride => '覆盖退出后命令';

  @override
  String get instanceSettingsPreLoadCommand => '加载前命令';

  @override
  String get instanceSettingsPreLaunchCommand => '启动前命令';

  @override
  String get instanceSettingsPostExitCommand => '退出后命令';

  @override
  String get instanceSettingsCommandEmptyHint => '留空表示不执行该命令。';

  @override
  String get instanceSettingsCommandsHelp =>
      '命令以当前用户身份、不经过 shell、在实例目录中运行；可使用 \$INST_* 环境变量。启动前命令失败会中止启动，退出后命令失败只写入日志。请勿在命令中填写账户令牌。';

  @override
  String get instanceSettingsAutoJoin => '自动加入';

  @override
  String get instanceSettingsAutoJoinDesc => '游戏启动后自动进入服务器或加载单人世界。';

  @override
  String get instanceSettingsJoinServer => '服务器地址';

  @override
  String get instanceSettingsJoinServerHint =>
      'host、host:port、[IPv6] 或 [IPv6]:port';

  @override
  String get instanceSettingsJoinWorld => '单人世界';

  @override
  String get instanceSettingsJoinWorldHint => '世界文件夹名';

  @override
  String get instanceSettingsAutoJoinNeedsTarget => '请填写服务器地址或单人世界。';

  @override
  String get instanceSettingsAutoJoinConflict => '服务器地址和单人世界只能填一个。';

  @override
  String get instanceSettingsInvalidWrapper => '包装命令最多 4096 个字符。';

  @override
  String get instanceSettingsInvalidServerAddress =>
      '请填写 host、host:port、[IPv6] 或 [IPv6]:port。';

  @override
  String get instanceSettingsInvalidWorldName =>
      '世界名长度需为 1–128，且不能包含 /、\\ 或 ..。';

  @override
  String get instanceSettingsJava => 'Java 可执行文件';

  @override
  String get instanceSettingsJavaHint => '填写 java 可执行文件的绝对路径；留空跟随全局选择。';

  @override
  String get instanceSettingsJavaPick => '从已扫描的 Java 中选择';

  @override
  String get instanceSettingsJavaFollowGlobal => '使用全局 Java 选择';

  @override
  String get instanceSettingsJavaScanning => '正在扫描 Java 运行时…';

  @override
  String get instanceSettingsJavaScanFailed => 'Java 扫描失败，点击重试';

  @override
  String get instanceSettingsJavaNone => '尚未扫描到 Java 运行时';

  @override
  String get instanceSettingsJavaRescan => '重新扫描 Java 运行时';

  @override
  String get instanceSettingsWindow => '游戏窗口';

  @override
  String get instanceSettingsWindowInherit => '跟随全局';

  @override
  String get instanceSettingsWindowFullscreen => '全屏';

  @override
  String get instanceSettingsWindowWindowed => '窗口化';

  @override
  String get instanceSettingsWindowMaximized => '最大化';

  @override
  String get instanceSettingsPerformance => '性能';

  @override
  String get instanceSettingsPerformanceHint =>
      '可跟随全局、强制开启或强制关闭；跟随全局会清除本实例的覆盖。';

  @override
  String get instanceSettingsGameMode => 'Feral GameMode';

  @override
  String get instanceSettingsMangoHud => 'MangoHud';

  @override
  String get instanceSettingsZink => 'Zink';

  @override
  String get instanceSettingsOverrideInherit => '跟随全局';

  @override
  String get instanceSettingsOverrideOn => '开启';

  @override
  String get instanceSettingsOverrideOff => '关闭';

  @override
  String get instanceSettingsWindowWidth => '窗口宽度';

  @override
  String get instanceSettingsWindowHeight => '窗口高度';

  @override
  String get instanceSettingsFollowGlobal => '跟随全局';

  @override
  String get instanceSettingsSave => '保存';

  @override
  String get instanceSettingsSaved => '实例设置已保存。';

  @override
  String instanceSettingsFollowGlobalWith(String value) {
    return '跟随全局（$value）';
  }

  @override
  String instanceSettingsMemoryAuto(int mb) {
    return '自动 · 推荐 $mb MB';
  }

  @override
  String instanceSettingsMemoryLow(int recommended) {
    return '低于推荐的 $recommended MB，可能卡顿或崩溃。';
  }

  @override
  String get instanceSettingsReset => '恢复全局设置';

  @override
  String get instanceSettingsResetBody => '清除该实例的全部覆盖，改为跟随全局设置。';

  @override
  String get instanceSettingsResetDone => '已恢复为全局设置。';

  @override
  String get instanceOverridesMemory => '内存';

  @override
  String get instanceOverridesGc => 'GC';

  @override
  String get instanceOverridesJvm => 'JVM 参数';

  @override
  String get instanceOverridesGameArgs => '游戏参数';

  @override
  String get instanceOverridesWrapper => '包装命令';

  @override
  String get instanceOverridesEnvironment => '环境变量';

  @override
  String get instanceOverridesAutoJoin => '自动加入';

  @override
  String get instanceOverridesJava => 'Java';

  @override
  String get instanceOverridesWindow => '窗口';

  @override
  String get instanceSettingsInvalidNumbers => '内存与窗口尺寸必须是整数。';

  @override
  String get instanceSettingsInvalidRange =>
      '内存需在 128–65536 MB 之间且最小值不大于最大值，窗口需在 640–7680 × 480–4320 之间。';

  @override
  String get instanceSettingsInvalidPath => '请填写 Java 可执行文件的绝对路径。';

  @override
  String get instanceSettingsInvalidGc => '不支持的垃圾回收器。';

  @override
  String instanceSettingsSaveFailed(String message) {
    return '保存实例设置失败：$message';
  }

  @override
  String get crashDialogTitle => '游戏异常退出';

  @override
  String crashDialogExitCode(int code) {
    return '退出码：$code';
  }

  @override
  String get crashDialogNoDetails => '没有匹配到具体原因。请打开启动日志查看最后的错误信息。';

  @override
  String get crashDialogShareHint => '如果你需要寻求他人的帮助，请向他人提供错误日志，而不是这个窗口的照片或者截图。';

  @override
  String get crashDialogForceStopHint => '如果此次终止由您本人操作，请忽略此消息。';

  @override
  String get crashDialogEvidence => '证据';

  @override
  String get crashDialogCopy => '复制诊断';

  @override
  String get crashDialogCopied => '诊断信息已复制。';

  @override
  String get crashDialogClose => '关闭';

  @override
  String get crashRuleJavaTooOldTitle => 'Java 版本过低';

  @override
  String crashRuleJavaTooOldDetail(String needed, String current) {
    return '游戏需要 Java $needed，本次启动使用的是 Java $current。';
  }

  @override
  String crashRuleJavaTooOldFix(String needed) {
    return '请在 设置 → Java 中为实例选择 Java $needed 或更高版本。';
  }

  @override
  String get crashRuleJavaTooNewTitle => 'Java 版本过高';

  @override
  String get crashRuleJavaTooNewDetail => '该旧版加载器不支持当前选择的 Java。';

  @override
  String get crashRuleJavaTooNewFix =>
      '1.12.2/1.16.5 请用 Java 8，1.18–1.20.4 用 Java 17，更新版本用 Java 21。';

  @override
  String get crashRuleHeapTitle => 'Java 内存不足';

  @override
  String get crashRuleHeapDetail => 'JVM 无法申请到配置的堆内存，或堆内存已耗尽。';

  @override
  String get crashRuleHeapFix => '关闭其他程序，调整分配内存，并确认使用的是 64 位 Java。';

  @override
  String get crashRuleMixinTitle => 'Mixin 注入失败';

  @override
  String crashRuleMixinDetail(String config) {
    return 'Mixin $config 注入失败，通常是对应 Mod 与当前 Minecraft/加载器版本不匹配。';
  }

  @override
  String get crashRuleMixinFix => '更新或移除拥有该 Mixin 的 Mod；可先禁用最近添加的 Mod 逐步排查。';

  @override
  String get crashRuleDependencyTitle => '缺少 Mod 前置';

  @override
  String get crashRuleDependencyDetail => '某个 Mod 依赖的前置未安装，或版本不匹配。';

  @override
  String get crashRuleDependencyFix => '安装与当前 Minecraft、加载器版本匹配的前置 Mod。';

  @override
  String get crashRuleMainClassTitle => '游戏或加载器安装不完整';

  @override
  String crashRuleMainClassDetail(String cls) {
    return '无法加载主类 $cls。';
  }

  @override
  String get crashRuleMainClassFix => '重新安装加载器或运行加载器修复后重试。';

  @override
  String get crashRuleGraphicsTitle => '图形/OpenGL 初始化失败';

  @override
  String get crashRuleGraphicsDetail => '游戏无法创建 OpenGL 窗口。';

  @override
  String get crashRuleGraphicsFix => '更新显卡驱动，改用独立显卡运行，并避免远程桌面或虚拟机。';

  @override
  String get crashRuleModTitle => '某个 Mod 启动失败';

  @override
  String crashRuleModDetail(String mod, String stage) {
    return 'Mod $mod 在 $stage 阶段抛出异常。';
  }

  @override
  String get crashRuleModFix => '更新或移除此 Mod；向作者反馈时附上崩溃报告。';

  @override
  String get crashRuleFileLockedTitle => '游戏文件被占用或无权限';

  @override
  String get crashRuleFileLockedDetail => '文件被其他进程占用，或权限不足。';

  @override
  String get crashRuleFileLockedFix => '关闭其他启动器/实例，将游戏移出 OneDrive，并检查目录权限。';

  @override
  String get crashRuleDriverTitle => '显卡驱动崩溃';

  @override
  String crashRuleDriverDetail(String lib) {
    return 'JVM 在 $lib 中崩溃。';
  }

  @override
  String get crashRuleDriverFix => '干净安装显卡驱动，并关闭 RTSS、Discord、Steam 等覆盖层。';

  @override
  String get crashRuleNativeTitle => 'JVM 原生崩溃';

  @override
  String crashRuleNativeDetail(String frame) {
    return 'JVM 在原生代码中崩溃：$frame。';
  }

  @override
  String get crashRuleNativeFix =>
      '查看 hs_err 日志，移除原生 Mod/Agent，并确认 Java 运行时正常。';

  @override
  String get crashRuleReportTitle => '游戏产生了崩溃报告';

  @override
  String crashRuleReportDetail(String description) {
    return '崩溃报告描述：$description';
  }

  @override
  String get crashRuleReportFix => '查看崩溃报告与 Mod 列表，尝试禁用最近添加的 Mod。';

  @override
  String get crashRuleUnknownTitle => '游戏异常退出';

  @override
  String crashRuleUnknownDetail(String code) {
    return '没有匹配到已知的失败特征，退出码为 $code。';
  }

  @override
  String get crashRuleUnknownFix => '打开启动日志和崩溃报告查看最后的错误信息。';

  @override
  String get accountAddAuthlib => '第三方认证';

  @override
  String get accountAuthlibDesc => '通过 LittleSkin 等 Yggdrasil 服务器登录。';

  @override
  String get accountAuthTitle => '第三方认证登录';

  @override
  String get accountAuthServerPreset => '认证服务器';

  @override
  String get accountAuthCustomServer => '自定义服务器';

  @override
  String get accountAuthServerUrl => 'Yggdrasil API 地址';

  @override
  String get accountAuthTermsUrl => '服务条款地址';

  @override
  String get accountAuthTermsTitle => '第三方服务条款';

  @override
  String accountAuthTermsBody(String server) {
    return '你即将登录 $server。请先阅读并同意该服务器的服务条款与隐私政策；启动器只会在你同意后下载 authlib-injector 并提交登录信息。';
  }

  @override
  String get accountAuthOpenTerms => '打开服务条款';

  @override
  String get accountAuthOpenTermsFirst => '请先打开服务条款阅读。';

  @override
  String get accountAuthAcceptTerms => '我已阅读并同意该服务器的服务条款与隐私政策';

  @override
  String get accountAuthUsername => '邮箱或用户名';

  @override
  String get accountAuthPassword => '密码';

  @override
  String get accountAuthLogin => '登录';

  @override
  String get accountAuthImportLink => '从 authlib 链接导入';

  @override
  String get accountAuthImportFailed => '剪贴板里没有 authlib 服务器链接。';

  @override
  String get platformSearchHint => '搜索整合包';

  @override
  String get platformLoadFailed => '无法加载整合包。';

  @override
  String get platformEmpty => '没有找到整合包。';

  @override
  String get platformEmptyHint => '试试其它关键词，或切换平台。';

  @override
  String get platformVersionsEmpty => '没有可安装的版本。';

  @override
  String get platformInstall => '安装';

  @override
  String get platformInstallUnsupported => '安装器暂不支持该平台整合包格式。';

  @override
  String get platformInstallMetadataFailed => '平台返回的整合包元数据不完整，请稍后重试或选择其他版本。';

  @override
  String get platformInstallNetworkFailed => '无法连接平台或其下载主机，请稍后重试或改用其他整合包/来源。';

  @override
  String get platformDirectArchiveTitle => '该整合包没有官方哈希，仍要安装吗？';

  @override
  String get platformDirectArchiveWarning =>
      '该平台只提供直链 zip，不发布 MD5/SHA 摘要。安装器将使用 HTTPS 下载、逐条校验 ZIP CRC，并记录下载后的 SHA-256。是否继续？';

  @override
  String get platformDirectArchiveConfirm => '继续安装';

  @override
  String get platformDirectArchiveBadge => '直链 zip · 无官方哈希';

  @override
  String get accountAuthTermsRequired => '请先同意第三方服务条款。';

  @override
  String get accountAuthServerInvalid => '请输入有效的 https 服务器地址。';

  @override
  String get accountAuthInvalidCredentials => '服务器拒绝了用户名或密码。';

  @override
  String get accountAuthNoProfile => '该账户在服务器上没有 Minecraft 角色。';

  @override
  String get accountAuthNetwork => '无法连接认证服务器。';

  @override
  String get accountAuthDownloadFailed => '无法下载 authlib-injector，请重试。';

  @override
  String get accountAuthTermsOpenFailed => '无法打开服务条款页面。';

  @override
  String get elevationTitle => '我知道你想干什么';

  @override
  String elevationBody(String kind) {
    return 'ArchoeraCX 检测到当前进程正以$kind权限运行。以超级管理员身份启动，实例、Mod 与游戏文件都会归该账户所有，下载或安装的恶意内容也会拥有更高的破坏力。\n\n启动器和 Minecraft 都不需要管理员权限。请用普通账户重新启动；只有在你清楚后果时才继续。';
  }

  @override
  String get elevationContinue => '我知道我在干什么';

  @override
  String get elevationExit => '退出';

  @override
  String get elevationKindRoot => 'root';

  @override
  String get elevationKindAdministrator => '管理员';
}
