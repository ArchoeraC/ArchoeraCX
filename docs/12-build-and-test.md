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

# Linux / macOS：一次构建 C++、C#、Rust 下载器与凭据保险库。
./tool/build-native.sh

flutter pub get
flutter gen-l10n
dart analyze            # 中文路径下不要用 flutter analyze
flutter test
flutter build linux --release   # macOS 用 flutter build macos --release
python3 tool/check_icons.py     # 校验 AppIcons 与自带图标字体一致
```

Windows 使用同构的原生入口，再构建 Windows bundle：

```bat
cd app
tool\build-native.bat
flutter pub get
flutter gen-l10n
dart analyze
flutter test
flutter build windows --release
python tool\check_icons.py build\windows\x64\runner\Release\data\flutter_assets
```

逐模块命令仍然可用：`tool/build-core.sh`（Windows 为 `.bat`）、`build-cs`、`build-dl`、`build-vault`。CI 与本地共用同一个 `build-native` 入口，避免构建顺序或 client id 注入只在一处修改。

macOS 的 Xcode 工程有一个 `Bundle Native Libraries` 阶段，调用 `tool/bundle-macos.sh` 把 C++/C#/Rust/SQLite 收进 `ArchoeraCX.app/Contents/Frameworks` 并逐个 ad-hoc 签名；Windows 的 `app/windows/CMakeLists.txt` 把 `app/build/native` 里的 DLL/EXE 安装到 Release 目录。两者都要求先跑 `build-native`，否则 bundle 里只有 Flutter 外壳。

### 2.1 打包

`flutter build` 之后，`packaging/` 下的脚本生成可分发包（输出到 `dist/`，每个包同时生成 `.sha256`）：

```bash
# Linux 原生格式（Ubuntu 24.04 构建）
bash packaging/linux/package.sh all app/build/linux/x64/release/bundle 0.0.1+1 x64
#   all = tar.gz + .deb + AppImage

# 仅 Debian/Ubuntu .deb；DISTRO_TAG 决定文件名与 BUILD-INFO
DISTRO_TAG=ubuntu24.04 BUILD_BASE="Ubuntu 24.04" \
  bash packaging/linux/package.sh deb app/build/linux/x64/release/bundle 0.0.1+1 x64

# Fedora/RHEL/openSUSE .rpm（需要 rpmbuild）
DISTRO_TAG=fedora bash packaging/linux/package.sh rpm \
  app/build/linux/x64/release/bundle 0.0.1+1 x64

# Arch/Manjaro：先预置工作目录，再在 Arch 上跑 makepkg
DISTRO_TAG=arch bash packaging/linux/package.sh arch \
  app/build/linux/x64/release/bundle 0.0.1+1 x64
(cd packaging/linux/work/arch && makepkg -f --skipinteg --nocheck --nodeps)

# Flatpak（需要 flatpak + flathub）
bash packaging/linux/package-flatpak.sh \
  app/build/linux/x64/release/bundle 0.0.1+1 x64

# NixOS：预置工作目录后在 nix 环境构建
bash packaging/linux/package.sh nix \
  app/build/linux/x64/release/bundle 0.0.1+1 x64
# 路径含非 ASCII 字符时先复制到 /tmp（flake 的 path: 解析更稳）：
#   cp -a packaging/linux/work/nix /tmp/archoera-nix
#   (cd /tmp/archoera-nix && nix build "path:$PWD#default" --option sandbox false)
(cd packaging/linux/work/nix && nix build "path:$PWD#default" --option sandbox false)

# macOS：便携 tar.gz、.dmg（必须在 macOS 上运行 hdiutil）
bash packaging/macos/package.sh all \
  app/build/macos/Build/Products/Release/archoera_cx.app 0.0.1+1 arm64

# Windows：便携 zip、Inno Setup 安装器（需要 ISCC.exe 在 PATH 或默认安装目录）
pwsh packaging/windows/package.ps1 `
  -ReleaseDir app/build/windows/x64/runner/Release `
  -Version 0.0.1+1 -Arch x64 -Installer
