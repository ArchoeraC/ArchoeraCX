# AGENTS.md — ArchoeraCX

## 布局

`app/lib` Flutter（经 `services/core/CoreClient` 调 C++）· `app/core` C++20 枢纽，`vault-rs` 为凭据保险库 · `app/modules/business` C# NativeAOT（业务与 SQLite）· `app/modules/downloader` Rust（只执行 `DownloadPlan`）· `app/contracts` ABI/schema · `docs/` 文档 · `Reference/` 只读参考。

## 架构红线

- Dart 只经 `CoreClient` 调 C++；UI 不读文件、不起进程、不直连业务 API（图片加载例外）。
- C++ 只做生命周期、路由、任务与事件，不写 Mojang/Modrinth/加载器业务。
- C# 独占 API、业务与 SQLite；Flutter 不引入数据库插件。
- Rust 只消费 `DownloadPlan`：不解压、不写 SQLite、不碰业务语义。`.mrpack` 由 C# 安全解压（zip-slip、symlink、size/entry 上限）。
- 例外：Forge/NeoForge 官方 Maven 不可达时，C# 仅对 installer 做 BMCLAPI 镜像引导下载并算 SHA-1。
- 事件必须 wake 驱动：C++ `wake` → Dart `NativeCallable.listener` 批量 drain；**禁止 `Timer.periodic` 轮询**。
- 新方法同时登记 C++ `method_registry` 与 C# `BusinessModuleRegistry`；未知返回 `E_UNSUPPORTED`，导出层不加分支。
- 跨模块走效果协议，不直接互调；跨边界数据须有 schema、owner、生命周期与失败路径。
- token / device code 不跨 ABI、不写日志；client id 不硬编码（CI `-p:MicrosoftClientId`，调试 `ARCHOERACX_MICROSOFT_CLIENT_ID`）。
- 禁止以Python等工具代替write直接操作代码（l10n对键值的翻译可以借助外部工具）
- 禁止加入对用户信息的第三方上传
- 禁止使用pkill -x等自杀终端命令
- 禁止不通过即提交
- 禁止非必要暴力测试
- 本地保存后提醒用户推送，优先使用GPG与SSH推送，降低代理依赖
- 禁止因为代码修改而仍然保留历史遗留

## 边界

- 只改本仓库；草稿与反编译产物放 `Reference/local/`（忽略）。`.gitignore` 不用 `**/bin/`、`**/build/` 等宽规则（会隐藏 Rust `src/bin/`）。
- 不复制 LauncherX / HMCL / Prism / Modrinth 的代码、XAML、图标、插画、文案或字体；新资产登记来源与许可证。不接受外部代码贡献（[`CONTRIBUTING.md`](CONTRIBUTING.md)）。

## 提交前必须验证

```bash
cd app
./tool/build-core.sh && ./tool/build-cs.sh && ./tool/build-dl.sh && ./tool/build-vault.sh
./build/native/abi_smoke && ./build/native/async_smoke && ./build/native/module_smoke
dotnet test modules/business/Archoera.Tests/Archoera.Tests.csproj -c Release
dotnet run --project modules/business/Archoera.DevHost -c Release -- --check
dotnet run --project modules/business/Archoera.DevHost -c Release -- --self-test-loader
cargo test --manifest-path modules/downloader/Cargo.toml
flutter pub get && flutter gen-l10n
dart analyze                 # 中文路径下不要用 flutter analyze
flutter test
ARCHOERACX_NETWORK_TESTS=1 flutter test test/resource_network_test.dart
ARCHOERACX_SETTINGS_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-settings flutter test test/settings_integration_test.dart
ARCHOERACX_INSTANCE_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-instance flutter test test/instance_integration_test.dart
ARCHOERACX_ACCOUNT_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-account flutter test test/account_integration_test.dart
ARCHOERACX_WIKI_TESTS=1 flutter test test/wiki_integration_test.dart
flutter build linux --release
python3 tool/check_icons.py
```

## 版本与协作

- 版本唯一来源 `app/pubspec.yaml`（当前 `0.0.1+1`），须与 `app/lib/utils/app_info.dart` 一致；C++ `build_info`、C# 模块表与 User-Agent、`launcher_version`、Rust crate 版本一并跟随。
- 应用 ID：Linux/Android `awa.archoera.archoera_cx`，macOS `awa.archoera.archoeraCx`。
- ABI 改动同步更新 `app/contracts/abi/archoera_abi.h`、C++、Dart、C harness 与测试。
- 可按 C++/CMake、Dart/Flutter、文档/契约拆子代理并行，但不得同改一个文件；共享契约先冻结，子代理产物由主代理统一构建验证。
- 不臆造进度或测试结果；未运行就写“未验证”。
