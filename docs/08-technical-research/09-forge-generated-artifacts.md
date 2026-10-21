# 09 · Forge/NeoForge 生成物：安装收据、预检与修复

> 目标：把 Forge/NeoForge 安装从「安装器/处理器退出码为 0 就算成功」改成「每个生成物有来源、有哈希、可验证、可修复、可迁移」。
> 范围：Forge 1.20.1 / 1.21.1、NeoForge 21.1（客户端为主，服务端差异单独标注）。
> 标记：**[事实]** 有官方文件/源码/实测；**[推断]** 基于实现与生态行为的判断；**[未验证]** 写代码前必须用真实样本确认。
> 说明：表格为压缩清单，未逐格重复标记；关键结论、推断和风险项在正文/§9 标注。
> 实测日期 **2026-10-21**。Forge 样本通过代理从官方 Maven 下载；NeoForge 样本来自 BMCLAPI 镜像路径。installer 的 SHA-1 是本机实测字节，复核情况见 §9。样本缓存在 `.local/forge-samples/`（不提交）。
> 配套：[02-loader-install.md](02-loader-install.md)（processors 与 Java 边界）、[05-download-engine.md](05-download-engine.md)（下载与原子落盘）、[10-download-sources.md](10-download-sources.md)（下载源）。
>
> **实现状态（2026-10-21）**：`ForgeArtifactPlanner` / `LoaderArtifactVerifier` / `ForgeRepairService` 已落地；`loader.verify` / `loader.repair` 已通过 C++ ModuleHost + C# NativeAOT + Dart `CoreClient` 端到端验证（真实 installer 清单）。`ForgeInstallPlanService` 已能把原版 + 加载器依赖 + installer 合并成下载计划；生成物仍只由修复流程产生。真实 Java processors 全链路和 `launch.prepare` 尚未接线。

---

## 0. 结论速览

| # | 结论 | 依据 |
|---|---|---|
| 1 | **安装退出码不是完成证明。** 处理器可能退出 0 但输出缺失、截断、被杀软隔离，或写到错误位置；必须在每个处理器后校验输出。 | [事实] Forge installer 自身也会校验 `outputs`，不匹配会删文件并失败；不少启动器只检查退出码 |
| 2 | **空 `url` 只是生成物的一种表现，不是唯一表现。** Forge 1.21.1 的 `-client.jar` 是 `version.json.libraries` 里的空 URL 条目；Forge 1.20.1 与 NeoForge 21.1.209 的 `version.json` **根本没有**这条库。 | [事实] 三份 installer 实测；见 §2.5 |
| 3 | 因此启动器不能只靠 `version.json` 推导启动输入；必须有一份 **LoaderArtifactManifest / install receipt**，显式记录启动相关 artifact（`launchJar` / `launchArtifact`）、中间产物和 `requiredForLaunch`。 | [推断] 由 §2.5 直接推出 |
| 4 | `PATCHED_SHA` 并不总是存在：1.20.1、1.21.1 有；NeoForge 21.1.209 没有。没有官方哈希时，只能采用「首次成功生成后记录 + 下次重算」的 TOFU，并保留重跑能力。 | [事实] `install_profile.data` 实测；[推断] TOFU 策略 |
| 5 | `processors[].outputs` 也不完整：NeoForge 21.1.209 的所有处理器都没有 `outputs`；1.20.1 只有 jarsplitter 声明了输出。必须从 `data` 变量和参数推导输出，未知时保守从最早处理器重跑。 | [事实] 实测；[推断] 推导算法 |
| 6 | **权威状态放共享缓存，实例只放收据。** `<data>/meta/loaders/.../manifest.json` 是规范来源；`instances/<id>/.archoera/loader/receipt.json` 只记录 `manifestId`、启动绑定和修复状态。所有路径相对数据根，迁移后仍可用。 | [推断] 结合共享 `libraries/` 与多实例模型 |
| 7 | **先预检，再修复，最后才允许启动游戏 JVM。** 修复本身可以启 Java 跑 processors；游戏 JVM 必须等所有 `requiredForLaunch` 生成物校验通过。 | [推断] 本项目设计；与 02 §6.1 的 PendingProcessors 一致 |
| 8 | **不下载、不内置第三方预生成的 `-client.jar`。** 官方 Maven 上该文件 404；公开分发含 Mojang 版权 class 的预生成 jar 风险高。 | [事实] 实测 404；[事实] 02 §2.5 |

**一句话**：生成物是一等公民，不是安装器的副作用；启动前要像校验游戏 jar 一样校验它，坏了要能自动重跑对应的 processor。

---

## 1. 问题定义

### 1.1 失败长什么样

用户看到的通常不是「processor 失败」，而是启动阶段的二次错误：

| 现象 | 真实原因（常见） |
|---|---|
| `Could not find or load main class net.minecraftforge...` / `cpw.mods.bootstraplauncher...` | `-client.jar` 缺失、0 字节、被截断，或 classpath 指向了错误路径 |
| `NoClassDefFoundError` / `ClassNotFoundException` | `client-extra`、`MC_SRG`、`MC_OFF` 等中间产物缺失，或 `-client.jar` 是半成品 |
| `Invalid or corrupt jarfile` | 处理器写到一半被杀、磁盘满、杀软隔离、跨卷 rename 失败 |
| 启动后立刻崩溃、日志里找不到 Forge 版本 | `version.json` 与生成的 jar 不匹配；实例换了加载器但旧 receipt/缓存未失效 |
| 两个实例只有一个能启动 | 共享 `libraries/` 里的生成物被另一个实例的安装/清理覆盖 |
| 移动数据目录后不能启动 | 安装时把绝对路径写进了重写后的 `install_profile.json` 或 classpath |

### 1.2 为什么「退出码 0」不够

Forge installer 自己的 `PostProcessors` 会做输出校验：对每个声明的 `outputs` 计算 SHA-1，不匹配会记录 `Processor failed, invalid outputs` 并删除坏文件。**[事实]** 对 `forge-1.21.1-52.1.0-installer.jar` 的 `PostProcessors.class` 反汇编可见「expected/actual」比较、`delete()` 与失败返回；这是 installer 2.2.9 的行为。**[事实]**

但启动器常见的做法是：

| 实现 | 安装完成判定 | 后果 |
|---|---|---|
| SJMCL | 逐个 `java -cp` 跑 processor，非 0 退出才失败；**不校验 `outputs` 的 SHA-1** | 退出 0 的半成品会被当作成功；02 §4.4 已记录 |
| Prism | 安装阶段尽量少做，首启由 ForgeWrapper 调 installer；首启失败才暴露 | 安装快，但错误延后，用户看到的是「启动失败」 |
| HMCL | `ForgeNewInstallTask`，有 `outputs` 预检/后检，可续跑 | 更稳，但仍以安装事务为中心 |
| ATLauncher | 启动器自身 JVM 内 `URLClassLoader` 反射调 processor | 无子进程，但 processor 崩溃可能带崩启动器 |
| CmlLib.Core / ProjBobcat | spawn Java 跑 processor；实现较干净 | 适合参考设计，不应假设它们替我们校验所有生成物 |
| MinecraftLaunch | MIT，现代 async 核心 | 可作为 .NET 侧 API/异步设计参考，不复制代码 |

> 结论：**「安装成功」应是「生成物集合校验通过」，而不是「installer main 返回 0」。** 这正是本设计要补齐的差异点。

### 1.3 两种生成物引用方式（本问题的核心）

Forge 系加载器的 `-client.jar` 在启动侧有两种完全不同的绑定方式：

| 方式 | 表现 | 版本样本 | 启动器要做什么 |
|---|---|---|---|
| A. 空 URL 库 | `version.json.libraries[]` 有 `downloads.artifact.url == ""`，带 `path`/`sha1`/`size` | Forge 1.21.1 | 识别为本地生成物；**绝不下载**；按 `path` 校验/加入 classpath |
| B. 未列出的 launch artifact | `version.json.libraries[]` 没有该条目；`install_profile.data.PATCHED` 指向生成路径；启动由 bootstrap 约定消费 | Forge 1.20.1、NeoForge 21.1.209 | 不能只遍历 `libraries`；必须由 receipt 显式记录 `launchArtifact`，并让 `LaunchPlan` 把它交给 bootstrap/模块层 |

**这是最常见的误判**：只实现「扫空 URL」的启动器，在 1.20.1 / NeoForge 21.1 上会认为「没有生成物要校验」，于是缺失的 `-client.jar` 直到 JVM 启动才暴露。**[推断]** 由 §2.5 的实测库列表直接推出。

---

## 2. 实测清单（Forge 1.20.1 / 1.21.1 / NeoForge 21.1）

### 2.1 样本

| 样本 | installer 大小 | installer SHA-1（实测） | `install_profile.spec` | `version.json.id` | `mainClass` |
|---|---:|---|---:|---|---|
| `forge-1.20.1-47.2.0-installer.jar` | 5,973,950 | `ded43dd18b3a1dd5098b114c28432224d72bd9f7` | 1 | `1.20.1-forge-47.2.0` | `cpw.mods.bootstraplauncher.BootstrapLauncher` |
| `forge-1.21.1-52.1.0-installer.jar` | 6,377,616 | `fa4f90047c23e6df4d2b4e649aec7fd5d1e20acd` | 1 | `1.21.1-forge-52.1.0` | `net.minecraftforge.bootstrap.ForgeBootstrap` |
| `neoforge-21.1.209-installer.jar` | 6,953,285 | `ec49fcb434d93fdbd615a0f29388b613c7df13f2` | 1 | `neoforge-21.1.209` | `cpw.mods.bootstraplauncher.BootstrapLauncher` |

