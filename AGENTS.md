# AGENTS.md — ArchoeraCX

面向在本仓库工作的 AI 助手与维护者。当前阶段：**Phase 1**（Flutter UI + C++ 枢纽 + C# 模块注册表 + 实例/下载/账户/整合包垂直切片）。

## 先读

- [`docs/12-build-and-test.md`](docs/12-build-and-test.md)：环境、逐模块构建、测试矩阵、数据目录
- [`docs/10-architecture-design.md`](docs/10-architecture-design.md)：ABI、模块协议、任务/事件、存储、安全
- [`docs/11-ui-design-system.md`](docs/11-ui-design-system.md)：设计令牌、AppShell、页面与动效
- [`docs/09-distribution-and-download-policy.md`](docs/09-distribution-and-download-policy.md)：发行地区门控、Provider、下载策略
- [`docs/README.md`](docs/README.md)：文档索引；冲突时以代码与契约为准

## 边界

- 只改本仓库；不要动仓库之外的目录。本地草稿、样本与反编译产物放 `Reference/local/`（忽略，不提交）。
- `.gitignore` 规则要收窄：不要用 `**/bin/`、`**/build/` 这类宽规则，它会把 Rust `src/bin/` 之类的源码一起隐藏。
- 不复制 LauncherX / HMCL / Prism / Modrinth 的代码、XAML、图标、插画、文案或字体；新资产必须登记来源与许可证。
- 不接受外部代码贡献，政策见 [`CONTRIBUTING.md`](CONTRIBUTING.md)。
- 代码、注释、提交信息用英文；面向用户的文档与 UI 文案用中文，UI 文案走 ARB（`en` / `zh`）。

## 目录

```text
app/
├─ lib/                  # Flutter：app/、pages/、stores/、services/core/、widgets/、theme/、l10n/
├─ core/                 # C++20 枢纽 archoera_core；core/vault-rs 为凭据保险库
├─ modules/business/     # C# NativeAOT archoera_cs（API / 业务 / SQLite 唯一 owner）
├─ modules/downloader/   # Rust archoera_dl（只执行 DownloadPlan）
├─ contracts/            # ABI 与模块 schema
├─ native/               # 平台桥接（占位）
├─ tool/                 # build-core / build-cs / build-dl / build-vault
└─ test/                 # Flutter 测试
docs/                    # 架构 / 构建 / 发行 / UI 文档
Reference/               # 只读参考，不参与构建
```

## 架构红线

- Dart 只经 `app/lib/services/core/CoreClient` 调 C++；UI 不读文件、不起进程、不直连业务 API（图片加载例外）。
- C++ 只做生命周期、路由、任务与事件，不写 Mojang/Modrinth/加载器业务。
- C# 是 API、业务、SQLite 的唯一 owner；SQLite 只由 C# 持有，Flutter 不引入数据库插件。
- Rust 只消费 `DownloadPlan`：不解压、不写 SQLite、不理解业务语义。
- `.mrpack` 由 C# 解析与安全解压（zip-slip、symlink、size/entry 上限）；Rust 仍只执行计划。
- 例外：Forge/NeoForge 官方 Maven 不可达时，C# 仅对 installer 做 BMCLAPI 镜像引导下载并计算 SHA-1；库与游戏文件仍由 Rust 下载。
- 核事件必须 **wake 驱动**：C++ 调 `wake`，Dart `NativeCallable.listener` 收到后批量 drain；**禁止 `Timer.periodic` 轮询**。
- 新方法必须同时登记到 C++ `method_registry` 与 C# `BusinessModuleRegistry`；未知方法返回 `E_UNSUPPORTED`，导出层不加 if 分支。
- 跨模块协作走效果协议，不直接互调；所有跨边界数据都有 schema、owner、生命周期与失败路径。
- token / refresh token / device code 不跨 ABI、不写日志；Microsoft client id 不硬编码（CI 注入 `-p:MicrosoftClientId`，调试用 `ARCHOERACX_MICROSOFT_CLIENT_ID`，运行时优先）。

## 提交前必跑

```bash
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

flutter pub get
flutter gen-l10n
dart analyze                 # 中文路径下不要用 flutter analyze
flutter test
ARCHOERACX_NETWORK_TESTS=1 flutter test test/resource_network_test.dart
ARCHOERACX_SETTINGS_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-settings flutter test test/settings_integration_test.dart
ARCHOERACX_INSTANCE_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-instance flutter test test/instance_integration_test.dart
ARCHOERACX_ACCOUNT_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-account flutter test test/account_integration_test.dart
ARCHOERACX_WIKI_TESTS=1 flutter test test/wiki_integration_test.dart
flutter build linux --release
```

## 协作约定

- ABI 改动必须同时更新 `app/contracts/abi/archoera_abi.h`、C++、Dart、C harness 与相关测试。
- 按边界拆子代理：C++/CMake、Dart/Flutter、文档/契约可并行；不要让两个代理改同一文件，共享契约先由主代理冻结。
- 子代理的报告不等于验证：完成后由主代理统一跑构建与测试。
- 不臆造进度或测试结果；没有实际运行就写“未验证”。

## 版本与身份

