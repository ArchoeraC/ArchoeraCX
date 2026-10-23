# AGENTS.md — ArchoeraCX

`app/lib` Flutter（经 `services/core/CoreClient` 调 C++）· `app/core` C++20 枢纽（`vault-rs` 凭据保险库）· `app/modules/business` C# NativeAOT（业务与 SQLite）· `app/modules/downloader` Rust（只执行 `DownloadPlan`）· `app/contracts` ABI/schema · `docs/` 文档 · `Reference/` 只读参考。

## 架构红线

- Dart 只经 `CoreClient` 调 C++；UI 不读文件、不起进程、不直连业务 API（图片加载例外）。
- C++ 只做生命周期、路由、任务与事件，不写 Mojang/Modrinth/加载器业务。
- C# 独占 API、业务与 SQLite；Flutter 不引入数据库插件。
- Rust 只消费 `DownloadPlan`：不解压、不写 SQLite、不碰业务语义；`.mrpack` 由 C# 安全解压（zip-slip、symlink、size/entry 上限）。
- 例外：官方 Maven 不可达时，C# 仅对 Forge/NeoForge installer 做 BMCLAPI 镜像引导下载并算 SHA-1。
- 事件必须 wake 驱动（C++ `wake` → Dart `NativeCallable.listener` 批量 drain），禁止 `Timer.periodic`。
- 新方法同时登记 C++ `method_registry` 与 C# `BusinessModuleRegistry`，未知返回 `E_UNSUPPORTED`，导出层不加分支。
- 跨模块走效果协议；跨边界数据须有 schema、owner、生命周期与失败路径。
- token / device code 不跨 ABI、不写日志；client id 不硬编码（CI `-p:MicrosoftClientId`，调试 `ARCHOERACX_MICROSOFT_CLIENT_ID`）。
- 禁止用 Python 等脚本代写代码（l10n 翻译除外）；禁止 pkill -x 等自杀命令；禁止非必要暴力测试。
- 不加入对用户信息的第三方上传；避开各语言 CVE；本地保存后提醒用户推送（GPG/SSH）。
- 禁止未验证即提交；禁止修改后保留历史遗留。

## 目录与拆分

- 新文件放归属领域文件夹，父目录只留门文件/引擎/公共基建。
- `services/core/` 顶层只放 `core_client`（唯一入口）/`core_transport`（FFI+任务）/`core_event`/`event_pump`；其余按域：`accounts` `downloads` `instances` `java` `launch` `loaders` `packs` `resources` `settings` `system` `versions` `wiki`。
- 一域 = 一个 `mixin CoreClientXxx on CoreTransport` + 该域模型；`pages/<name>_page.dart` 只留骨架，私有组件放 `pages/<name>/` 并用 `part`；`widgets/` 按区域分组；`stores/` 一域一文件（组合只放 `providers.dart`）。
- C# 分 Domain/Application/Infrastructure/Exports；Rust 一模块一关注点，`executor.rs` 只编排。
- 单文件目标 ≤800 行；超出须拆分，或为单一职责的引擎/编排/测试夹具并在提交正文说明；500~800 行内出现第二职责也拆。
- 拆分是搬移不是重写（SQL/参数/异常码/envelope 零漂移）；公共名不变，路径可改但须全量改 import、旧路径 grep 归零并同步文档。
- 兼容转发成员须有真实引用并注明；删除即删除（不留注释副本/孤儿 doc/TODO）。

## 单点与命名

- C# handler 构造期建 `Dictionary<string, ModuleMethodDelegate>`（Ordinal），`HandleAsync` 只查表，不写字符串 switch 链。
- 版本串唯一来源 `app/pubspec.yaml`；JSON 走 `JsonReader`/`JsonEnvelope`/`ModuleJson`；路径统一 `PathSemantics`；离线 UUID 统一 `OfflinePlayerUuid`。
- Dart snake_case 文件、PascalCase 类型；C# 文件名=类型名、命名空间随目录、`internal` 优先；Rust 模块名=职责。
- 注释写契约与“为什么”，doc 挂在被描述成员上。

## 测试与安全

- 新逻辑带测试，修 bug 带回归测试；Dart 在 `test/`，C# 在 `Archoera.Tests/`；网络或真实运行时测试用 `ARCHOERACX_*_TESTS=1` 门控。
- 不得删测试、放宽断言或靠重试掩盖；偶发失败定位根因。
- 外部输入路径归一化并拒绝 `..`/绝对路径/符号链接逃逸；链接走 http/https 白名单；下载一律校验哈希（镜像同等）。
- 新依赖评估必要性、许可证与 CVE。

## 边界

- 只改本仓库；草稿与反编译产物放 `Reference/local/`（忽略）。`.gitignore` 不用 `**/bin/`、`**/build/` 等宽规则。
- 不复制 LauncherX / HMCL / Prism / Modrinth 的代码、XAML、图标、插画、文案或字体；新资产登记来源与许可证；不接受外部代码贡献（`CONTRIBUTING.md`）。

## 提交前必须验证

```bash
cd app
./tool/build-core.sh && ./tool/build-cs.sh && ./tool/build-dl.sh && ./tool/build-vault.sh
./build/native/abi_smoke && ./build/native/async_smoke && ./build/native/module_smoke
dotnet test modules/business/Archoera.Tests/Archoera.Tests.csproj -c Release
dotnet run --project modules/business/Archoera.DevHost -c Release -- --check
dotnet run --project modules/business/Archoera.DevHost -c Release -- --self-test-loader
cargo test --manifest-path modules/downloader/Cargo.toml
flutter pub get && flutter gen-l10n && dart analyze && flutter test   # 中文路径下不要用 flutter analyze
ARCHOERACX_NETWORK_TESTS=1 flutter test test/resource_network_test.dart
ARCHOERACX_SETTINGS_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-settings flutter test test/settings_integration_test.dart
ARCHOERACX_INSTANCE_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-instance flutter test test/instance_integration_test.dart
ARCHOERACX_ACCOUNT_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-account flutter test test/account_integration_test.dart
ARCHOERACX_WIKI_TESTS=1 flutter test test/wiki_integration_test.dart
flutter build linux --release
python3 tool/check_icons.py
```

0 error 0 warning（`dart analyze` 与 `build-cs` 均不得留 CS86xx/lint）；布局或路径改动也要跑全套。

## 版本与协作

- 版本唯一来源 `app/pubspec.yaml`（当前 `0.0.1+1`），与 `app_info.dart` 一致；C++ `build_info`、C# 模块表与 UA、`launcher_version`、Rust crate 版本跟随。
- 应用 ID：Linux/Android `awa.archoera.archoera_cx`，macOS `awa.archoera.archoeraCx`；ABI 改动同步 `app/contracts/abi/archoera_abi.h`、C++、Dart、C harness 与测试。
- 可按 C++/CMake、Dart/Flutter、文档/契约拆子代理并行，但不得同改一文件；契约先冻结，产物由主代理统一构建验证。子代理报告不等于验证，以工作区实际状态为准。
- 不臆造进度或测试结果；未运行就写“未验证”。

## 提交

- 标题用祈使句英文单行，正文写“为什么”；不提交构建产物、`client_id.key`、凭据与 `Reference/local/`；提交与 tag 用 GPG 签名，remote 用 SSH。