- 三者 `version.json` 都有 `inheritsFrom`（分别是 `1.20.1` / `1.21.1` / `1.21.1`），且都**没有** `downloads.client`；原版 jar 由父版本提供。**[事实]**
- Forge 1.21.1 的 installer 内只有 `maven/...-shim.jar`；1.20.1 与 NeoForge 21.1.209 的 `maven/` 目录没有额外产物。**[事实]**
- `install_profile.path`：1.21.1 为 `net.minecraftforge:forge:1.21.1-52.1.0:shim`；1.20.1 与 NeoForge 为 `null`。**[事实]**

### 2.2 `data` 变量

`install_profile.json` 的 `data` 是变量表，值有三种形态：Maven 坐标、installer 内路径（`/data/...`）、字面量（通常是 SHA-1）。**[事实]**

| 变量 | Forge 1.20.1 | Forge 1.21.1 | NeoForge 21.1.209 |
|---|---|---|---|
| `MAPPINGS` | `[de.oceanlabs.mcp:mcp_config:1.20.1-20230612.114412:mappings@txt]` | — | `[net.neoforged:neoform:1.21.1-20240808.144430:mappings@txt]` |
| `MOJMAPS` | `[net.minecraft:client:1.20.1-20230612.114412:mappings@txt]` | `[net.minecraft:client:1.21.1:mappings@tsrg]` | `[net.minecraft:client:1.21.1-20240808.144430:mappings@txt]` |
| `MOJMAPS_SHA` | — | `2a8064cf...` | — |
| `MERGED_MAPPINGS` | `[de.oceanlabs.mcp:mcp_config:...:mappings-merged@txt]` | — | `[net.neoforged:neoform:...:mappings-merged@txt]` |
| `BINPATCH` | `/data/client.lzma`（server 为 `/data/server.lzma`） | 同左 | 同左 |
| `MC_UNPACKED` | `[net.minecraft:client:...:unpacked]` | `[net.minecraft:client:1.21.1]` | `[net.minecraft:client:...:unpacked]` |
| `MC_UNPACKED_SHA` | — | `30c73b1c...` | — |
| `MC_SLIM` | `[net.minecraft:client:...:slim]` | — | `[net.minecraft:client:...:slim]` |
| `MC_SLIM_SHA` | `de86b035...` | — | — |
| `MC_EXTRA` | `[net.minecraft:client:...:extra]` | — | `[net.minecraft:client:...:extra]` |
| `MC_EXTRA_SHA` | `8c5a95cb...` | — | — |
| `MC_SRG` | `[net.minecraft:client:...:srg]` | — | `[net.minecraft:client:...:srg]` |
| `MC_OFF` | — | `[net.minecraft:client:1.21.1:official]` | — |
| `MC_OFF_SHA` | — | `fd32ce6d...` | — |
| `PATCHED` | `[net.minecraftforge:forge:1.20.1-47.2.0:client]` | `[net.minecraftforge:forge:1.21.1-52.1.0:client]` | `[net.neoforged:neoforge:21.1.209:client]` |
| `PATCHED_SHA` | `3e175b011146785588f1649a20d1834d10282a7c` | `2d2537d306a05aeac6ed5fa9bf176ffacb78cb4f` | **无** |
| `MCP_VERSION` | `'20230612.114412'` | — | `'1.21.1-20240808.144430'` |

> 注：上表只列关键变量，且是 **client 侧**值；server 侧会不同（如 `BINPATCH=/data/server.lzma`、`MOJMAPS` 指向 server mappings）。完整列表以 installer 内 `install_profile.json` 为准。`*_SHA` 是字面量，不是 Maven 坐标。**[事实]**

### 2.3 processors 链（按 `sides` 过滤前）

#### Forge 1.20.1（10 个）

| # | sides | 工具 | 任务 | 输出 |
|---:|---|---|---|---|
| 0 | server | `installertools:1.3.0` | `EXTRACT_FILES`（run.sh/args） | 无声明 |
| 1 | server | `installertools:1.3.0` | `BUNDLER_EXTRACT`（libraries） | 无声明 |
| 2 | server | `installertools:1.3.0` | `BUNDLER_EXTRACT`（jar-only） | 无声明 |
| 3 | 双 | `installertools:1.3.0` | `MCP_DATA` | 无声明 |
| 4 | 双 | `installertools:1.3.0` | `DOWNLOAD_MOJMAPS` | 无声明 |
| 5 | 双 | `installertools:1.3.0` | `MERGE_MAPPING` | 无声明 |
| 6 | **client** | `jarsplitter:1.1.4` | 拆 slim/extra | `{MC_SLIM}`, `{MC_EXTRA}` |
| 7 | server | `jarsplitter:1.1.4` | 拆 slim/extra | `{MC_SLIM}`, `{MC_EXTRA}` |
| 8 | 双 | `ForgeAutoRenamingTool:0.1.22:all` | official → srg | 无声明（实际 `{MC_SRG}`） |
| 9 | 双 | `binarypatcher:1.1.1` | 解 LZMA + delta | 无声明（实际 `{PATCHED}`） |

客户端实际执行：`3, 4, 5, 6, 8, 9` = **6 个**。**[事实]**

#### Forge 1.21.1（7 个）

| # | sides | 工具 | 任务 | 输出 |
|---:|---|---|---|---|
| 0 | server | `installertools:1.4.3` | `EXTRACT_FILES` | 无声明 |
| 1 | server | `installertools:1.4.3` | `BUNDLER_EXTRACT`（libraries） | 无声明 |
| 2 | server | `installertools:1.4.3` | `BUNDLER_EXTRACT`（jar-only） | `{MC_UNPACKED}` |
| 3 | 双 | `installertools:1.4.3` | `DOWNLOAD_MOJMAPS`（`--sanitize`） | `{MOJMAPS}` |
| 4 | server | `ForgeAutoRenamingTool:1.0.6` | ATR（server） | `{MC_OFF}` |
| 5 | **client** | `ForgeAutoRenamingTool:1.0.6` | ATR（client） | `{MC_OFF}` |
| 6 | 双 | `binarypatcher:1.2.0` | 解 LZMA + delta | `{PATCHED}` |

客户端实际执行：`3, 5, 6` = **3 个**。**[事实]**

#### NeoForge 21.1.209（10 个）

| # | sides | 工具 | 任务 | 输出 |
|---:|---|---|---|---|
| 0 | server | `installertools:2.1.2` | `EXTRACT_FILES` | 无声明 |
| 1 | server | `installertools:2.1.2` | `BUNDLER_EXTRACT`（libraries） | 无声明 |
| 2 | server | `installertools:2.1.2` | `BUNDLER_EXTRACT`（jar-only） | 无声明 |
| 3 | 双 | `installertools:2.1.2` | `MCP_DATA` | 无声明 |
| 4 | 双 | `installertools:2.1.2` | `DOWNLOAD_MOJMAPS` | 无声明 |
| 5 | 双 | `installertools:2.1.2` | `MERGE_MAPPING` | 无声明 |
| 6 | **client** | `jarsplitter:2.1.2` | 拆 slim/extra | 无声明（实际 `{MC_SLIM}`, `{MC_EXTRA}`） |
| 7 | server | `jarsplitter:2.1.2` | 拆 slim/extra | 无声明 |
| 8 | 双 | `AutoRenamingTool:2.0.3:all` | official → srg | 无声明（实际 `{MC_SRG}`） |
| 9 | 双 | `binarypatcher:2.1.2:fatjar` | 解 LZMA + delta | 无声明（实际 `{PATCHED}`） |

客户端实际执行：`3, 4, 5, 6, 8, 9` = **6 个**。**[事实]**

### 2.4 `outputs` 与 `PATCHED_SHA` 的不一致

| 版本 | 声明了 `outputs` 的 processor | 未声明但启动需要的产物 |
|---|---|---|
| Forge 1.20.1 | #6 jarsplitter：`MC_SLIM`、`MC_EXTRA` | #8 `MC_SRG`、#9 `PATCHED`；`PATCHED_SHA` 单独给出 |
| Forge 1.21.1 | #2 `MC_UNPACKED`；#3 `MOJMAPS`；#4/#5 `MC_OFF`；#6 `PATCHED` | #0/#1 服务端脚本；shim 是 installer 库，不是 processor 输出 |
| NeoForge 21.1.209 | **无** | `MC_SLIM`、`MC_EXTRA`、`MC_SRG`、`PATCHED`；`PATCHED_SHA` 也没有 |

**结论**：
- 不能只信 `outputs`：NeoForge 这一代完全不声明输出，Forge 1.20.1 也只声明了一部分。
- 不能只信 `PATCHED_SHA`：NeoForge 没有；1.20.1 只有 SHA-1 没有 size。
- 必须把「data 变量 + processor 参数 + outputs」三者结合，生成一份**显式的产物清单**；无法确定时保守重跑。**[推断]**

### 2.5 空 URL 库与启动 artifact 绑定

| 版本 | `version.json.libraries` 数量 | 空 `url` 条目 | 具体条目 | 启动绑定 |
|---|---:|---:|---|---|
| Forge 1.20.1 | 29 | **0** | — | `install_profile` 的 `PATCHED` 指向 `libraries/net/minecraftforge/forge/1.20.1-47.2.0/forge-1.20.1-47.2.0-client.jar`，**未出现在 libraries**；installer 只把原版 jar 下到 `versions/1.20.1/1.20.1.jar`，processor 输出留在 `libraries/`；需 receipt 绑定 `launchArtifact` **[事实/推断]** |
| Forge 1.21.1 | 49 | **1** | `net.minecraftforge:forge:1.21.1-52.1.0:client`，`path` 为 `.../forge-1.21.1-52.1.0-client.jar`，`sha1=2d2537d306a05aeac6ed5fa9bf176ffacb78cb4f`，`size=28054104` | 空 URL 库，按合并规则进入 classpath；**不能下载** |
| NeoForge 21.1.209 | 47 | **0** | — | `PATCHED` 路径 `libraries/net/neoforged/neoforge/21.1.209/neoforge-21.1.209-client.jar`，**未出现在 libraries**；需 receipt 绑定 `launchArtifact` **[事实/推断]** |