- 版本唯一来源是 `app/pubspec.yaml` 的 `version`（当前 `0.0.1+1`），并与 `app/lib/utils/app_info.dart` 的 `AppInfo.version` 保持一致。
- 所有对外版本号都跟随它：C++ `build_info`/`system.modules`、C# 模块表与 User-Agent、启动参数 `launcher_version`、Rust crate（`archoera-dl` / `archoera-vault`）。改版本时一并更新这些常量。
- 应用 ID：Linux/Android `awa.archoera.archoera_cx`，macOS `awa.archoera.archoeraCx`（Apple 不允许下划线）。
- 身份与品牌细则见 [`app/README.md`](app/README.md)、[`docs/04-licensing.md`](docs/04-licensing.md)。

## 当前进度

已完成：

- 界面：AppShell + 6 页面（主页/实例/下载/账户/Wiki/设置）、设计令牌、明暗主题、中英文 i18n、斜切角面板/Toast/页面转场、账户皮肤预览；右上角任务中心为右侧滑入边栏，展示真实进度/速度与具体目标，只记录长任务；实例菜单含游戏日志查看器（`launch.logs` / `launch.log.read` / `launch.log.tail`，运行中经 ABI 进度回调实时跟随，UI 不读文件；每实例保留最近 10 次、≤32 MiB，单文件 64 MiB 截断）。
- Wiki 页（底栏第 5 项，`/wiki`）：C++ `system.status` 用平台 API 采样 CPU/核数/负载、内存、实例根所在磁盘、启动器进程与系统运行时长，`system.status.watch` 用条件变量每 5 秒推送域事件（Dart 只 drain，不轮询）；`wiki.feed` 汇总 Mojang 官方新闻、Java 补丁说明与 Minecraft Wiki 最近编辑（逐源独立失败 + 5 分钟缓存）。负载由核心推送更新，社区动态只在用户刷新或进入页面时拉取，Dart 不做定时轮询。
- Java 选择：设置页 Java 分区只保留自动/手动与内存参数，并提供 `/settings/java` 子页入口；子页平铺全部扫描结果（版本徽标、使用中标记、搜索、自定义目录/可执行文件），不再是“显示前 5 + 查看全部”弹窗。
- 外观设置：C++ `system.appearance`（XDG Portal / Windows 注册表 / AppKit）推送系统深浅色与强调色；“外观”改为单列分组平铺（主题 / 主题色 / 界面 / 语言与区域，无二级页签，行外不套卡片），含主题模式、系统/自定义主题色、全局着色、界面密度、减少动效与重置；关于页含环境信息复制、源码/反馈、许可证/隐私/安全入口。
- 系统集成：C++ `system.openUrl` / `system.notify` 用系统 API 打开授权浏览器并发送登录成功桌面通知（Linux D-Bus / Windows Shell API / macOS AppKit+UserNotifications）。
- 核心：C++ Method/Module 注册表、wake 事件泵、C ABI v2 进度回调；C# NativeAOT 模块与 DevHost。
- 账户：Microsoft Authorization Code + PKCE（loopback）→ XBL/XSTS → Minecraft；凭据保险库（crypto/file）；离线账户；在线启动。
- 实例与设置：SQLite 索引 + `instance.json` 权威源；新建实例默认版本隔离（`versions/<name>`，整合包安装同样默认隔离；显式 `rootPath` 仍可覆盖）；Java 扫描/选择、内存/GC/JVM/游戏参数持久化。
- 下载：Mojang 原版与 Java runtime、Modrinth（Mod/光影/资源包）、Forge/NeoForge 依赖与 installer 计划；Rust 执行器含 hash、原子落盘、进度与取消。
- 加载器与整合包：Fabric/Quilt/Forge/NeoForge 安装与依赖检查/补全；Forge/NeoForge processor 修复；Modrinth 与本地 `.mrpack` 整合包安装。`loader.versions` 按“自动源”处理：中国大陆网络镜像优先（Fabric fabric-meta、Forge per-game JSON、NeoForge Maven 镜像），其他地区官方优先、失败回退镜像；结果缓存 10 分钟。
- 启动依赖预检 `launch.prepare` / `launch.start`：启动前快速扫描全部库（含原生 classifier）、client jar、asset index 与逐条 asset object；先本地修复（缺失/未合并 profile 从 `versions/` 本地 profile 合并，Forge/NeoForge 复用已有 installer 跑 processors），本地修复失败才自动下载补全（下载策略关闭返回 `AUTO_REPAIR_BLOCKED`）。
- 加载器实例启动：优先使用实例目录自己的版本 JSON，再回退到游戏版本对应的加载器 profile；自定义整合包名（如 `AutumnTown`、`Radiance`）不再报 `VERSION_JSON_MISSING` 或误用其它实例的 profile。NeoForge 的 client/universal 生成物按 Maven 约定预检，齐全时直接启动，不因实例缺少 installer 触发下载修复。

待接：

- 正式 C++ DownloadManager、原版安装入口；游戏进程状态与停止（`launch.status` / `launch.stop`）。
- 真实 Entra / Minecraft Services client_id 审核；更新器、签名与安装包。
