# AGENTS.md — ArchoeraCX 维护指引

面向在本仓库工作的 AI 助手与维护者。当前阶段：**Phase 1，Flutter UI + C++/C# 模块注册表 + 实例/下载/账户垂直切片**。

## 工作范围

- 只处理本仓库；不要修改仓库之外的目录。
- 不要复制 LauncherX / HMCL / Prism / Modrinth 的代码、XAML、图标、插画、文案或字体。
- 不接受外部代码贡献的仓库政策见 `CONTRIBUTING.md`。

## 目录

```text
app/                 # 单应用根
├─ lib/
│  ├─ app/           # app / router / shell
│  ├─ pages/         # 页面
│  ├─ services/      # CoreClient / 事件 / 平台服务
│  ├─ stores/        # Riverpod providers
│  ├─ theme/         # tokens / app_theme
│  ├─ widgets/       # common / layout
│  ├─ utils/
│  └─ l10n/          # ARB + generated/
├─ core/             # C++ archoera_core
├─ modules/          # C# / Rust（下一步）
├─ native/           # 平台桥接（下一步）
├─ contracts/        # ABI / schema
├─ assets/
├─ tool/             # build-core.sh / build-core.bat
└─ test/
docs/                # 架构 / UI / 预研 / 发行
Reference/           # 只读参考，不参与构建
```

## 提交前必跑

```bash
# 原生核心 + C# 模块
cd app
./tool/build-core.sh
./tool/build-cs.sh
./tool/build-dl.sh
./tool/build-vault.sh
./build/native/abi_smoke
./build/native/async_smoke
./build/native/module_smoke
dotnet test modules/business/Archoera.Tests/Archoera.Tests.csproj -c Release
dotnet run --project modules/business/Archoera.DevHost -c Release -- --check
dotnet run --project modules/business/Archoera.DevHost -c Release -- --self-test-loader
cargo test --manifest-path modules/downloader/Cargo.toml

# Flutter
flutter pub get
flutter gen-l10n
dart analyze          # 中文路径下不要用 flutter analyze
flutter test
ARCHOERACX_NETWORK_TESTS=1 flutter test test/resource_network_test.dart
ARCHOERACX_SETTINGS_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-settings flutter test test/settings_integration_test.dart
ARCHOERACX_INSTANCE_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-instance flutter test test/instance_integration_test.dart
ARCHOERACX_ACCOUNT_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-account flutter test test/account_integration_test.dart
flutter build linux --release
```

## 架构红线

- Dart 只通过 `app/lib/services/core/CoreClient` 调 C++ 枢纽；**不直连业务 API**。
- UI 不读文件、不起进程；图片加载是唯一例外。
- C++ 枢纽不做 Mojang/Modrinth/加载器业务。
- C# 是 API、业务、SQLite 的唯一 owner。
- Rust 只消费 `DownloadPlan`：不解压、不写 SQLite、不理解业务语义。
- SQLite 只由 C# 持有；Flutter 不引入数据库插件、不直接读 SQLite；C++ 只路由。Linux/macOS 使用系统 SQLite，Windows 使用随 C# 模块分发的 `e_sqlite3.dll`。
- 跨模块协作走效果协议，不直接互调。
- 核事件必须 **wake 驱动**：C++ 调 `wake`，Dart `NativeCallable.listener` 收到后批量 drain；**禁止 `Timer.periodic` 轮询**。
- 所有跨边界数据都有 schema、owner、生命周期和失败路径。
- 新方法必须同时登记到 C++ `method_registry` 与 C# `BusinessModuleRegistry`；未知方法返回 `E_UNSUPPORTED`，不允许在导出层加 if 分支。
- Microsoft 登录默认 Device Code Flow；device code / access token / refresh token 不跨 ABI，也不写日志。
- Microsoft client id **不硬编码**：CI/发布构建用 `-p:MicrosoftClientId=<GUID>` 注入编译期默认值；本地调试用 `ARCHOERACX_MICROSOFT_CLIENT_ID` 运行时临时覆盖（运行时优先）。

## 子代理协作

- 按边界拆任务：C++/CMake、Dart/Flutter、文档/契约可以并行。
- 不要让两个代理同时改同一个文件；共享契约先由主代理冻结。
- 子代理完成后必须由主代理统一跑构建/测试，不直接相信报告。
- 涉及 ABI 的改动必须同时改 `app/contracts/abi/archoera_abi.h`、C++、Dart 和 C harness。

## 当前进度