补充事实：
- 三者 `version.json` 都没有 `downloads.client`，都靠 `inheritsFrom` 指向原版。**[事实]**
- Forge 1.20.1 的 `arguments.jvm` 含 `-DignoreList=...,forge-,${version_name}.jar`；NeoForge 21.1.209 含 `-DignoreList=client-extra,${version_name}.jar`；Forge 1.21.1 只有 `-Djava.net.preferIPv6Addresses=system`。**[事实]** 这解释了为什么不同代的启动侧消费方式不同，不能用一个「扫空 URL」规则覆盖全部。
- 对照 Forge Installer 2.x 源码：`ClientInstall.run` 只把原版 client 下载到 `versions/<mc>/<mc>.jar`，然后 `processors.process(librariesDir, clientTarget, target, installer)`；processor 的输出路径在 `librariesDir` 下，代码不把 `PATCHED` 复制成版本 jar。**[事实]** 所以 `PATCHED` 更像「bootstrap 侧 artifact」，不能假设它等于 `LaunchPlan.clientJar`；receipt 必须按 role 记录，具体模块层/ignoreList 绑定见 §9 未验证项 1。
- 官方 Maven 的 `forge-1.21.1-52.1.0-client.jar` 实测返回 **404**；BMCLAPI 同路径也 404。**[事实]** 所以不存在「直接下载 client jar」的兜底。

### 2.6 生成/抽取物清单

路径模板统一为数据根相对路径；Maven 坐标按 `group/artifact/version/artifact-version[-classifier].ext` 展开。**[推断]**

| key | 角色 | 路径模板 | 1.20.1 | 1.21.1 | NeoForge 21.1 | 期望哈希来源 |
|---|---|---|---|---|---|---|
| `INSTALLER` | 输入 | `downloads/objects/sha1/...` | ✓ | ✓ | ✓ | installer SHA-1 |
| `INSTALL_PROFILE` | 输入 | 从 installer 抽取 | ✓ | ✓ | ✓ | 原始字节 SHA-1 |
| `VERSION_JSON` | 输入/元数据 | 从 installer 抽取 | ✓ | ✓ | ✓ | 原始字节 SHA-1 |
| `BINPATCH` | 输入 | `data/client.lzma`（临时） | ✓ | ✓ | ✓ | 抽取后 SHA-1（installer 无声明） |
| `MAPPINGS` | 中间 | `libraries/.../mcp_config-...-mappings.txt` | ✓ | — | ✓ | 无官方声明；按下载/生成记录 |
| `MOJMAPS` | 中间 | `libraries/net/minecraft/client/.../client-...-mappings.txt` 或 `.tsrg` | ✓ | ✓ | ✓ | 1.21.1 `MOJMAPS_SHA`；其余无 |
| `MERGED_MAPPINGS` | 中间 | `libraries/.../mcp_config-...-mappings-merged.txt` | ✓ | — | ✓ | 无 |
| `MC_UNPACKED` | 中间 | `libraries/net/minecraft/client/.../client-...[-unpacked].jar` | server | server | server | 1.21.1 `MC_UNPACKED_SHA` |
| `MC_SLIM` | 中间 | `libraries/net/minecraft/client/.../client-...-slim.jar` | ✓ | — | ✓ | 1.20.1 `MC_SLIM_SHA` |
| `MC_EXTRA` / `client-extra` | 中间 | `libraries/net/minecraft/client/.../client-...-extra.jar` | ✓ | — | ✓ | 1.20.1 `MC_EXTRA_SHA` |
| `MC_SRG` | 中间 | `libraries/net/minecraft/client/.../client-...-srg.jar` | ✓ | — | ✓ | 无 |
| `MC_OFF` | 中间 | `libraries/net/minecraft/client/.../client-...-official.jar` | — | ✓ | — | 1.21.1 `MC_OFF_SHA` |
| `PATCHED` | **启动 artifact** | `libraries/net/minecraftforge/forge/.../forge-...-client.jar` 或 `libraries/net/neoforged/neoforge/.../neoforge-...-client.jar` | ✓ | ✓ | ✓ | 1.20.1/1.21.1 `PATCHED_SHA`；NeoForge 无 |
| `SHIM` | installer 库 | `libraries/net/minecraftforge/forge/.../forge-...-shim.jar` | — | ✓ | — | install_profile 库的 SHA-1 |
| `RUN_SCRIPTS` | 服务端脚本 | `run.sh`/`run.bat`/`*_args.txt` | server | server | server | 无 |

> 「中间」不等于可丢：`MC_SRG`/`MC_OFF`/`MC_SLIM`/`MC_EXTRA` 是后续 processor 的输入；删了它们，下一次修复无法只重跑最后一步。**[推断]**
> 1.21.1 未发现 `MC_EXTRA`/jarsplitter 变量；这不等于客户端永远不需要 `client-extra`，见 §9 未验证项 2。

---

## 3. `LoaderArtifactManifest` 设计

### 3.1 不变量

1. **身份唯一**：`(loader.kind, loader.gameVersion, loader.version, side, installer.sha1, recipe.sha1)` 唯一确定一份生成物集合；同一加载器版本换侧别（client/server）是两份 manifest。
2. **不可变清单 + 可变状态分离**：`manifest.json` 只存 recipe 与期望哈希；`status.json` 存 `lastVerifiedAt/state/lastFailure`；否则每次校验都改 manifest，`manifestId` 会漂移。
3. **相对路径**：manifest 中所有路径相对数据根；不写盘符、用户名、绝对路径、临时目录。
4. **每个生成物都有 `producerIndex`**；抽取物用 `-1`，并标注 `source: installer`。
5. **`requiredForLaunch` 明确**：中间产物可能不是 classpath 条目，但仍是修复输入；启动前至少校验 `requiredForLaunch=true` 的项。
6. **可重建**：只要 installer/inputs 还在，manifest 能重放 processors；不依赖被重写过的 `install_profile.json`。

### 3.2 Schema（v1）

以下为节选：`processorRecipe` 只展示一个 processor，实际应包含完整链；`generatedArtifacts` 同理。**[推断]**

```json
{
  "schemaVersion": 1,
  "manifestId": "sha256:...",
  "loader": {
    "kind": "forge",
    "version": "52.1.0",
    "gameVersion": "1.21.1",
    "side": "client",
    "profileId": "1.21.1-forge-52.1.0"
  },
  "installer": {
    "coordinate": "net.minecraftforge:forge:1.21.1-52.1.0:installer",
    "filename": "forge-1.21.1-52.1.0-installer.jar",
    "url": "https://maven.minecraftforge.net/net/minecraftforge/forge/1.21.1-52.1.0/forge-1.21.1-52.1.0-installer.jar",
    "sha1": "fa4f90047c23e6df4d2b4e649aec7fd5d1e20acd",
    "size": 6377616
  },
  "profile": {
    "spec": 1,
    "installProfileSha1": "...",
    "versionJsonSha1": "...",
    "recipeSha1": "..."
  },
  "processorRecipe": [
    {
      "index": 3,
      "tool": "net.minecraftforge:installertools:1.4.3",
      "mainClass": "net.minecraftforge.installertools.Main",
      "sides": ["client", "server"],
      "classpath": ["net.minecraftforge:installertools:1.4.3"],
      "args": ["--task", "DOWNLOAD_MOJMAPS", "--sanitize", "--version", "1.21.1", "--side", "{SIDE}", "--output", "{MOJMAPS}"],
      "outputs": { "MOJMAPS": { "sha1": "2a8064cf9359dd03125bc6f4aa99715dbbf50492" } }
    }
  ],
  "generatedArtifacts": [
    {
      "key": "PATCHED",
      "role": "classpathLibrary",
      "path": "libraries/net/minecraftforge/forge/1.21.1-52.1.0/forge-1.21.1-52.1.0-client.jar",
      "sha1": "2d2537d306a05aeac6ed5fa9bf176ffacb78cb4f",
      "size": 28054104,
      "producerIndex": 6,
      "requiredForLaunch": true,
      "expected": { "sha1": "2d2537d306a05aeac6ed5fa9bf176ffacb78cb4f", "origin": "version-json" }
    }
  ],
  "inputs": [
    { "key": "MINECRAFT_JAR", "path": "versions/1.21.1/1.21.1.jar", "sha1": "30c73b1c5da787909b2f73340419fdf13b9def88", "size": 26836906 },
    { "key": "INSTALLER", "path": "downloads/objects/sha1/fa/fa4f...", "sha1": "fa4f...", "size": 6377616 }
  ]
}
```

字段说明：

