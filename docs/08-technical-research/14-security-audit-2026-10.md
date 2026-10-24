# 14 · 安全审查（2026-10）

> 范围：依赖 CVE、Rust 下载器、C# 业务层、C++ core / C ABI、Dart 边界。
> 方法：OSV 批量查询 + 四个只读子代理读码 + 主代理逐条复核关键路径；**没有做动态利用、ASan/TSan、fuzz**。
> 基线：工作区 2026-10-24（含尚未提交的协议回退改动）。
> 结论：修复了 1 个 CVE、1 个下载器内存 DoS、1 个 modpack 植入 Forge installer 的代码执行链；仍有 4 个 Medium 开放，详见 §3。

---

## 0. 结论速览

| # | 结论 | 状态 |
|---|---|---|
| 1 | `tokio 1.54.0` 命中 `CVE-2026-115567`（仅该版本）；升级到 1.54.1 | **已修复** |
| 2 | 计划里 `chunkSize=1` + 1 TiB 文件可让下载器在任何请求前分配 ~17 TB 的分片表并 spawn 海量任务 | **已修复**（参数边界 + 单文件 1024 片上限） |
| 3 | `.part` / `.part.meta.tmp` 未做符号链接检查，可被诱导覆盖 root 外文件；单流只校验网络字节、不校验落盘内容 | **已修复**（写入前拒绝 link-like；提交前重算 `.part`） |
| 4 | `.mrpack` 可写 `downloads/`，植入被后续预检当作 Forge installer 解析并执行 processor 的 jar | **已修复**（`downloads` 列入 pack 保留根） |
| 5 | CWD 劫持、zip 炸弹、代理凭据、事件队列无界、非终态 release 等 Medium/Low 均已修复；剩余项见 §3（多为同用户本地篡改或产品取舍） | **已修复** |
| 6 | OSV 扫描 217 个 Rust 唯一包、95 个 Pub 包、21 个 NuGet 包，除 tokio 外无已知通告；内嵌 SQLite 3.53.3 与 Flutter engine 是扫描盲区 | [事实] 不代表无漏洞 |

---

## 1. 依赖 CVE

| 包 @ 版本 | 级别 | 通告 | 影响 | 处置 |
|---|---|---|---|---|
| `tokio 1.54.0` | Medium（CVSS 5.8，本地/高复杂度） | `CVE-2026-115567` / `GHSA-5wjr-9356-4wc8` / `RUSTSEC-2026-0384` | `time::Interval` 竞态；本项目未使用 `interval`，可达性低 | 已 `cargo update -p tokio --precise 1.54.1` |

扫描覆盖（OSV，2026-10-24）：

| 生态 | 输入 | 去重包数 | 命中 |
|---|---|---|---|
| crates.io | `app/modules/downloader/Cargo.lock` + `app/core/vault-rs/Cargo.lock` | 217 | 1（tokio） |
| Pub | `app/pubspec.lock`（含 dev） | 95 | 0 |
| NuGet | 6 个 `*.csproj` 的传递依赖 | 21 | 0 |
| C/C++ | 无 FetchContent、无 vendored 第三方、无 submodule | 0 | — |

盲区：[事实] 内嵌 SQLite **3.53.3**（`SQLitePCLRaw.lib.e_sqlite3`）无 OSV 生态条目；Flutter engine / Skia / ICU、插件携带的 native 库、宿主 GTK/dbus、rustc/.NET SDK 自身均未扫描。仓库没有 CI 依赖扫描（`.github` 无 workflow），新披露无法自动发现。

---

## 2. 已修复项

### 2.1 Rust：计划参数导致的内存/任务爆炸（High，DoS）

- 位置：`plan.rs` 校验、`chunk_stream.rs` 分片构造。
- 问题：`chunkSize` 只校验负数，`chunkSize=1` + `size=1 TiB` 会在任何网络请求前生成约 1.1e12 条 range 并逐片 spawn；即使默认 4 MiB，1 TiB 也会产生 262144 个任务与对应 `ValidatedFile` 克隆。
- 修复：`chunkSize` 限定 **64 KiB–64 MiB**（0 关闭 Range）；单文件最多 **1024** 片，超过则按 `size / 1024` 自动放大块大小（`effective_chunk_size`）；`.part.meta` 的 `chunkSize` 随之匹配，旧 meta 自然失效。
- 测试：`plan.rs` 边界用例 + 1 TiB 分片数断言；既有分片测试（64 KiB×1 MiB）仍通过。

### 2.2 Rust：侧车符号链接与单流“验流不验盘”（Medium）