- ✅ Flutter AppShell + 5 页面骨架 + 设计令牌 + i18n；全局文案走 ARB，支持跟随系统/English/简体中文切换；首页改为 Hero + 单行快捷条 + 最近实例网格，使用斜切角/直线面板；底栏降至 60（首页 52）且首页隐藏启动按钮、首页相关切换自下而上覆盖旧页；账户页改为当前账户 Hero + 单列表，并支持 2D 纸娃娃皮肤预览与离线默认外观；全局通知改为标题栏下方斜切角 Toast，设置页保留分区导航 + 平铺设置组；加载器图标全部使用素材资产（原版为 Wikimedia 草方块，Fabric/Quilt/Forge/NeoForge 为官方图标），来源/许可证见 `assets/icons/loaders/SOURCES.md`
- ✅ `system.ping` / `system.pingAsync`：C++ ABI / C harness / Dart `CoreClient` / integration test；首页不再暴露调试卡片
- ✅ 事件泵：wake + `NativeCallable.listener` + 批量 drain；无定时器/轮询
- ✅ C# `java.scan`：C++ ModuleRegistry + NativeAOT `archoera_cs` + Java status 卡；支持手动目录/可执行文件回退与持久化
- ✅ Forge/NeoForge 生成物清单 + 校验 + 修复；`loader.verify`/`loader.repair` 已接 ModuleRegistry/CoreClient（待接 launch preflight/UI）
- ✅ Rust `DownloadPlan` 执行器 MVP（待接 C++ ModuleRegistry）
- ✅ C# `DownloadPlan` 契约 + Modrinth provider（Mod/光影/资源包；待接 ModuleRegistry/UI）
- ✅ Mojang 原版 `DownloadPlan`（manifest/version/assets/libraries/logging；待接 ModuleRegistry/UI）
- ✅ Forge/NeoForge installer + 依赖库 `DownloadPlan`（生成物不进计划；待接安装流程/UI）
- ✅ 版本合并 / `EffectiveVersion`（inheritsFrom、库去重、参数拼接、legacy 参数）
- ✅ Forge/NeoForge 安装下载组合计划（原版 + 加载器依赖 + installer）
- ✅ Mojang Java runtime 下载计划（all.json → platform manifest → raw/executable）
- ✅ C++ `ModuleRegistry` + `MethodRegistry`；`system.modules` 可查询已加载模块；C# `BusinessModuleRegistry` 统一注册业务方法
- ✅ Microsoft Device Code 登录：Microsoft token → XBL → XSTS → Minecraft login → profile/certificates；账户页可登录/切换/刷新/移除，离线账户可用
- ✅ 实例 UI：创建/选择/删除/搜索、可选实例目录；C# SQLite 索引 + `instance.json` 权威源
- ✅ 下载页 UI：6 个分栏；Modrinth 搜索/版本/安装已接线，CurseForge 源待 key
- ✅ C# `settings.get/set`：多实例根目录（`instancesRoots`，首项默认）；`instance.sync` 采纳/刷新/标记缺失；Flutter 设置页与创建实例对话框已接入
- ✅ C# SQLite 实例索引 + `instance.json` 权威源；Flutter 通过 `instance.*` 模块方法读写
- ✅ Modrinth 端到端：Flutter 搜索/版本/安装 → C# 计划 → Rust `archoera-dl` 下载并校验（Sodium 1.21.1 Fabric 实测）
- ✅ Noto Sans SC Medium 打包为默认 UI 字体
- ✅ Java 扫描按 `java.home` 归并 symlink/JRE 别名，扩展用户目录，UI 显示前 5 + 查看全部
- ✅ `version.list` / `loader.check` / `loader.plan` / `loader.commit`；下载页版本列表与全局 Mod 搜索；实例“更多”中安装/更新 Forge/NeoForge 加载器、管理已安装 Mod（启停/删除），支持 quick/full 依赖检查
- ✅ `settings.get/set` 持久化 `javaPath` / `javaAuto` / `memoryMinMb` / `memoryMaxMb` / `jvmArgs` / `gcMode` / `gameArgs`；设置页支持自动/手动 Java、内存、GC、JVM 参数与附加游戏参数；`launch.start` 支持基础离线账户启动（自动选择匹配游戏版本的 Java，持久化日志）
- ✅ Modrinth 模组安装支持递归安装 required 依赖（深度 8、环检测），optional/incompatible 只报告不自动安装
- ✅ Forge/NeoForge 安装前通过 `java.select` 选择 Java，下载后调用 `loader.repair` 执行 processors；仍需 JVM（自动选择或手动指定）
- ⚠️ Fabric/Quilt 无 Java 安装待接
- ⬜ 真实 Entra client_id + Minecraft Services client_id 审核；构建期注入 `-p:MicrosoftClientId`，调试期用 `ARCHOERACX_MICROSOFT_CLIENT_ID`
- ✅ 凭据保险库：`core/vault-rs`（ArchoeraMusic 同系列 AGPL 实现）已接入 C# `VaultCredentialStore`；仅暴露 crypto/file 免密模式，旧 `accounts.credentials.json` 首次使用时迁移并删除
- ⬜ CurseForge provider（鉴权后置）
- ⬜ 正式 C++ DownloadManager / 安装原版；`launch.start` 目前只支持离线账户，进程状态/停止/日志流待接