| 字段 | 含义 | 约束 |
|---|---|---|
| `manifestId` | 对不可变子集（schemaVersion/loader/installer coordinate+sha1+size/profile/processorRecipe/generatedArtifacts/inputs）的 canonical JSON 做 SHA-256 | 不含 `installer.url`/时间戳/状态；用于 receipt 引用；同一字节从不同镜像下载仍是同一 ID |
| `loader.kind` | `forge` / `neoforge` | 与 `instance.json.loader.kind` 对齐 |
| `loader.version` | 加载器版本（Forge 1.20.1 用 `47.2.0`，gameVersion 单独存） | 不解析版本号反推 MC 版本 |
| `installer.sha1/size` | 实际下载字节 | 身份的一部分；换 installer 就换 manifest |
| `profile.recipeSha1` | 归一化后的 `data` + `processors` + `side` 的哈希 | 用于检测 profile 漂移 |
| `processorRecipe[]` | 可重放的处理器配方（工具坐标、主类、classpath 坐标、参数模板、输出 key/期望哈希） | 不存绝对路径；`{ROOT}`/`{LIBRARY_DIR}` 运行期解析 |
| `generatedArtifacts[].key` | 与 `data` 变量一致的 key（`PATCHED` 等） | 唯一 |
| `role` | `launchJar`/`launchArtifact`/`classpathLibrary`/`clientExtra`/`intermediate`/`metadata` | 启动绑定依据；Forge 1.21.1 的空 URL client 是 `classpathLibrary` [事实]；1.20.1/NeoForge 的 `PATCHED` 是 `launchArtifact`（bootstrap 侧，非版本 jar）[推断] |
| `producerIndex` | 产出它的 processor 下标；抽取物 `-1` | 用于最早重跑点 |
| `requiredForLaunch` | 缺了就不能启动 | 中间产物可为 false，但仍参与修复 |
| `expected` | 期望哈希来源：`version-json`/`profile-outputs`/`profile-data-sha`/`tofu`/`size-only` | `tofu` 必须在首次成功后写回 |

> 如果 `version.json` 有 `sha1/size`（如 Forge 1.21.1 空 URL 库），优先用 `version-json`；否则用 `outputs`/`PATCHED_SHA`；再否则首次成功后记录 `tofu`。**[推断]**
>
> 编号约定：本文 §2 表格为便于对照 install_profile 使用**原始 index**（0–9）；落盘/代码应使用 `manifest.Processors[]` 的稳定下标。建议同时保存 `sourceIndex`（原始）与 `producerIndex`（manifest 下标），或全链路统一过滤后下标，避免修复时错位。**[推断]**

### 3.3 存储位置与职责

```
<data>/
  meta/loaders/
    forge/1.21.1/52.1.0/client/fa4f90047c23/
      manifest.json          # 不可变：recipe + 期望哈希
      status.json            # 可变：state/lastVerifiedAt/lastFailure
      journal.json           # 修复事务：当前 processor/临时目录/已完成
      logs/processor-6.log   # 环形/滚动，脱敏
  shared/libraries/...       # 生成物与下载库
  shared/versions/...         # 版本 jar（若加载器要求）
  downloads/objects/...       # 内容寻址缓存
instances/<id>/
  .archoera/loader/
    receipt.json             # 实例收据：manifestId + launchArtifactKeys + installedAt
    quarantine/<txid>/...    # 坏输出，保留供诊断
```

| 文件 | 权威性 | 说明 |
|---|---|---|
| `shared/meta/loaders/.../manifest.json` | **规范来源（期望状态）** | 同一加载器版本的所有实例共享；避免两份 recipe 漂移 |
| `instances/<id>/.archoera/loader/receipt.json` | 实例绑定 | 记录该实例用哪份 manifest、哪些启动 artifact、最后验证时间；可随实例移动 |
| `instance.json.loader` | 用户意图 | 用户选择的加载器/版本；与 receipt 冲突时以用户意图为准，触发重装/修复 |
| `status.json` | 运行状态 | 不参与 `manifestId`；可重建 |
| `journal.json` | 崩溃恢复 | 修复中断后据此回滚/续跑；提交后删除或标记完成 |
| SQLite | 索引/GC 辅助 | 只由 C# 写；不是权威文件内容 |

写入规则：`manifest.json`/`receipt.json` 用 `<name>.tmp` → `fsync` → 原子 `rename`；保留最近 3 份 `.bak`。`status.json`/`journal.json` 同理。不要直接原地改写 manifest。**[推断]**

**为什么不是只放实例目录**：生成物在 `shared/libraries/`，两个实例共用；如果每个实例各存一份 manifest，A 修复后 B 的 expected hash 可能过期。**为什么还留实例收据**：它记录「这个实例启动时到底该用哪个 jar」，且不含绝对路径，迁移/复制实例仍可读。

### 3.4 源与状态的优先级

```
用户意图: instance.json.loader
   │ 不一致 → 需要重装/切换，不直接信任 receipt
   ▼
实例收据: receipt.manifestId
   │ 缺失/找不到 → 尝试按 loader 身份找共享 manifest；找不到则 repair/reinstall
   ▼
共享 manifest: expected artifacts + recipe
   │ 与实际文件 hash 比较
   ▼
实际文件: 唯一字节真相
```

- `receipt` 不是「安装成功证明」，只是一张指针；每次预检都要重新验证文件。**[推断]**
- `manifestId` 找不到但共享目录里有同名 manifest 时，可按 `loader` 身份重新绑定，并记录 `migratedFrom`。**[推断]**

### 3.5 迁移

| 场景 | 处理 |
|---|---|
| manifest schema 升级 | 读旧版 → 补默认字段 → 写回 `schemaVersion=2`；保留旧文件 `.bak`；`manifestId` 重新计算并更新 receipt |
| 旧实例只有 `install_profile.json` 重写文件、没有 receipt | 从实例目录/缓存找 installer；重建 manifest；若 installer 缺失，标 `legacy-unverified`，不自动删除，提示「重新安装加载器」 |
| 数据根移动/换机 | 所有路径相对数据根；receipt 只存 `manifestId`；预检按新根解析。禁止把绝对路径写进 manifest/journal |
| 加载器版本切换 | 新 manifest key；旧 manifest 保留；旧 receipt 标 `superseded`；GC 按引用清理 |
| 同一版本换了 installer 字节 | 新 `installer.sha1` → 新 manifest；旧 manifest 不覆盖；避免「同版本不同来源」混用 |
| `version.json` 被用户/工具改写 | `versionJsonSha1` 不匹配 → `VERSION_JSON_MODIFIED`；要求重新安装或显式「信任本地修改」 |

### 3.6 与当前 Domain 模型的对齐（只读观察）

> 本节只记录文档与现有代码的对应关系，不修改代码；用于在实现前冻结字段/编号。当前代码位于 `app/modules/business/Archoera.Domain/Loaders/`、`Archoera.Application/Loaders/`、`Archoera.Infrastructure/Loaders/`。

| 本文概念 | 当前模型（只读） | 备注 |
|---|---|---|
| loader kind/game/version/installer sha1 | `LoaderId(Kind, GameVersion, LoaderVersion, InstallerSha1)` | 与本文身份键一致 |
| artifact `path` | `LoaderArtifact.RelativePath` + `Location(Root/Libraries)` | 落盘 wire 名是 `relativePath`，本文用 `path` 简写 |
| `key/sha1/size/producerIndex/requiredForLaunch` | `LoaderArtifact` 同名 | 另有 `Kind(Generated/Extracted)` |
| processor recipe | `ProcessorStep(Index, JarRelativePath, ClassPath, MainClass, Arguments, Outputs)` | 路径相对 libraries；参数用 `{ROOT}`/`{LIBRARIES}`/`{INSTALLER}`/`{MINECRAFT_JAR}` |
| manifest 根 | `LoaderArtifactManifest(InstallerPath, VersionJsonPath, VersionId, LibrariesPath, Artifacts, Processors)` | 当前是**实例根相对**，不是本文的共享 `meta/loaders/` |
| 校验结果 | `LoaderVerification(Ok, Missing, Mismatched, Details, Unsafe)` | 与本文预检结果可映射 |
| 修复结果 | `LoaderRepairResult(Status, Verification, ProcessorsRun, Details)` | `AlreadyValid/Repaired/Failed`；`ProcessorsRun` 可证明幂等 |

**落地前必须对齐的点**：

1. **processor 编号**：当前 `ProcessorStep.Index` 是 **client 过滤后的 0..n-1**；本文 §2 表格为便于对照 install_profile 使用原始 index（#6/#9）。`producerIndex` 必须与 manifest 的 `Processors[]` 下标一致；建议加 `SourceIndex` 保存原始 index，或全链路统一用过滤后 index。
2. **未列出的 `PATCHED`**：已处理。`ForgeArtifactPlanner.AddDataArtifacts()` 会遍历 `data` 中解析到 `libraries/` 的变量，只保留被客户端 processor 使用或已被 version JSON 引用的项，显式加入 `PATCHED`（`RequiredForLaunch=true`），并从 processor 的 `--output` 参数推导 `ProducerIndex`；`ForgeRepairService` 的 `OutputsForProcessor` 会合并 `processors[].outputs` 与 manifest 中同 `producerIndex` 的 artifact，因此 Forge 1.20.1 / NeoForge 21.1 的 `PATCHED` 缺失时仍会重跑对应 processor。服务端专用变量（如 1.20.1 的 `MC_UNPACKED`）不会污染客户端清单。已用真实 1.20.1 / 1.21.1 / NeoForge 21.1 installer 验证清单；Forge 1.20.1 的 `PATCHED_SHA` 单引号字面量也已归一化。**[已实现]**
3. **共享 vs 实例根**：当前 manifest 是实例根相对；本文的共享 `meta/loaders/` + 实例 receipt 是目标态。若继续实例根，多实例会重复生成/可能漂移；至少需要 `manifestId`/installer 注册表与 GC。
4. **跨进程锁**：当前 repair 用进程内 `SemaphoreSlim`；两个进程（两个实例/两个启动器窗口）同时修复仍需 OS 文件锁（本文 §5.3）。
5. **manifest 扩展字段**：`manifestId`、`status.json`、`installer.url/size`、`profile.recipeSha1`、`expected.origin` 是本文建议，当前模型尚未包含；新增必须可选并保持 schema 兼容。
6. **runner**：当前已有 argv 数组、清空 `JAVA_TOOL_OPTIONS`/`_JAVA_OPTIONS`/`JDK_JAVA_OPTIONS`/`CLASSPATH`、超时、`Kill(entireProcessTree)`；与本文 §5.5 一致，可继续复用。