- 位置：`parts.rs`（新增 `reject_link_like`）、`single_stream.rs`、`chunk_stream.rs`、`resume.rs`。
- 问题：计划校验只覆盖 `dest` 的父目录与 `dest` 本身；预置 `x.jar.part -> ~/.bashrc` 后 `File::create`/`set_len` 会跟随链接截断目标。单流路径用网络流计算哈希，写入后被并发修改仍会“校验通过”。
- 修复：写 `.part`、分片写、`meta.tmp` 之前拒绝 link-like/目录；`load_resume` 只接受 ≤64 KiB 的普通文件；单流在 `drop` 文件后对 `.part` 重算整文件哈希，再进 `commit_part`。
- 测试：新增 Unix 符号链接集成测试（目标文件保持不变、结果为 `PATH_ESCAPE`）；既有 hash/续传/截断测试通过。
- 残留：检查与 `open` 之间仍有 TOCTOU；同用户本地攻击者仍可在窗口内替换。彻底修复需要 `O_NOFOLLOW`/`openat`（见 §5）。

### 2.3 C#：modpack 植入 Forge installer 的执行链（High）

- 位置：`Archoera.Application/Packs/ModpackIndex.cs:469`（`ReservedRoots`）、`Archoera.Infrastructure/Packs/PackForgeInstallerBootstrap.cs:43`、`PackInstallService.Repair.cs:145,197`。
- 问题：`.mrpack` 的 `overrides/` 与 `files[]` 允许写 `downloads/loaders/forge/...`；bootstrap 遇到该路径的任意 `PK` 文件直接返回；repair 预检随后 `ParseAsync` 并执行其中声明的 processor → 启动器权限下的任意 Java 代码，且发生在游戏启动前。
- 修复：把 `downloads` 加入 pack 保留根（大小写不敏感的首段比较），pack 不能再写入 launcher 管理的下载目录。
- 测试：`ModpackIndexTests.Parse_rejects_launcher_managed_roots` 扩为 `downloads/...`、`versions/...`、`libraries/...` 三个用例；`dotnet test --filter ModpackIndexTests` 10/10 通过。
- 残留：复用已有 installer 时仍只做 `IsZip` 判断，没有 SHA-1 复核；在 `downloads` 已保留的前提下，这退化为“同用户本地篡改”问题（同权限，不属于提权边界）。建议后续用下载时记录的 sidecar 摘要或 Forge 侧车 SHA-1 复核。

### 2.4 Rust：其余已修的低危项

| 项 | 修复 |
|---|---|
| 公网源 302 到 `http://127.0.0.1`（SSRF/端口探测） | 重定向策略要求目标与原始 URL **同时**是 loopback http；公网 origin 只能跳 https（`client.rs`） |
| `Authorization`/`Cookie` 随 mirror 发送 | 只发给与 `file.url` 同 host 的源（`wire.rs`） |
| `*.part.meta.tmp` 未在黑名单 | `normalize_dest` 拒绝该后缀（`plan.rs`） |
| `.part.meta` 无大小上限 | `load_resume` 限制 64 KiB 且必须是普通文件（`resume.rs`） |
| errno 跨平台错义（Linux 39=ENOTEMPTY 被判磁盘满） | 按 Linux/macOS/Windows 分别映射并加单测（`error.rs`） |
| 取消 2 s 后强杀任务残留 `.part` | 取消 drain 后统一清扫计划内 sidecar（`executor.rs`） |
| 结果/日志中的 URL 带 query token | `source_label` 去掉 query/fragment（`parts.rs`） |

### 2.5 C# 与 C++/Dart 的其余修复

| 领域 | 问题 | 修复 |
|---|---|---|
| C# | CWD 可写时的 `archoera-dl` / `archoera-vault` 劫持 | Release 只认显式 env 与 `AppContext.BaseDirectory` 链；CWD 探测移入 `#if DEBUG`，并校验是已存在文件 |
| C# | 代理 `user:pass` 落盘 | plan 文件 Unix 0600、文件名加 nonce、完成后删除、失败保留并 7 天清理 |
| C# | zip 炸弹 | 总量 64→16 GiB；>1 MiB 条目压缩比 >200:1 拒绝；解压前检查可用空间（payload×1.1 + 64 MiB） |
| C# | `providerBase` 任意 URI / 无响应上限 | Release 仅允许官方 https host；响应 16 MiB 流式上限；JSON `MaxDepth=64` |
| C# | 导入实例 `javaPath` | 只接受绝对、存在、普通文件、非 reparse；内存值限制在 128–65536 MB |
| C# | `instance.delete` 可删配置根外目录 | 递归删除要求目录位于配置实例根内；新增 `outside_root` 原因与 UI 文案 |
| C++ | 事件队列无界 | 4096 上限，优先丢最旧的非终态事件；终态事件永不丢；`seq` 保持单调 |
| C++ | 运行中任务被 release | `archoera_release` 忽略非终态任务；终态结果 10 分钟 TTL；Dart 只在终态后 release |
| C++ | 持锁 `notify_wake` | 终态唤醒移到锁外，避免回调重入自死锁 |
| C++ | worker 只增不减 | 提交新任务时回收已结束线程；shutdown 仍全部 join |
| C++ | release 读取任意 env 模块路径 | 仅 debug 任意路径；release 只接受 core 库目录内的显式路径 |
| C++ | `archoera_poll` 异常丢事件 | 批量取出后在锁外组装 payload；失败时按原序放回队首 |
| Dart | slug 允许 `.`/`..` | 纯点号 slug 归一为 `instance`，新增单测 |
| 供应链 | 无自动依赖更新 | 新增 `.github/dependabot.yml`（cargo ×2 / nuget / pub / actions） |