```

Windows 的便携形态就是解压即用的 zip：runner 依赖旁加载的 `flutter_windows.dll`、引擎数据与原生模块侧车文件，Flutter 工具链不支持链接成单个 exe，故不做单文件打包（见 `09-distribution-and-download-policy.md` §2.7）。

只给 `package.sh` 传 `<bundle> <version> [arch]`（不带格式）时仍生成便携 tar.gz，兼容旧命令。`all` 是本机便捷入口（tar.gz + deb + AppImage）；CI 按发行版拆分 job，rpm / Arch / Deepin 在各自容器内从源码构建，AppImage / Flatpak / Nix 消费 Ubuntu job 上传的原始 bundle。每个包同时写入 `BUILD-INFO.txt` 与 `.sha256`。

CI 的发行矩阵：

| 产物 | 构建环境 | 说明 |
|---|---|---|
| Ubuntu tar.gz / `.deb` | Ubuntu 24.04 runner | 源码构建；同时上传原始 bundle |
| Deepin `.deb` | Deepin 25 容器 | 源码构建；glibc 2.38 基线 |
| `.rpm` | Fedora 容器 | 源码构建，链接 Fedora 的 glibc/libstdc++/GTK |
| `.pkg.tar.zst` | Arch 容器 | 源码构建，`makepkg` |
| `.AppImage` | Ubuntu 24.04，依赖 linux-deb | 消费 Ubuntu bundle，`linuxdeploy` 收拢 GTK/X11 |
| `.flatpak` | Ubuntu 24.04，依赖 linux-deb | 消费 Ubuntu bundle |
| NixOS `.tar.gz` | Ubuntu + Nix，依赖 linux-deb | 消费 Ubuntu bundle，`autoPatchelfHook` 处理 NixOS 动态库 |
| macOS `.tar.gz` / `.dmg` | macOS-15 | 独立 `macos` job；源码构建并打包 |
| Windows `.zip` / 安装器 | windows-latest | 独立 `windows` job；源码构建并打包 |

AppImage 在 CI 中下载 `appimagetool`、`linuxdeploy` 和 type-2 runtime，用 `linuxdeploy` 收拢 GTK/X11 动态依赖。`.deb` / `.rpm` / Arch 包都安装到 `/opt/archoera-cx`，并注册 desktop 条目、AppStream 元数据和 hicolor 图标。Flatpak 使用 `--filesystem=home` 与只读 `host-os` / `host-etc`，以便访问实例目录和系统 Java。Gentoo overlay 的 ebuild 在 `packaging/gentoo/games-util/archoera-cx/`，直接消费发布 tar.gz。

`workflow_dispatch` 可分别勾选九项：macOS / Windows / Ubuntu deb / Deepin deb / Fedora rpm / Arch pkg / AppImage / Flatpak / Nix。勾选 AppImage、Flatpak 或 Nix 会自动带上 Ubuntu deb（它们消费其原始 bundle）；只有九项全选的手动运行，`release` 才会放行。

`app/shaders/*.frag` 由 `pubspec.yaml` 的 `flutter: shaders:` 声明交给 `impellerc` 编译；`flutter test` 同样会编译，`test/app_shaders_test.dart` 按各着色器声明的 uniform 个数逐个绘制，索引与 GLSL 不一致会在此失败。新增或调整 uniform 时必须同步修改调用方的 `setFloat` 索引。窗口背景是纯色、没有持续动画，测试里的 `pumpAndSettle` 可正常收敛。

Microsoft 登录的 client ID 必须在 C# 构建步骤注入：

```bash
export ARCHOERACX_MICROSOFT_CLIENT_ID="<Application (client) ID>"
./tool/build-cs.sh
```

- CI/Release 构建从 GitHub Actions secret `ARCHOERACX_MICROSOFT_CLIENT_ID`（仓库 Settings → Secrets and variables → Actions）注入；工作流只在编译 C# 模块和校验形状的步骤里暴露该值，不放进 job 级环境。
- `CURSEFORGE_API_KEY` 是 CurseForge Provider 的保留键名；接入实现时与 Microsoft client ID 相同，经 CI secret / MSBuild 属性在构建期注入（当前 registry 尚未消费该属性），默认空值使 provider 在 API 访问配置前不可用。
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

Linux release bundle 在 `lib/` 下自带 `libarchoera_core.so`、`archoera_cs.so`、`libe_sqlite3.so`、`libarchoera_dl.so`、`archoera-dl` 与 `archoera-vault`。macOS bundle 的同一组文件在 `ArchoeraCX.app/Contents/Frameworks/`，Windows 在 `Release/` 根目录（`archoera_core.dll` 等）。`flutter build` 前先跑 `tool/build-native`（或 `.bat`），否则这些文件不会进入 bundle。

macOS 的 App Sandbox 在 `Runner/*.entitlements` 中关闭：启动器需要读写用户选择的任意实例目录并启动 Java；App Store 沙箱不适用于当前分发方式。

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

CI 在 `build-native` 之后用下面这条测试确认 client id 确实编译进了 C# 模块（`system.identity` 只返回摘要，不会输出原始 id）：

```bash
ARCHOERACX_CLIENT_ID_TESTS=1 flutter test test/client_id_integration_test.dart
```

发布 tag 构建额外设置 `ARCHOERACX_REQUIRE_OFFICIAL_BUILD=1`，要求编译期 id 与仓库公开的官方摘要一致；不一致会阻止发版。

### 4.1 在 Linux 主机上预检 Windows / macOS 的 C++ 核心

CI 的三端 runner 才是最终验收；下面两个脚本只用于改完平台代码后的快速编译回归，不代替真机或 CI。

```bash
# Windows：msvc-wine 编译 CMake 工程，再用 Wine 跑四个 C/C++ smoke
bash app/tool/verify-windows-core.sh

# macOS：clang + macOS SDK 交叉编译 arm64 / x86_64 的 dylib 与 smoke（只编译链接，不运行）
bash app/tool/verify-macos-core.sh
```

依赖分别见脚本头部：Windows 需要 `/opt/msvc` 与 `wine`（可用 `MSVC_INSTALL_DIR` 覆盖）；macOS 需要 `clang`、`ld64.lld` 与 `~/.local/share/macos-sdk/MacOSX14.0.sdk`（`ARCHOERACX_MACOS_SDK` 覆盖）。macOS SDK 只用于本地构建，不入库、不随发布分发。

C# NativeAOT 与 Rust 的 Windows/macOS 产物仍由 CI 原生构建：Linux 主机没有对应 AOT 交叉工具链。

## 5. 数据目录与本地文件

```text
Linux:   ~/.local/share/archoera-cx
macOS:   ~/Library/Application Support/ArchoeraCX
Windows: %APPDATA%/ArchoeraCX
```

可用 `ARCHOERACX_DATA_DIR` 覆盖。解析优先级：`ARCHOERACX_DATA_DIR` → 平台目录变量（Linux `XDG_DATA_HOME`、Windows `APPDATA`）→ 用户目录（macOS `Library/Application Support`、Linux `.local/share`）→ 系统临时目录（仅兜底）。相对 `XDG_DATA_HOME` 按规范忽略。主要文件：

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
- 发布包只从 GitHub CI 构建：[`.github/workflows/build-all.yml`](../.github/workflows/build-all.yml) 按发行版拆分 job，Ubuntu / Deepin / Fedora / Arch 各自在对应环境从源码构建，AppImage / Flatpak / Nix 消费 Ubuntu 原始 bundle；macOS 与 Windows 是独立 job，各自完成构建和打包；推送 `v*` tag 后汇总全部产物、生成 `SHA256SUMS` 并发布；
- 构建期凭据只通过 CI secret / MSBuild 属性注入，不进入仓库；当前注入的是 Microsoft client ID。tag 构建会校验 secret 的 GUID 形状，并在打包前用 `system.identity` 断言编译期 id 已生效且为官方 id；

发行模型、Provider/Policy/`DownloadPlan` 与镜像细节见 [`09-distribution-and-download-policy.md`](09-distribution-and-download-policy.md)。


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