---

### 3.7 GC 与引用

- **只 GC 生成物/共享缓存，不碰用户文件**。生成物指 manifest 中 `source` 为 processor/installer 的路径；用户放在 `mods/`、`config/`、`saves/` 的文件永不进入 GC。
- 引用来源：所有实例 receipt 的 `manifestId` + 所有共享 manifest 的 `generatedArtifacts[].path/sha1`。
- 规则：
  1. 有实例 receipt 引用 → 不删。
  2. 无 receipt，但仍有 manifest 引用同一路径且 hash 相同 → 不删。
  3. 无任何引用且超过 30 天 → 移入 `<data>/trash/loaders/<manifestId>/`，不直接 `rm`。
  4. 路径在实际文件中 hash 与 manifest 不一致（用户改过/损坏）→ 不删，标 `unknown-provenance`，提示诊断。
- 并发：GC 持全局 loader 读写锁的写锁；repair/commit 持读锁；删除失败（占用）跳过。
- SQLite 可加 `loader_manifests` / `loader_artifacts` 索引表，但以文件扫描可重建；不要把 DB 当唯一来源。**[推断]**

---

## 4. 预检（preflight）

### 4.1 目标

在**不启动游戏 JVM**的前提下回答三个问题：

1. 这份实例的加载器身份是否与 manifest 一致？
2. `version.json`/classpath 需要的文件是否齐全、哈希是否正确？
3. 如果不对，最少从哪个 processor 开始修？

### 4.2 步骤

| # | 检查 | 失败码 |
|---:|---|---|
| 1 | 读 `instance.json.loader`；与 receipt/manifest 的 `kind/gameVersion/version/side` 比较 | `LOADER_IDENTITY_MISMATCH` |
| 2 | 读 receipt；找 manifest；校验 `manifestId`/`schemaVersion` | `MANIFEST_MISSING` / `MANIFEST_CORRUPT` / `SCHEMA_UNSUPPORTED` |
| 3 | 校验 `install_profile.json`/`version.json` 原始 SHA-1（若记录） | `PROFILE_MODIFIED` / `VERSION_JSON_MODIFIED` |
| 4 | 解析 `version.json`：`mainClass` 非空、`inheritsFrom` 可解析、`libraries` schema、无重复 key、无绝对路径 | `VERSION_JSON_INVALID` |
| 5 | 遍历有效版本的 libraries：区分 remote / Maven 坐标 / 空 URL 生成物 | `LIBRARY_SCHEMA_UNSUPPORTED` |
| 6 | 对 `generatedArtifacts` 中 `requiredForLaunch=true` 的项：存在、size、SHA-1 | `ARTIFACT_MISSING` / `ARTIFACT_SIZE_MISMATCH` / `ARTIFACT_HASH_MISMATCH` |
| 7 | 校验启动 artifact 绑定：receipt 指定路径存在且 hash 正确；`version.json` 空 URL 项也有对应 manifest 项 | `LAUNCH_ARTIFACT_MISSING` / `UNKNOWN_GENERATED_ARTIFACT` |
| 8 | 校验 Java：major 匹配 `javaVersion.majorVersion`；路径可执行 | `JAVA_MISSING` / `JAVA_VERSION_MISMATCH` |
| 9 | 校验磁盘可写、目标路径未被用户文件占用、锁状态 | `DISK_FULL` / `PERMISSION_DENIED` / `PATH_OCCUPIED_BY_USER` / `LOADER_BUSY` |

> 预检不启动 JVM；Java 探测只跑 `java -version` 类命令（见 08-java-scan）。**[事实/推断]**

### 4.3 `version.json` 校验清单

- `mainClass` 非空；`inheritsFrom` 链能解析到原版；无环。
- `libraries[]` 每项 `name` 合法；`downloads.artifact` 要么 `url+sha1+size`，要么 `url==""` + `path`（生成物）。
- `downloads.artifact.url` 为空但 manifest 没有对应项 → `UNKNOWN_GENERATED_ARTIFACT`，**不要尝试下载**。
- `downloads` 缺失 → 按 Maven 坐标解析（可能是远程；也可能是旧格式），标记 `libraryWithoutDownloads`。
- `arguments`/`minecraftArguments` 结构与类型正确；`logging.client.file` 若有，url/sha1 必须成对。
- 版本 `id` 与 `inheritsFrom` 不能自引用；`javaVersion.majorVersion` 缺失时从父版本继承。
- 生成物路径必须是相对路径，且在 `shared/libraries` 或 manifest 允许的根内；拒绝 `..`、绝对路径、symlink 祖先。

### 4.4 空 URL / 生成物检测伪代码

```text
for lib in effective.libraries:
    art = lib.downloads?.artifact
    if art == null:
        # 可能是 Maven 坐标库；不是生成物，除非 manifest 显式标记
        classify = remote_or_maven
    else if art.url is missing or art.url == "":
        # 生成物候选
        m = manifest.findByPath(art.path) or manifest.findByMaven(lib.name)
        if m == null: problems += UNKNOWN_GENERATED_ARTIFACT
        else: verify(m)
    else:
        classify = remote
        if deep: verify hash; else: check exists/size
```

并额外遍历 `manifest.generatedArtifacts`，因为 Forge 1.20.1/NeoForge 的 `PATCHED` 可能不在 `libraries` 里。**[推断]**

### 4.5 校验等级

| 等级 | 范围 | 使用场景 | 成本 |
|---|---|---|---|
| `fast` | 生成物全量 SHA-1；版本 JSON/ profile 全量 SHA-1；远程库存在+size | 每次 `launch.prepare` | 约 30–100 MB 读，几十到几百 ms |
| `deep` | 所有 classpath jar 全量 SHA-1；所有 manifest 项；`client-extra` | 修复后、手动「验证文件」、崩溃诊断 | 数百 MB–1 GB，秒级 |
| `repair` | 预检 + 必要时重跑 processor | 用户点「修复」/启动前自动修复 | 取决于处理器数量，通常 < 2 min |

> 默认 `fast`；不要每次启动对所有 libraries 做全量哈希，否则 10k 资源/几百 MB 库会把启动拖慢。**[推断]** 生成物才是高风险、小集合，值得每次全量。

### 4.6 预检结果

```json
{
  "status": "ready | repair-required | blocked",
  "manifestId": "sha256:...",
  "verifiedAt": "2026-10-21T00:00:00Z",
  "blocking": [
    {
      "code": "ARTIFACT_MISSING",
      "key": "PATCHED",
      "path": "libraries/net/minecraftforge/forge/1.21.1-52.1.0/forge-1.21.1-52.1.0-client.jar",
      "producerIndex": 6,
      "requiredForLaunch": true,
      "repairable": true
    }
  ],
  "warnings": [
    { "code": "HASH_SOURCE_TOFU", "key": "PATCHED", "message": "该 NeoForge 版本没有官方 PATCHED_SHA，使用首次成功生成的哈希" }
  ],
  "repair": {
    "fromProcessor": 6,
    "estimatedProcessors": 1,
    "needsNetwork": false,
    "needsJava": true,
    "installerCached": true
  }
}
```

- `repair-required`：可通过重跑 processor 修复；`blocked`：缺 installer/Java/权限/未知生成物，需用户动作。
- 不要用 `ready` 掩盖 warning；TOFU 必须让诊断可见。**[推断]**

---

## 5. 修复算法

### 5.1 状态机

```
unknown
  └─ verify ──► ready
        │
        └─ repair-required
              └─ acquire-lock ──► ensure-inputs ──► run-processors
                                                        ├─► verify-outputs
                                                        │      ├─ ok ──► commit ──► ready
                                                        │      └─ fail ─► repair-failed
                                                        └─ timeout/cancel ─► repair-required
```

- `repair-failed` 保留已完成且校验通过的产物；下次从最早无效项继续。
- `repairing` 是持久状态（`status.json` + `journal.json`），进程被杀后不能假装 `ready`。

### 5.2 最早 processor 判定

```text
invalid = artifacts where not valid(a)
if invalid.empty: return already-valid

start = min(a.producerIndex for a in invalid where a.producerIndex >= 0)
unknown = invalid.any(a.producerIndex < 0 or a.producerIndexUnknown)
if unknown: start = 0          # 保守：从头重放；valid 的输出会跳过
if invalid.any(a.expected.sha1 == null and a.expected.origin == "tofu"):
    start = min(start, a.producerIndex)  # 没有可信期望值，必须重跑产生它

for p in processors[start..]:
    if sideFilteredOut(p): continue
    if outputsValid(p) and inputsUnchanged(p):
        record skipped; continue
    run(p); verifyOutputs(p); checkpoint()
```

要点：

- **不要只重跑「最后一步」**：`PATCHED` 坏了可能是 `MC_SRG` 也坏了；从最早无效项开始可以覆盖依赖链。
- 如果某个 processor 的输出全部有效、输入哈希未变，可以跳过；但**默认保守重跑**从 `start` 到末尾，避免「上游悄悄变了、下游哈希碰巧没变」。
- NeoForge 没有 `outputs` 时，按 `data`/`args` 推导；推导不完整就把 `start` 提前到 0。**[推断]**

