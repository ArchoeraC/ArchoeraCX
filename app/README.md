# ArchoeraCX · Flutter UI

ArchoeraCX 的 Flutter 桌面外壳 + 原生切片。当前包含 UI 基础设施、`system.ping`、C++/C# 模块注册表、`java.scan`、Forge/NeoForge 生成物校验/修复库、Microsoft Device Code 登录，以及 Rust `DownloadPlan` 执行器 MVP；启动/正式 DownloadManager 尚未端到端接线。

设计规范见 [`docs/11-ui-design-system.md`](../docs/11-ui-design-system.md)。当前已实现：设计令牌、底部 CommandBar 导航的 AppShell、5 个页面、明暗主题、中英文、渐变背景与基础组件；以及：

- `system.ping` / `system.pingAsync`：C++ ABI + wake 驱动 `EventPump` + 首页 Core status 卡
- `system.modules`：C++ `ModuleRegistry` 返回已加载模块；C++ `MethodRegistry` 与 C# `BusinessModuleRegistry` 显式登记方法
- `system.openUrl` / `system.notify`：C++ 调用系统 API 打开默认浏览器、发送桌面通知；Microsoft 登录的授权页与登录成功提示均走该通道
- `system.appearance`：C++ 读取系统深浅色与强调色（Linux XDG Portal / Windows 注册表 / macOS AppKit），变化通过 domain 事件推送；设置页“外观”下分“个性化 / 语言与区域”子菜单，个性化包含浅色/深色/跟随系统、跟随系统或自定义主题色、全局着色、界面密度与减少动效
- 关于页：应用图标与开发版徽标、版本/系统/Dart/原生核心环境信息与一键复制、源码与问题反馈、许可证/第三方许可/隐私政策/安全策略入口
- `java.scan`：C++ `ModuleRegistry` 加载 C# NativeAOT `archoera_cs` + 首页 Java status 卡；默认展示前 3 个，支持“查看全部”（本机 `/opt/java` 72/72 全部识别）；扫描为空时可手动选择目录搜索，仍为空再选择 JDK 目录或 `java` 可执行文件；路径会持久化，并用 `includeDefaults:false` 验证
- Forge/NeoForge 生成物：`LoaderArtifactManifest` + 校验 + 按 processor 重跑修复；已通过 `loader.verify` / `loader.repair` 接入 C++ ModuleRegistry 和 Dart `CoreClient`，并有 Flutter 端到端测试（真实 installer 清单；尚未接 `launch.prepare`/UI）
- Rust `archoera_dl`：`DownloadPlan` 执行器 MVP（单流 HTTP、hash、原子落盘、进度/取消；尚未接 C++ ModuleRegistry）
- C# `DownloadPlan` 契约 + Modrinth provider：搜索/项目/版本、version 级兼容过滤、Mod/光影/资源包计划生成（尚未接 ModuleRegistry/UI）
- Mojang 原版计划：manifest/version JSON/asset index + client/libraries/natives/assets/logging 的 `DownloadPlan`（尚未接 ModuleRegistry/UI）
- Forge/NeoForge 依赖计划：installer `.sha1`/size 解析 + `install_profile.libraries`/`version.json.libraries` 的 `DownloadPlan`，生成物绝不进入下载计划（尚未接安装流程/UI）
- 版本合并 / `EffectiveVersion`：`inheritsFrom` 链、库去重与子层覆盖、JVM/game 参数拼接、legacy `minecraftArguments` 转换
- Forge/NeoForge 安装下载组合：原版 client/assets + 加载器库 + installer 合并为一个计划（尚未执行下载/启动）
- Mojang Java runtime 下载计划：`all.json` → 平台 manifest → raw 文件，保留 executable；LZMA-only 暂不支持
- 账户 UI：Microsoft Device Code 登录、设备码展示/复制/取消、切换/刷新/移除账户；离线账户
- Microsoft 登录链：Microsoft token → Xbox Live → XSTS → Minecraft login → profile/certificates；token 只留在 C#
- 实例 UI：创建/选择/删除实例、搜索过滤、可选实例目录；C# SQLite 实例索引 + `instance.json` 权威元数据（Flutter 不引入数据库）
- Modrinth 已接入 Flutter 下载页：真实搜索/版本选择/计划生成/下载（C# 临时桥接 Rust CLI；Sodium 1.21.1 Fabric 实测通过）

> 事件模型：C++ 任务状态变化时调用 `wake`，Dart `NativeCallable.listener` 收到后批量 drain `archoera_poll`；**没有定时器/轮询**。

## 身份

| 项 | 值 |
|---|---|
| 软件名 | `ArchoeraCX` |
| Dart 包名 | `archoera_cx` |
| 应用 ID（Linux / 未来 Android） | `awa.archoera.archoera_cx` |
| macOS Bundle ID | `awa.archoera.archoeraCx` |
| Windows 可执行文件 | `archoera_cx.exe` |
| 默认主题 | Material 3，浅色/深色/跟随系统；主题色跟随系统或自定义 |
| 语言 | `en` / `zh`（ARB） |

> macOS 的 Bundle ID 使用驼峰 `archoeraCx`：Apple 的 Bundle ID 只接受字母、数字、连字符和点，不能使用下划线；Linux/Android 仍使用 `archoera_cx`。

## 技术栈

- Flutter 3.47.7 / Dart 3.13.5
- `flutter_riverpod`：状态管理/依赖注入
- `go_router`：路由
- `flutter_localizations` + ARB：i18n
- Material 3：主题令牌从 `lib/theme/app_theme.dart` 开始