### 2.6 跟进加固（2026-10-25）

| 领域 | 问题 | 修复 |
|---|---|---|
| Rust | 侧车检查与 open 之间的 TOCTOU | 新增 `open_sidecar`：Unix `O_NOFOLLOW`，Windows `FILE_FLAG_OPEN_REPARSE_POINT`；`ELOOP` 在 Linux/macOS 映射为 `PATH_ESCAPE`；单测覆盖“绕过预检直接 open”的路径 |
| Rust | 公网源 302 到私网/云元数据地址 | 重定向策略拒绝“公网 origin → 私网 https IP 字面量”；IPv4/IPv6（含 `::ffff:`）与 `.local` 均覆盖；LAN 镜像自身的重定向仍允许 |
| Rust | Windows 把 OneDrive 占位文件当链接 | `is_link_like` 改用 `FileTypeExt::is_symlink/is_symlink_dir`，只拒绝符号链接/junction，不再误伤云占位文件 |
| Dart | 图片 URL 可指向内网 | `isSafeImageUrl` 限定 http(s)、拒绝 userinfo 与私网/loopback/IPv4-mapped 地址；`Image.network`/`NetworkImage` 全部走该检查并单测 |
| C++ | `gsettings` 经 PATH 查找 | 按绝对路径查找 `/usr/bin`、`/bin`、`/usr/local/bin`、NixOS 的 `/run/current-system/sw/bin`，不再依赖 PATH |
| C# | 导入 `javaPath` 的链接判断 | 改用 `FileInfo.LinkTarget`，跨平台识别 symlink/junction，同时放过 OneDrive 占位文件 |
| C# | 复用的 Forge installer 未复核 | 见 §2.7 |
| UI | 破坏性整合包只有普通 toast | `PACK_ARCHIVE_BOMB` / `PACK_SYMLINK_ENTRY` / `PACK_PATH_ESCAPE` / `PACK_TOO_LARGE` 走阻塞式警告对话框（说明原因、来源风险），本地 inspect 与后台安装共用根导航入口；普通损坏包仍是 toast |

### 2.7 C#：复用 Forge installer 的摘要复核

- 位置：`PackForgeInstallerBootstrap.cs`、`PackInstallService.Repair.cs`。
- 修复：下载完成时把已校验的 SHA-1 写入 `<installer>.sha1`；复用前比对 sidecar，失配则删除/拒绝；离线修复在没有 sidecar 的旧文件上保持兼容，但 sidecar 存在且不匹配时不再执行。
- 覆盖：`LocalForgeInstallerTests` 的匹配/失配用例（详见本轮 `dotnet test` 结果）。

---

## 3. 剩余风险（已接受 / 需产品决策）

以下不是“没发现”，而是本轮明确不按可利用提权/远程漏洞处理，或需要产品取舍：

- **同用户竞态（Rust/C#）**：侧车写入已用 `O_NOFOLLOW`/reparse 标志封死“跟随链接”；但“哈希 `.part` → `rename`”之间仍可被同用户换文件，installer 的 sidecar 也可能被同用户同时改写。这等价于该用户已能执行本机代码，不属于提权边界。
- **直接计划指向私网 https（Rust）**：计划由 C# 生成；若 C# 被攻破，执行器不是最后边界。当前堵住了“公网服务器 302 到私网 IP 字面量”和 loopback HTTP，但 DNS 解析到私网（rebinding）以及计划本身就写私网 https 仍被允许（会破坏局域网镜像，需显式开关）。
- **JVM argv 中的 access token（C#）**：Linux `/proc/<pid>/cmdline` 默认仅同用户可读，Windows 需要同用户/管理员权限；这仍在同用户边界内，且改参数传递方式会破坏 Minecraft 兼容性。
- **无磁盘空间预检、`dedup`/`executable` 无行为、单流无续传**：功能缺口，已在 README/05 记录，不是安全边界。
- **内嵌 SQLite / Flutter engine / 系统库**：OSV 不覆盖，需人工跟踪；见 §1 盲区。
- **macOS 沙箱 entitlements**：仅含 app-sandbox，联网/文件选择需要额外的 entitlement；这是尚未真正出包的桌面平台问题，不是漏洞。