**常见定位**：

| 缺失/损坏项 | 最早重跑 processor | 说明 |
|---|---:|---|
| `MAPPINGS` / `MERGED_MAPPINGS` | 3 / 5 | 1.20.1/NeoForge 的 MCP_DATA/MERGE_MAPPING |
| `MOJMAPS` | 3/4 | 1.21.1 #3；1.20.1/NeoForge #4 |
| `MC_SLIM` / `MC_EXTRA` | 6 | client jarsplitter |
| `MC_SRG` | 8 | ATR |
| `MC_OFF`（1.21.1） | 5 | client ATR |
| `PATCHED` | 9（1.20.1/NeoForge）、6（1.21.1） | binarypatcher |
| `client-extra` 单独损坏 | 6 | 重新拆 extra；不要只补空文件 |

### 5.3 锁

| 锁 | 粒度 | 实现 | 目的 |
|---|---|---|---|
| 实例锁 | `instances/<id>/.archoera/loader.lock` | OS 文件锁（`fs4`/`FileStream` 排他） | 防止同一实例并发 prepare/repair/launch |
| 共享 loader 锁 | `meta/loaders/<key>/.lock` | OS 文件锁 + 进程内 async mutex | 防止两个实例同时写同一 `libraries/` 生成物 |
| 内容库锁 | 按对象 hash | 下载器已有 | 防止重复下载 |

- 锁顺序固定：**实例锁 → 共享 loader 锁**；反向会死锁。
- 获取超时（如 30s）返回 `LOADER_BUSY`，带当前持有者 `pid/startedAt`（不泄露路径）；UI 显示「另一个实例正在修复」。
- 进程崩溃后 OS 自动释放文件锁；`journal.json` 残留由下一次预检识别并清理临时目录。**[推断]**

### 5.4 确保输入

修复前要保证：

1. **installer**：按 manifest 的 coordinate/url/sha1/size 从官方 Maven 下载到内容库；已有且哈希匹配则复用。
2. **install_profile.json / version.json**：从 installer zip 读取；校验原始 SHA-1；解析 `spec`（只支持 1；spec 0 走旧路径）。
3. **原版 client jar**：按 `version.json.downloads.client` 下载；校验 sha1/size；存 `shared/versions/<mc>/<mc>.jar`。
4. **Maven 输入**：`install_profile.libraries` 中每个库；校验 sha1/size；缺失则生成 DownloadPlan（C# 产出，Rust 执行）。
5. **BINPATCH**：从 installer 抽取 `data/client.lzma` 到 staging；用 zip entry 的 CRC/size 做抽取校验，再记录 SHA-1。
6. **shim**（1.21.1）：按 `install_profile.path`/libraries 下载；它是输入，不是生成物。
7. **Mojang mappings**：默认让 `DOWNLOAD_MOJMAPS` 处理器下载；只有在参数**不含 `--sanitize`**、且能找到可信的输出哈希时，才可由 C# 直接下 `version.downloads.client_mappings` 替代。Forge 1.21.1 的该处理器带 `--sanitize`，原始 `client_mappings`（`2244b6...`）与 `MOJMAPS_SHA`（`2a8064...`）不同，不能直接拿原始文件冒充处理器输出。**[事实/推断]**

> 如果离线且输入缺失：`REPAIR_OFFLINE`；不要用「同名但 hash 不同」的文件凑数。**[推断]**

### 5.5 运行 processors

**argv 形状**：

```text
<java> -Xmx512m -Dfile.encoding=UTF-8 \
  -cp <processor.jar><sep><classpath...> \
  <Main-Class> <args...>
```

- `Main-Class` 从 processor jar 的 `META-INF/MANIFEST.MF` 读取；缺失 → `PROCESSOR_MAIN_CLASS_MISSING`。**[事实]** SJMCL 也这么做（02 §4.2），但我们是独立实现。
- **绝对不使用 shell**：argv 数组；路径含空格/中文/引号都作为单个参数传递。
- 清空 `JAVA_TOOL_OPTIONS`、`_JAVA_OPTIONS`、`JDK_JAVA_OPTIONS`、`CLASSPATH`、`MODULE_PATH`；设置工作目录为 staging；禁用交互。
- `-cp` 顺序：processor jar + `classpath`（按 install_profile 顺序）。不要用通配符展开。
- `-Xmx512m` 只是默认；大版本可调，但不能把启动器的内存参数原样带进去。**[推断]**

**占位符**：

| 占位符 | 值 |
|---|---|
| `{SIDE}` | `client` / `server` |
| `{MINECRAFT_JAR}` | 原版 client/server jar 绝对路径 |
| `{INSTALLER}` | installer 绝对路径 |
| `{ROOT}` | 本次 staging 根（不是活动实例） |
| `{LIBRARY_DIR}` | staging 内 `libraries/`（输入用硬链接/复制，输出写这里） |
| `{BINPATCH}` | 抽取后的 `client.lzma` 路径 |
| 其他 `data` key | 按 Maven 坐标/installer 路径/字面量解析 |
| `[group:artifact:version[:classifier][@ext]]` | 参数里的方括号 Maven 引用；解析成 `libraries/` 绝对路径，不要当字面量传给 processor |

**staging 与提交**：

```
shared/.loader-staging/<txid>/
  libraries/        # 输入硬链接（同卷）或复制；输出新文件
  data/client.lzma
  logs/
```

- 不要直接让 processor 写活动 `shared/libraries`：崩溃会留下半文件。**[推断]**
- 输入硬链接/复制前记录 hash；processor 结束后若输入 hash 变化，视为越界写，回滚该次修复（不要静默接受被改的共享库）。**[推断]**
- 处理器只应写 `{ROOT}` 下路径；如果参数出现 `{ROOT}` 之外的绝对路径，拒绝（`UNSAFE_PROCESSOR_OUTPUT`）。
- 输出提交：校验通过后 `fsync` → 原子 `rename` 到 `shared/libraries` 目标；同卷才 rename，跨卷复制+校验。
- 提交前若目标已存在：同 hash 直接复用；不同 hash 且不是本 manifest 生成 → 移入 quarantine，不覆盖用户文件。

**超时/取消**：

| 项 | 默认 | 处理 |
|---|---|---|
| 单 processor | 5 min | `PROCESSOR_TIMEOUT` |
| 整次修复 | 20 min | `REPAIR_TIMEOUT` |
| 无输出增长 | 2 min | 记录 warning；不自动杀（有些处理器长时间静默） |
| 取消 | 用户 | 杀进程树；保留已完成输出；state 回 `repair-required` |

- Windows 用 Job Object（`KILL_ON_JOB_CLOSE`）确保子进程树一起死；Unix 用 `setsid` 建进程组，超时 `kill(-pgid, SIGKILL)`。**[推断]**
- stdout/stderr 环形缓冲 64 KB；完整日志写 `logs/processor-<n>.log`；UI 不显示完整 classpath（可能含用户名）。**[推断]**

### 5.6 每个 processor 后的输出校验

| 情况 | 动作 |
|---|---|
| `outputs` 声明了 key→sha1 | 按 key 解析路径；存在、size（若给出）、SHA-1 全匹配 |
| 只有 `outputs` key、无 sha1 | 计算 SHA-1；与 manifest 的 expected 比较；无 expected 则记录 TOFU |
| 没有 `outputs` | 用推导的 `producedKeys`；至少校验存在、非空、`.jar` 可读 zip；计算 SHA-1 |
| `requiredForLaunch=false` 的中间物 | 仍校验（后续 processor 要用）；失败同样中止 |
| 输出是文本 mappings | 非空、行数/大小合理；不做 JSON 解析 |
| 输出是 jar | `ZipFile` 能打开 central directory；可选检查 `META-INF/MANIFEST.MF` |
| 退出码非 0 | `PROCESSOR_EXIT_NONZERO`；保留 stdout/stderr；不继续 |

**绝不以「文件存在」代替哈希**；0 字节文件、半截 jar 都会在启动时炸。**[推断]**

### 5.7 记录与幂等

- 每个 processor 成功后写 `journal.json`：`{processorIndex, outputHashes, completedAt}`；提交点写 `status.json` 和 receipt。
- 幂等：
  - 所有 `requiredForLaunch` 生成物有效 → `repair` 直接返回 `already-valid`，**不启动 Java**。
  - 部分有效 → 从最早无效 processor 开始；有效输出跳过。
  - 重复调用 `loader.repair` 同一 manifest → 返回同一结果（`planId`/`manifestId` 幂等）。
- 取消/崩溃：
  - `journal.state=repairing` + staging 目录存在 → 下次启动清理 staging，重新校验；已提交到 shared 的有效输出保留。
  - 不要因为 journal 残留就判定实例损坏；先验证实际文件。**[推断]**

### 5.8 失败状态与用户可见错误

