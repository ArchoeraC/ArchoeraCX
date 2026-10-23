# 构建、运行与测试

本文是本地从源码构建 ArchoeraCX、运行 release bundle 与执行测试的完整手册。
架构与模块边界见 [`10-architecture-design.md`](10-architecture-design.md)；登录流程与错误码见 [`authentication.md`](authentication.md)。

## 1. 环境

| 依赖 | 版本 / 说明 |
|---|---|
| Flutter | 3.47.7（Dart 3.13.5） |
| .NET SDK | 10 |
| Rust | stable |
| CMake | 3.16+ |
| C++ | C++20 编译器 |
| Linux | GTK3 开发包 |
| macOS | 目标为 Apple Silicon arm64 |

## 2. 构建模块

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
python3 tool/check_icons.py   # 校验 AppIcons 与自带图标字体一致
```

`app/shaders/*.frag` 由 `pubspec.yaml` 的 `flutter: shaders:` 声明交给 `impellerc` 编译；`flutter test` 同样会编译，`test/app_shaders_test.dart` 按各着色器声明的 uniform 个数逐个绘制，索引与 GLSL 不一致会在此失败。新增或调整 uniform 时必须同步修改调用方的 `setFloat` 索引。窗口背景是纯色、没有持续动画，测试里的 `pumpAndSettle` 可正常收敛。

Microsoft 登录的 client ID 必须在 C# 构建步骤注入：

```bash
export ARCHOERACX_MICROSOFT_CLIENT_ID="<Application (client) ID>"
./tool/build-cs.sh
```

- CI/Release 构建从 GitHub Actions secret `ARCHOERACX_MICROSOFT_CLIENT_ID` 注入。
- 构建期只把 client ID 的**高熵 blob**（随机 nonce + SHA-256 keystream 异或）写进 `obj/.../MicrosoftBuildConfig.g.cs`，运行时在内存里还原；明文不落仓库、不出现在二进制字符串表。
- 本地构建未设置该变量时 client ID 为空，登录返回 `MICROSOFT_CLIENT_ID_MISSING`，不会发出网络请求。
- 运行时环境变量优先于编译期默认值。
- Authorization Code + loopback 登录需要在 Entra 注册精确回调 `http://localhost:53682/callback`；端口可用 `ARCHOERACX_OAUTH_PORT` 覆盖，并同步更新 Entra 回调地址。
- 离线启动在 C# 端强制要求已验证的 Microsoft 凭据，没有公开的跳过开关。

## 3. 运行

```bash
./build/linux/x64/release/bundle/archoera_cx
```

本地调试时可在启动阶段临时覆盖 client ID：

```bash
ARCHOERACX_MICROSOFT_CLIENT_ID="<Application (client) ID>" \
  ./build/linux/x64/release/bundle/archoera_cx
```

Linux release bundle 自带 `libarchoera_core.so`、`archoera_cs.so` 与 `archoera-dl`。

## 4. 测试

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

可选集成测试（需要真实环境变量与临时数据目录）：

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

## 5. 数据目录与本地文件

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

- Flutter 不打开 SQLite；所有平台都随 C# 模块分发 SQLitePCLRaw 的 `e_sqlite3`（Linux `libe_sqlite3.so`、macOS `libe_sqlite3.dylib`、Windows `e_sqlite3.dll`），由 C# 按绝对路径加载，宿主不再需要 `libsqlite3`。
- 账户 token 只在 C# 使用；UI 只拿账户元数据。
- 失败登录不会写入账户或 token。
- OS 凭据库（DPAPI/Keychain/libsecret）尚未接入。

## 6. 发行与合规

当前为单一发行版：启动时按粗粒度 IP/系统地区判断是否处于中国大陆网络。

- 中国大陆网络默认隐藏下载入口并禁用下载，可手动创建 `compliance.json` 解锁；
- 其他地区默认启用下载；
- 发布包计划只从 GitHub CI 构建；当前新仓库暂未启用 Actions，Workflow 草稿位于 [`ci-drafts/`](ci-drafts/)；
- Microsoft client ID 与未来的 CurseForge key 仅通过 CI secrets 在编译期注入，不进入仓库。

发行通道、Provider/Policy/`DownloadPlan` 与镜像细节见 [`09-distribution-and-download-policy.md`](09-distribution-and-download-policy.md)。


## 图标字体

界面图标不再使用 Material 图标，而是仓库自带的子集字体 `assets/fonts/AppIcons.ttf`（Tabler Icons 3.49.0 裁剪，MIT）。运行时与构建期都不需要联网，也不依赖图标 pub 包。

三层结构：

| 层 | 文件 | 说明 |
|---|---|---|
| 字形清单 | `tool/icons/tabler-icons.txt` | 唯一事实源：Tabler 图标名 + 码位 |
| 生成物 | `assets/fonts/AppIcons.ttf`、`lib/theme/icons/app_icon_data.dart` | 由脚本生成，禁止手改 |
| 语义层 | `lib/theme/icons/app_icons.dart` | 页面只引用 `AppIcons.xxx` |

新增图标：

```bash
# 1. 在 tool/icons/tabler-icons.txt 追加 <tabler-name>\t<codepoint>
#    （码位见上游 tabler-icons.css）
# 2. 生成字体与 Dart 表
./tool/build-icons.sh
# 3. 在 lib/theme/icons/app_icons.dart 里给新字形起一个语义名
```

`build-icons.sh` 首次运行会从 npmmirror（回退 jsDelivr）下载钉住版本的 Tabler webfont 到 `app/.cache/icons/`，并在同一目录建 venv 安装 fontTools；两者都不入库，只有改动图标集时才需要。

常见问题：

- **界面出现空方块**：语义名对应的字形不在字体里。跑 `python3 tool/check_icons.py` 定位；补清单后重跑 `build-icons.sh`。
- **改了清单但忘记重跑脚本**：`dart analyze` 会报 `AppIcons` 里引用了不存在的 `AppIconData` 常量。
- **release bundle 仍是旧字形**：`rm -rf build/flutter_assets` 后重新构建（或 `flutter clean`）。

每次 release 构建后运行：

```bash
python3 tool/check_icons.py            # 默认同时检查源字体与 linux bundle
python3 tool/check_icons.py <assets-dir>
```
