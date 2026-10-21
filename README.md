# ArchoeraCX

跨平台 Minecraft: Java Edition 启动器。Flutter 负责 UI，C++ 做唯一枢纽，C# 负责业务/API/SQLite，Rust 负责下载执行。

> **开发版 / Development build**  
> 本项目目前仅通过 GitHub 发布开发版。功能不完整，可能损坏实例或配置数据；**请勿用于正式环境或重要实例**。不提供稳定性、兼容性或数据安全保证。

> 当前为 GitHub-only alpha：已完成实例管理、原版文件安装、微软登录、权益校验、离线/在线启动、Modrinth 搜索/下载与加载器安装；整合包、Fabric/Quilt 无 Java 安装和 CurseForge 尚未完成。

## 当前能力

- Flutter AppShell、5 个页面、明暗主题、中英文 i18n
- `system.ping` / `system.pingAsync` / `system.info` / `system.modules`
- C++ `ModuleRegistry` + `MethodRegistry`，wake 驱动事件泵，无定时轮询
- C# NativeAOT `archoera_cs`：方法注册表、Java 扫描、Forge/NeoForge 生成物校验/修复
- 实例：创建/选择/删除/搜索、自定义目录、SQLite 索引 + `instance.json` 权威元数据
- 设置：实例根目录持久化（C# 持有）
- Modrinth：搜索、版本选择、`DownloadPlan`、Rust 下载与 hash 校验
- 账户：Microsoft Authorization Code + PKCE（loopback）→ XBL → XSTS → Minecraft → entitlement/profile/certificates；离线/在线启动
- Rust `archoera_dl`：`DownloadPlan` 执行器 MVP（HTTP、hash、原子落盘、取消）
- Linux release bundle 自带 `libarchoera_core.so`、`archoera_cs.so` 和 `archoera-dl`

当前基线：295 个 C# 测试、37 个 Flutter 测试、21 个 Rust 测试通过（Linux x64）。

## 架构

```text
Flutter UI (Dart)
  │  CoreClient / C ABI
  ▼
archoera_core (C++20)
  │  ModuleRegistry + MethodRegistry + Task/Event/ModuleHost
  ├──────────────┐
  ▼              ▼
archoera_cs     archoera_dl        （Rust 模块后续接入 C++ 枢纽）
C# NativeAOT    Rust
业务/API/SQLite  DownloadPlan 执行
```

红线：

- Dart 不直连业务 API；图片加载是唯一例外。
- C++ 只做生命周期、路由、任务和事件，不写业务规则。
- C# 是 API、业务、SQLite 的唯一 owner。
- Rust 只消费 `DownloadPlan`，不解压、不写 SQLite。
- token 不跨 C ABI、不写日志；refresh token/Minecraft token 存在本地加密凭据保险库中。

## 环境

- Flutter 3.47.7 / Dart 3.13.5
- .NET 10 SDK
- Rust stable
- CMake 3.16+、C++20 编译器
- Linux 需要 GTK3 开发包；macOS 目标为 Apple Silicon arm64

## 构建

```bash
cd app

./tool/build-core.sh    # C++ 核心
./tool/build-cs.sh      # C# NativeAOT 模块
./tool/build-dl.sh      # Rust DownloadPlan 执行器 + CLI

flutter pub get
flutter gen-l10n
dart analyze            # 中文路径下不要用 flutter analyze
flutter test
flutter build linux --release
```

Microsoft 登录的 client ID 必须在这个 C# 构建步骤注入：

```bash
export ARCHOERACX_MICROSOFT_CLIENT_ID="<Application (client) ID>"
./tool/build-cs.sh
```

离线启动在 C# 端强制要求已验证的 Microsoft 凭据，没有公开的跳过开关。

CI/Release 构建从 GitHub Actions secret `ARCHOERACX_MICROSOFT_CLIENT_ID` 注入；
本地构建未设置该变量时，生成的 client ID 为空，登录会返回
`MICROSOFT_CLIENT_ID_MISSING`，而不是发出网络请求。

Authorization Code + loopback 登录还需要在 Entra 中注册精确回调：

```text
http://localhost:53682/callback
```

端口可用 `ARCHOERACX_OAUTH_PORT` 覆盖，并同步更新 Entra 回调地址。


启动 release bundle：

```bash
./build/linux/x64/release/bundle/archoera_cx
```

## Microsoft 登录与 Minecraft 认证

Entra Application (client) ID **不硬编码、不进仓库**。