| 状态/码 | 含义 | 可重试 | 用户动作 |
|---|---|---|---|
| `ARTIFACT_MISSING` | 生成物缺失 | 是（repair） | 「修复加载器文件」 |
| `ARTIFACT_HASH_MISMATCH` | 生成物被改写/损坏 | 是 | 修复；若反复失败看日志/杀软 |
| `ARTIFACT_SIZE_MISMATCH` | 截断/0 字节 | 是 | 修复 |
| `VERSION_JSON_INVALID` | 版本 JSON 结构坏 | 否（需重装） | 重新安装加载器 |
| `UNKNOWN_GENERATED_ARTIFACT` | 空 URL 库没有 manifest 映射 | 否（需重装/更新启动器） | 重新安装加载器；报告样本 |
| `LAUNCH_ARTIFACT_MISSING` | receipt 绑定的启动 artifact 不在 | 是 | 修复 |
| `LAUNCH_BINDING_MISMATCH` | receipt 与 `LaunchPlan` 的启动 artifact/路径不一致 | 是 | 修复/重装 |
| `INSTALLER_MISSING` / `INSTALLER_HASH_MISMATCH` | installer 缺失/被替换 | 是（下载） | 联网重试或手动选择官方 installer |
| `INPUT_MISSING` | 原版 jar/mappings/库缺失 | 是（下载） | 联网重试 |
| `JAVA_MISSING` / `JAVA_VERSION_MISMATCH` | 无 Java/版本不对 | 否 | 选择/安装 Java（如 21） |
| `PROCESSOR_START_FAILED` | 无法 spawn | 视情况 | 检查 Java/权限/杀软 |
| `PROCESSOR_EXIT_NONZERO` | 处理器失败 | 是 | 重试；查看 processor 日志 |
| `PROCESSOR_TIMEOUT` | 超时 | 是 | 重试；检查磁盘/网络/杀软 |
| `OUTPUT_MISSING` / `OUTPUT_HASH_MISMATCH` | 退出 0 但输出不对 | 是 | 重试；仍失败则重装 |
| `OUTPUT_NOT_A_JAR` | 输出不是可读 zip | 是 | 重试/重装 |
| `LOCK_TIMEOUT` / `LOADER_BUSY` | 另一实例在修 | 是（等待） | 稍后重试 |
| `DISK_FULL` / `PERMISSION_DENIED` | 磁盘/权限 | 否（先处理） | 清盘/换目录/修权限 |
| `REPAIR_OFFLINE` | 离线且缺输入 | 是（联网） | 连接网络 |
| `PROFILE_SPEC_UNSUPPORTED` | 未知 spec | 否 | 更新启动器 |
| `PATH_OCCUPIED_BY_USER` | 目标路径是用户文件 | 否 | 移动/备份后重试 |

**文案原则**：说「哪个加载器文件不完整、将用官方安装器重新生成」，不说 classpath、不说「删除实例」。日志里保留 processor index、退出码、stdout/stderr 末尾、期望/实际 hash 前 8 位。

### 5.9 不删用户文件、不碰第三方 client jar

- 修复只写 manifest 声明的生成物路径、staging、logs、quarantine；**不递归删除** `libraries/`、`versions/`、实例目录。
- 目标路径已有文件但 hash 不符：
  - 若 manifest 记录这是生成物 → 先移到 `quarantine/<txid>/`，再写新文件；保留证据。
  - 若是用户文件（不在 manifest）→ `PATH_OCCUPIED_BY_USER`，不覆盖。
- **不下载/不使用第三方预生成 `-client.jar`**：
  - 官方 Maven 404；第三方包含 Mojang 版权 class，公开分发风险高（02 §2.5）。
  - 用户手动放入的 `-client.jar` 标记 `untrusted`，默认不用于启动；如未来提供「高级：信任本地文件」，必须显式确认并记录审计，不作为默认路径。
- 不修改 `.minecraft` 里的用户 mod/config/saves；加载器修复与实例内容解耦。**[推断]**

---

## 6. 启动集成

### 6.1 `launch.prepare` 的返回

`launch.prepare` 负责构建/预检 `LaunchPlan`，**不启动游戏 JVM**（架构 10 §8.6/§12.6）。建议返回：

```json
{
  "status": "ready | repair-required | blocked",
  "instanceId": "01J...",
  "manifestId": "sha256:...",
  "readinessToken": "opaque-short-lived",
  "launchPlan": null,
  "problems": [],
  "repair": {
    "fromProcessor": 6,
    "estimatedProcessors": 1,
    "needsNetwork": false,
    "needsJava": true
  }
}
```

- `ready`：所有 `requiredForLaunch` 通过；返回完整 `LaunchPlan`（classpath、mainClass、args、natives、logging）。
- `repair-required`：不返回可启动的 `LaunchPlan`，或返回 `launchPlan=null`；UI 调 `loader.repair`，成功后再次 `launch.prepare`。
- `blocked`：需要用户处理（Java/权限/安装器缺失/未知生成物）；`problems[].code` 可直接映射文案。
- 可选 `autoRepair=true`：`launch.start` 在 spawn 前内部调 repair；但**游戏 JVM 必须等 repair 的 verify 通过**。默认建议 UI 两步走，进度更清晰。

### 6.2 推荐时序

```
UI: 点击「启动」
  → launch.prepare
      ├─ ready → launch.start(readinessToken)
      └─ repair-required
            → UI 显示「正在修复加载器（1/3）」
            → loader.repair { manifestId, fromProcessor }
                 → 预检（锁内）→ 确保输入 → 跑 processor → 校验输出 → commit
            → launch.prepare 再验证
            → ready → launch.start
```

- `launch.start` 必须拒绝 `repair-required/blocked`，返回 `LOADER_NOT_READY` + `blocking[]`；**不允许**「先 spawn 再修」。
- 修复期间不阻塞其他实例；但同一 manifest 的 repair 串行。
- 修复用的 Java 与游戏 Java 可以是同一个；Java 版本不匹配在预检就报错，不进入 processor。

### 6.3 事件与 UI 阶段

| stage | 含义 | UI |
|---|---|---|
| `verify` | 预检/哈希 | 「正在验证加载器文件…」 |
| `acquire` | 等锁 | 「另一个实例正在修复，等待…」 |
| `inputs` | 下载/解包 installer、库、原版 jar | 「准备安装器/库（n/m）」 |
| `processor` | 跑 processor | 「生成启动文件（3/6）：binarypatcher」 |
| `verify-output` | 校验输出 | 「校验生成文件…」 |
| `commit` | 原子落位/写 receipt | 「完成」 |
| `failed` | 失败 | 错误码 + 重试/查看日志/重装 |

事件示例：

```json
{
  "type": "loader.repair.progress",
  "instanceId": "01J...",
  "manifestId": "sha256:...",
  "stage": "processor",
  "processorIndex": 6,
  "processorName": "binarypatcher",
  "progress": 0.5,
  "message": "生成 forge-1.21.1-52.1.0-client.jar"
}
```

- 不显示完整 classpath/绝对用户路径；日志详情单独展开。
- 取消按钮调 `loader.repair.cancel`；状态回 `repair-required`，已完成输出保留。

### 6.4 readiness token 与 TOCTOU

- `launch.prepare` 成功后发短时效 `readinessToken`（如 60s），绑定 `instanceId + manifestId + 启动 artifact hashes + java path`。
- `launch.start` 校验 token，并在 spawn 前做一次轻量复核（生成物 mtime/size；必要时重算启动 artifact SHA-1 若文件变了）。
- 若文件在 prepare 与 start 之间被替换/删除 → `LOADER_CHANGED_SINCE_PREPARE`，要求重新 prepare；不带着不确定状态启动。
- 这样避免「prepare 通过、start 时文件已被另一个进程改掉」。**[推断]**

### 6.5 classpath 与 launch jar 绑定

- `LaunchPlan` 必须区分：
  - `clientJar`：本次启动实际使用的游戏/版本 jar（通常是原版 jar）。
  - `launchArtifacts`：bootstrap/加载器需要额外看到的生成物（Forge 1.20.1/NeoForge 的 `PATCHED`；可能通过 `-DignoreList`/模块层/参数传入，不是普通 classpath）。
  - `classpath`：按 rules 过滤后的库；空 URL 库用 manifest 路径替换，不生成下载项。
  - `mainClass`：从 `version.json` 读；不硬编码（1.20.1 与 1.21.1 不同，02 §2.6）。
- 如果 receipt 的 `launchArtifacts` 与 `LaunchPlan` 不一致：`LAUNCH_BINDING_MISMATCH`，先修复/重装，不启动。
- 有效版本缓存 key 加 `manifestId`；修复后 `manifestId` 变化/文件 hash 变化 → 缓存失效，重建 `LaunchPlan`。**[推断]**

---

## 7. 测试

### 7.1 合成 profile + 假 runner

不依赖网络/真实 Java，先测算法：

| # | 用例 | 期望 |
|---:|---|---|
| 1 | 3 个 processor，fake runner 按 argv 写输出；输出哈希匹配 | 顺序执行；每个 output 校验；receipt 完整 |
| 2 | 中间输出缺失（删除 `MC_SRG`） | `start=8`；只重跑 8/9；不重跑 3–6 |
| 3 | 最后输出缺失（删除 `PATCHED`） | `start=9`；重跑后 ready |
| 4 | 输出 hash 不匹配 | 该 processor `OUTPUT_HASH_MISMATCH`；不 commit；坏输出进 quarantine |
| 5 | 退出码 0 但没有写输出 | `OUTPUT_MISSING`；不把退出码当成功 |
| 6 | 退出码 1 / 超时 / 被杀 | 对应错误；已完成输出保留 |
| 7 | 两个输出指向同一路径 / `..` / 绝对路径 | `UNSAFE_PROCESSOR_OUTPUT`；拒绝 |
| 8 | 路径含空格/中文/引号/`;` | argv 正常；无 shell 注入 |
| 9 | 第二次运行全部有效 | 不 spawn fake runner；返回 `already-valid` |
| 10 | 并发两个 repair | 一个跑，另一个等锁；不重复 spawn |
| 11 | 崩溃在 processor 中间 | 下次从最早无效项继续；staging 清理；无半截 manifest |
| 12 | manifest 迁移/缺字段 | 补默认；`manifestId` 重算；旧文件 `.bak` |

### 7.2 真实集成