## 目录

```
lib/
├─ app/                  # App、bootstrap、router、shell
├─ pages/                # 页面（dashboard/instances/download/accounts/settings）
├─ services/             # CoreClient、事件泵、平台服务
├─ stores/               # Riverpod providers / prefs
├─ theme/                # tokens / app_theme
├─ widgets/              # common / layout
├─ utils/                # 无状态工具
├─ l10n/                 # ARB + generated/
└─ main.dart
test/
├─ app_test.dart
├─ core_client_integration_test.dart
└─ core_status_card_test.dart
tool/
└─ build-core.sh / build-core.bat
core/          # C++ archoera_core + ModuleRegistry/MethodRegistry
contracts/     # ABI / module ABI
modules/       # C# archoera_cs + Rust archoera_dl（下载执行器 MVP）
native/        # 平台桥接（下一步）
```

UI 规则见 [`docs/10-architecture-design.md`](../docs/10-architecture-design.md) §10：

- Widget 不直接读文件、起进程、连业务 API；
- 业务调用统一走 `CoreClient`；
- 图片加载是唯一例外；
- 平台能力（托盘/单实例/文件关联/更新）放 C++，不放 Dart 插件。

## 常用命令

```bash
# 1. 编译原生核心与 C# 模块
./tool/build-core.sh      # Windows 用 tool\build-core.bat
./tool/build-cs.sh        # Windows 用 tool\build-cs.bat

# 2. Flutter
flutter pub get
flutter gen-l10n          # 修改 lib/l10n/*.arb 后执行
dart analyze
flutter test
flutter run -d linux      # 或 windows / macos
flutter build linux --release
# Linux release bundle 会带上 libarchoera_core.so、archoera_cs.so 和 archoera-dl
```

单独验证 C# 模块：

```bash
dotnet test modules/business/Archoera.Tests/Archoera.Tests.csproj -c Release
dotnet run --project modules/business/Archoera.DevHost -c Release -- --check
dotnet run --project modules/business/Archoera.DevHost -c Release -- --scan
dotnet run --project modules/business/Archoera.DevHost -c Release -- --self-test-loader
# 需要用真实 installer 复核清单时：先把样本放到 <repo>/.local/forge-samples/
./tool/verify-loader-samples.sh
```

Rust 下载器：

```bash
cargo test --manifest-path modules/downloader/Cargo.toml
cargo build --release --manifest-path modules/downloader/Cargo.toml

# Modrinth 端到端试跑（无需 API key）
./tool/try-modrinth.sh sodium 1.21.1 fabric
```

> 当前仓库路径含中文时，`flutter analyze` 的 analysis server 可能异常退出；用 `dart analyze` 代替，结果等价。

macOS 只支持 Apple Silicon（arm64），不构建 x64/universal。

## 账户配置

Microsoft 登录需要自己的 Entra 公共客户端 ID。**不要把它硬编码进源码**：

```bash
# CI / 发布构建：注入编译期默认值
ARCHOERACX_MICROSOFT_CLIENT_ID="<your-public-client-id>" ./tool/build-cs.sh

# 本地调试：构建保持空值，启动时临时覆盖（运行时优先）
ARCHOERACX_MICROSOFT_CLIENT_ID="<your-public-client-id>" \
  ./build/linux/x64/release/bundle/archoera_cx
```

等价的 MSBuild 参数是：

```bash
./tool/build-cs.sh -p:MicrosoftClientId="<your-public-client-id>"
```

（`build-cs.sh` 会把 `ARCHOERACX_MICROSOFT_CLIENT_ID` 传给原生构建。）device code 流程不需要 redirect URI。即使 OAuth/XBL/XSTS 成功，`login_with_xbox` 仍需 Minecraft Services 对该 client_id 的审核；未审核时 UI 会显示 `MINECRAFT_APP_NOT_APPROVED`。refresh token 存 `<dataRoot>` 下的 `archoera-vault` 凭证保险库（crypto/file 免密模式）；旧 `accounts.credentials.json` 首次使用时迁移并删除。

## 下一步

按 `docs/10-architecture-design.md` §20.2 继续：

1. ✅ `contracts/abi/archoera_abi.h` + C++ `archoera_core` + `system.ping`
2. ✅ Dart `CoreClient` 加载 `archoera_core`，首页 Core status 卡可探测
3. ✅ C# `java.scan`：ModuleRegistry + NativeAOT `archoera_cs` + Java status 卡
4. Forge/NeoForge 生成物清单 + 修复库（已落地，待接 launch preflight）
5. ✅ Rust `DownloadPlan` 执行器（MVP 已落地，待接 C++ ModuleRegistry）
6. ✅ Microsoft Device Code 登录链 + 账户页；待真实 client_id 审核与 OS 凭据库
7. 下一步：C# 下载源/计划构造 + 安装/启动

## 许可证

AGPL-3.0-only；见仓库根目录 [`LICENSE`](../LICENSE)。

界面图标字体 `assets/fonts/AppIcons.ttf` 是 [Tabler Icons](https://github.com/tabler/tabler-icons) 3.49.0 的裁剪子集，单独遵循 **MIT**（© 2020-2026 Paweł Kuna）；正文随应用分发于 `assets/licenses/Tabler-Icons-MIT.txt`，并通过 `lib/app/licenses.dart` 注册到 Flutter 的第三方许可页面。仓库只提交打包好的子集字体，不提交上游图标集；来源与致谢见 [`assets/icons/ui/SOURCES.md`](assets/icons/ui/SOURCES.md)。