发布/CI 构建注入编译期默认值：

```bash
ARCHOERACX_MICROSOFT_CLIENT_ID="<Application (client) ID>" ./tool/build-cs.sh
```

本地调试时构建保持空值，启动时临时覆盖：

```bash
ARCHOERACX_MICROSOFT_CLIENT_ID="<Application (client) ID>" \
  ./build/linux/x64/release/bundle/archoera_cx
```

运行时环境变量优先于编译期默认值。详细流程、错误码和审核要求见
[`docs/authentication.md`](docs/authentication.md)。

> **关键门槛**：Entra 应用注册、OAuth、XBL、XSTS 全部成功，也不等于 Minecraft 登录成功。
> `login_with_xbox` 使用的 client_id 还必须通过 Minecraft Services 的应用审核
> （`https://aka.ms/mce-reviewappid`）。未审核时返回 `MINECRAFT_APP_NOT_APPROVED`。
> 不要复用其他启动器的 client_id 绕过审核。

## 隐私与贡献

- [隐私政策](PRIVACY.md)（本地账户、令牌、皮肤缓存、未成年人说明与删除方式）
- [第三方许可清单](THIRD-PARTY-NOTICES.md)（Flutter/Dart、.NET、Rust、字体与图标）
- 当前暂不接受外部代码贡献；仅接受指定开发者及经邀请的必要安全/合规修复，详见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 数据与安全

默认数据目录：

```text
Linux:   ~/.local/share/archoera-cx
macOS:   ~/Library/Application Support/ArchoeraCX
Windows: %APPDATA%/ArchoeraCX
```

可用 `ARCHOERACX_DATA_DIR` 覆盖。主要文件：

```text
settings.json                 实例根目录等设置
archoera.db                   C# SQLite 实例索引
<instance>/.archoera/instance.json
accounts.json                 账户元数据
archoera-vault                凭证保险库（crypto/file 免密模式）
logs/account.log              脱敏的账户诊断日志
```

- Flutter 不打开 SQLite；Linux/macOS 用系统 SQLite，Windows 随 C# 模块分发 `e_sqlite3.dll`。
- 账户 token 只在 C# 使用；UI 只拿账户元数据。
- 失败登录不会写入账户或 token。
- OS 凭据库（DPAPI/Keychain/libsecret）尚未接入。

## 测试

```bash
cd app
./build/native/abi_smoke
./build/native/async_smoke
./build/native/module_smoke

dotnet test modules/business/Archoera.Tests/Archoera.Tests.csproj -c Release
dotnet run --project modules/business/Archoera.DevHost -c Release -- --check
dotnet run --project modules/business/Archoera.DevHost -c Release -- --self-test-loader
cargo test --manifest-path modules/downloader/Cargo.toml

flutter test
```

可选集成测试：

```bash
ARCHOERACX_SETTINGS_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-settings \
  flutter test test/settings_integration_test.dart
ARCHOERACX_INSTANCE_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-instance \
  flutter test test/instance_integration_test.dart
ARCHOERACX_ACCOUNT_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-account \
  flutter test test/account_integration_test.dart
ARCHOERACX_NETWORK_TESTS=1 flutter test test/resource_network_test.dart
ARCHOERACX_MICROSOFT_START_TESTS=1 \
ARCHOERACX_MICROSOFT_CLIENT_ID="<client id>" \
  flutter test test/microsoft_start_test.dart
```

## 文档

- [文档索引](docs/README.md)
- [详细架构设计](docs/10-architecture-design.md)
- [Microsoft / Minecraft 认证](docs/authentication.md)
- [UI 设计系统](docs/11-ui-design-system.md)
- [发行与下载策略](docs/09-distribution-and-download-policy.md)
- [许可证与资产边界](docs/04-licensing.md)
- [路线图](docs/05-roadmap.md)
- [技术预研](docs/08-technical-research/README.md)

## 许可证与贡献

- 代码以 **AGPL-3.0-only** 发布，见 [LICENSE](LICENSE)。
- **不接受外部代码贡献**，原因见 [CONTRIBUTING.md](CONTRIBUTING.md)。
- 安全问题请走私密渠道，见 [SECURITY.md](SECURITY.md)。
- 可提交 Issue、崩溃日志、功能建议和文档纠错；请先脱敏。

## 免责声明

本项目与 Mojang Studios、Microsoft 无关联。

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.

启动器不分发游戏本体、资源文件或绕过正版验证的在线服务。