---

## 4. 判定健全的部分（摘要）

- **Rust 下载器**：dest 归一化/保留名/ADS/重复检测；link/symlink 模式在验证期拒绝；TLS 校验收默认开启；重定向最多 5 跳、拒绝 userinfo、跨 host 剥离凭据；`Content-Range`/`Content-Length`/hash 校验；`#![forbid(unsafe_code)]`；无 `std::process`。
- **C#**：解压路径校验与 symlink 条目拒绝、临时文件 + 原子 `File.Move`；SQLite 全参数化；无 `UseShellExecute`/`cmd /c`/字符串 `Arguments`；保险库 HMAC + stdin 传 token + 0600；PKCE/state；无关闭 TLS 校验的代码；账户日志不含 token。
- **C++/Dart**：10 个导出均有空指针检查与异常边界；返回字符串生命周期明确；方法路由精确匹配、未知方法 `E_UNSUPPORTED`；C++ 不 spawn 子进程；UI 无 `Process.run`/`dart:io` 读写；事件泵 wake 合并、batch 预算后重新调度；模块 ABI 版本/函数指针校验。

---

## 5. 验证与未覆盖

已运行：

```bash
# Rust（下载器）
cargo test                      # 30 unit + 17 integration + 1 doctest，全部通过
cargo clippy --all-targets -D warnings   # 干净
./tool/build-dl.sh              # release 构建通过

# C#
dotnet test app/modules/business/Archoera.Tests/Archoera.Tests.csproj -c Release
# 593 passed, 0 failed
dotnet run --project modules/business/Archoera.DevHost -c Release -- --check
dotnet run --project modules/business/Archoera.DevHost -c Release -- --self-test-loader

# C++ / Dart
./tool/build-core.sh
./build/native/abi_smoke && ./build/native/async_smoke \
  && ./build/native/module_smoke && ./build/native/event_queue_smoke
dart analyze                    # No issues found
flutter test                    # 151 passed, 12 skipped
ARCHOERACX_INSTANCE_TESTS=1 ARCHOERACX_DATA_DIR=/tmp/archoera-instance-audit \
  flutter test test/instance_integration_test.dart   # 3/3

# 其他门禁
./tool/build-vault.sh && python3 tool/check_icons.py
./tool/build-cs.sh
flutter build linux --release     # ✓ build/linux/x64/release/bundle/archoera_cx
```

未运行：ASan/TSan/valgrind、fuzz、真实恶意 `.mrpack` 复现、Windows/macOS 实机路径语义、网络/账户/wiki 等需要外部服务的集成测试。尝试过 `cargo check --target x86_64-pc-windows-msvc` / `x86_64-apple-darwin`，但本机只装了 target manifest、没有对应 std，无法完成跨目标编译；Windows/macOS 分支只做了代码审阅（`FileTypeExt`、`O_NOFOLLOW`/reparse 标志、`FileInfo.LinkTarget`、`InternetAddress`）。仓库没有 `app/android`、`app/ios`，移动端 intent/FileProvider 不在范围内。

---

## 6. 建议的下一步

1. **已完成（本次）**：tokio CVE；Rust 计划边界、侧车 `O_NOFOLLOW`、单流落盘校验、重定向/凭据、errno、取消清扫；C# `downloads` 保留、CWD、代理凭据、zip 炸弹、`providerBase`、导入 `javaPath`、删除根约束、installer sidecar 复核；C++ 事件上限、非终态 release/TTL、锁外唤醒、worker 回收、env 策略、poll 恢复、gsettings 绝对路径；Dart slug/release/env、图片私网过滤；Dependabot。
2. **P1（预防）**：CI 增加 OSV-Scanner/cargo-deny 与 `dotnet list package --vulnerable`；为内嵌 SQLite 3.53.3 建人工 CVE 跟踪。
3. **P2（加固）**：DNS rebinding / 计划直连私网地址的显式开关；磁盘空间预检；C++ JSON 重复键拒绝；macOS 沙箱 entitlements 与实机验证。
4. **P3（验证）**：在 Windows/macOS 跑完整路径/权限矩阵；补 ASan/TSan/fuzz 与恶意 mrpack 回归夹具。