| # | 用例 | 期望 |
|---:|---|---|
| 1 | Forge 1.20.1-47.2.0 全新安装 | 6 个 client processor；`PATCHED` 路径/哈希正确；launch.prepare ready |
| 2 | Forge 1.21.1-52.1.0 全新安装 | 3 个 client processor；空 URL 库被识别为生成物；不尝试下载（404） |
| 3 | NeoForge 21.1.209 全新安装 | 6 个 client processor；无 `outputs`/`PATCHED_SHA` 时仍能生成/记录 TOFU |
| 4 | 删除 `-client.jar` | repair-required → 修复 → ready；游戏 JVM 未提前启动 |
| 5 | 把 `-client.jar` 截断 1 字节 / 替换为文本 | hash/size 失败；修复成功 |
| 6 | 删除 `client-extra` / `MC_SRG` / `MC_OFF` | 从最早需要的 processor 重跑；不误删其他文件 |
| 7 | 删除/改写 installer | `INSTALLER_MISSING`/`HASH_MISMATCH`；重新下载官方 installer |
| 8 | 删除 `version.json` 或改 `mainClass` | `VERSION_JSON_INVALID`；不启动 |
| 9 | 无 Java / Java 17 跑 1.21.1 | `JAVA_MISSING`/`JAVA_VERSION_MISMATCH`；不进入 processor |
| 10 | 路径 `D:\我的 游戏\inst` / `/tmp/我的 游戏` | argv 正常；无引号/编码问题 |
| 11 | 两个实例同时修复同一加载器 | 锁串行；生成物一致；无重复 JVM |
| 12 | 断网但 installer/输入已缓存 | 修复成功；不请求网络 |
| 13 | 断网且 installer 缺失 | `REPAIR_OFFLINE`；可读提示 |
| 14 | 修复中 kill -9 | 重启后 journal 恢复；有效输出跳过；无半成品被当成功 |
| 15 | 磁盘满 / 只读目录 / 文件被占用 | 明确错误；不破坏已有文件；不清空实例 |
| 16 | 服务端侧（`side=server`） | 只跑 server processors；不跑 client jarsplitter |
| 17 | 切换 Forge → NeoForge | 新 manifest；旧 receipt superseded；旧生成物可 GC |
| 18 | 移动数据根 | 相对路径生效；receipt 仍能解析；无绝对路径错误 |
| 19 | 旧实例只有重写 `install_profile.json` | 能重建则重建；否则 `legacy-unverified` + 提示重装 |
| 20 | 第三方预生成 `-client.jar` 放入 libraries | 标 `untrusted`；默认不使用；不静默覆盖 |

### 7.3 验收标准

- 三个真实加载器：删除 `-client.jar` 后一次修复成功；第二次 repair 不启 JVM。
- `launch.prepare` 在 `repair-required` 时绝不 spawn 游戏 JVM；修复后 `launch.start` 才启动。
- manifest 中 0 个绝对路径；0 个空 URL 被当作下载项；所有 `requiredForLaunch` 有 hash（TOFU 也算记录）。
- 修复失败不删除用户文件；坏输出进 quarantine；日志含 processor index/退出码/期望与实际 hash。
- 冷启动 `fast` 预检 p95 < 500 ms（仅生成物全量哈希；不含首次下载）。**[推断，需实测]**
- Windows/Linux 路径含空格/中文通过；无 shell 拼接。

---

## 8. 参考实现（只读，不抄代码）

| 项目 | 许可证 | 观察到的行为 | 我们采用/不采用 |
|---|---|---|---|
| **SJMCL** | GPL-3.0 + 附加条款 | `execute_processors` 逐个 `java -cp`；非 0 退出失败；不校验 `outputs`；重写后的 `install_profile.json` 写进实例且含绝对路径；失败靠重启 | 采用「只跑 processors、不跑 installer 主类」；**不采用**「退出码即成功/绝对路径」 |
| **HMCL** | GPL-3.0 | `ForgeNewInstallTask`；`outputs` 预检+后检；可续跑；DOWNLOAD_MOJMAPS 本地化 | 采用「预检+后检+续跑」；独立实现 |
| **Prism** | GPL-3.0 | meta 组件 + installer；首启由 ForgeWrapper 调 installer 跑 processors | 采用「延迟到有 Java 时再跑」；**不采用**「把错误延后到首启」 |
| **ATLauncher** | GPL-3.0 | 启动器自身 JVM 内 `URLClassLoader` 反射调 processor 主类 | 不采用同进程；避免带崩启动器 |
| **CmlLib.Core** | MIT | 拆分包 `CmlLib.Core.Installer.Forge`；spawn Java；实现干净 | 设计参考；可代码级借鉴（保留 MIT 许可） |
| **ProjBobcat** | MIT | `HighVersionForgeInstaller`；spawn Java；功能全；字符串拼 `Arguments` 有引号风险 | 采用「功能分层」；**不采用**字符串拼参数 |
| **MinecraftLaunch** | MIT | 现代 async .NET 核心；Java 搜索/启动参数模型 | 作为 .NET 异步/模型参考；不复制代码 |
| **Forge Installer** | LGPL-2.1-only（源码头） | `PostProcessors` 校验 outputs，不匹配删除并失败；`SimpleInstaller` 支持 `--installClient`；installer 自身在进程内用 `URLClassLoader` 调 processor | 采用「输出校验」思想；不复制代码、不调用 installer 主类、不重分发 |

> GPL/AGPL 项目只读行为，不复制代码、注释、文案或资产。MIT 项目如复用代码，保留版权与许可声明；本设计默认独立实现。

---

## 9. 未验证 / 开放问题

1. **Forge 1.20.1/NeoForge 21.1 的 `PATCHED` 最终消费方式**：Forge Installer 2.x 源码确认 installer 只把原版 client 下到 `versions/<mc>/<mc>.jar`，processor 输出留在 `libraries/...-client.jar`，不复制成版本 jar。**[事实]** 但 bootstrap 究竟如何发现它（`-DignoreList` + 模块层、BootstrapLauncher 自动扫描、还是启动参数）尚未实机确认；这决定 `LaunchPlan` 是把它放 `launchArtifacts` 还是 `classpath`。**[未验证]** 写代码前必须在 Linux/Windows 各装一次并观察实际启动命令/模块层。
2. **`client-extra` 是否在所有版本启动必需**：1.20.1/NeoForge 有 jarsplitter；1.21.1 没有。若资源缺失是否只影响贴图/语言，需实测。**[未验证]**
3. **NeoForge 无 `PATCHED_SHA` 的验收策略**：TOFU 是否足够？是否需要维护一份「已知哈希」签名清单？需要产品/安全决策。**[未验证]**
4. **NeoForge 无 `outputs` 的推导可靠性**：从 args 扫 `--output` 可能漏掉隐式输出；保守从头重跑的成本是否可接受？**[未验证]**
5. **installer 官方字节 vs BMCLAPI 镜像字节**：Forge 1.20.1 / 1.21.1 样本已通过代理从官方 Maven 下载并复核 SHA-1；NeoForge 官方 Maven 在当前网络下 TLS 不稳定，样本来自 BMCLAPI，字节 SHA-1 与官方记录一致。生产安装仍应记录实际字节 SHA-1，并优先官方源；镜像只作为可选回退。**[事实/已复核]**
6. **shim 是否参与启动**：它出现在 `install_profile.path`/libraries，但不在 `version.json.libraries`；可能只是 installer 用。若启动需要，manifest 要把它列为 input/required。**[未验证]**
7. **1.20.1 的 `PATCHED` 没有 size**：只有 SHA-1；首次修复后需记录 size。**[事实/推断]**
8. **Windows 文件锁/杀软对 staging→shared rename 的影响**：需要在 Windows 实机跑并发/占用用例。**[未验证]**
9. **跨平台路径**：`{ROOT}` 含中文/空格、`\\?\` 前缀传给 Java 的兼容性；需要在 Windows 实测。**[未验证]**
10. **Forge 1.13–1.19 的中间版本**：本清单只覆盖 1.20.1/1.21.1/NeoForge 21.1；更早版本可能只有 3–6 个 processor，且 spec 0 分支不同。**[未验证]**
11. **Java runtime 版本**：1.20.5+ 需要 21；1.20.1 需要 17；processor 与游戏是否总能共用同一 JRE，需按版本矩阵验证。**[未验证]**
12. **取消/超时的进程树 kill**：Windows Job Object 与 Unix 进程组在 Java 孙进程上的覆盖度需实测。**[未验证]**

---

## 10. 参考链接

| 内容 | 链接 |
|---|---|
| Forge installer / Maven | https://maven.minecraftforge.net/net/minecraftforge/forge/ |
| NeoForge Maven | https://maven.neoforged.net/releases/net/neoforged/neoforge/ |
| Forge Installer 源码 | https://github.com/MinecraftForge/Installer |
| ForgeWrapper（Prism 首启方案） | https://github.com/ZekerZhayard/ForgeWrapper |
| Prism meta 组件 | https://meta.prismlauncher.org/v1/net.minecraftforge/52.1.0.json |
| SJMCL（GPL，只读行为） | https://github.com/UNIkeEN/SJMCL |
| HMCL（GPL，只读行为） | https://github.com/HMCL-dev/HMCL |
| CmlLib.Core（MIT） | https://github.com/CmlLib/CmlLib.Core |
| ProjBobcat（MIT） | https://github.com/Corona-Studio/ProjBobcat |
| MinecraftLaunch（MIT） | https://github.com/Lunova-Studio/MinecraftLaunch |
| 本项目加载器安装预研 | [02-loader-install.md](02-loader-install.md) |
| 本项目下载引擎 | [05-download-engine.md](05-download-engine.md) |
