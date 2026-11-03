# 16 · 无 Java 安装的可行性实测：客户端 processor 已全部原生实现

> 状态：2026-11-01 · Linux x86_64
> 样本：Forge 1.20.1-47.4.18、NeoForge 21.1.248、Minecraft 1.21.1 client
> 目标：回答“能否用原生实现模拟一个小 Java 环境，在没装 Java 的机器上完成 Forge/NeoForge 安装”。
> 边界：只依据公开格式、字节码行为与实测输出；不复制任何 LGPL 代码。

---

## 0. 结论

**主要步骤已经原生接管；剩余 processor 明确要求 JVM，并在下载前警告。**

- 客户端实际只跑 3 个 processor（Forge 1.21.1）：`DOWNLOAD_MOJMAPS` → `ART` → `binarypatcher`。
- 1.20.1 / NeoForge 21.1 还多 `MCP_DATA`、`MERGE_MAPPING`、`jarsplitter`；**`BUNDLER_EXTRACT` 是 server 侧，客户端不跑**。
- `MCP_DATA`、`MERGE_MAPPING`、`jarsplitter`、`ART`、`binarypatcher` 已由 C# 原生实现，
  并在真实产物上**逐字节一致**（见 §3.4）。
- `ART` 不能用“语义等价的重映射”替代：补丁以 ART 产物的**类名 + Adler32 + GDIFF 绝对偏移**为前提，
  必须**逐字节一致**；本仓库的 `ArtRemapper` + 四个 fixer 已对齐 0.1.22 / 1.0.6 / 2.0.3。
- 其余步骤（`DOWNLOAD_MOJMAPS`、`PROCESS_MINECRAFT_JAR`、`binarypatcher 1.3.4 --store/--marker`、
  `ART --reverse`）不做原生实现，遇到时要求 JVM。
- `loader.plan` 会返回 `requiresJava` 与 `javaRequiredSteps`；Dart 在下载前判断，
  无 Java 时直接提示“此加载器需要 Java 才能安装”。Fabric/Quilt 不探测 Java。
- 实测一个 **39 MB 的 jlink 运行时**（`java.base` + `jdk.crypto.ec`）就能跑通全部 processor，
  输出与完整 JDK 逐字节一致——这是需要 Java 时的最小形态。

---

## 1. 客户端真正执行的 processor（按 `sides` 过滤）

1.21.1 的 `install_profile.json` 共 7 个 processor，其中 0–2 是 `["server"]`：

| # | processor | sides | 作用 |
|---|---|---|---|
| 0 | `EXTRACT_FILES` | server | 解 `data/run.sh` 等 |
| 1 | `BUNDLER_EXTRACT --libraries` | server | 解 server bundler 的内嵌库 |
| 2 | `BUNDLER_EXTRACT --jar-only` | server | 取 server 内层 jar |
| 3 | `DOWNLOAD_MOJMAPS` | 双侧 | 下载官方映射，转 TSRG |
| 4 | `ART` | server | server 侧重映射 |
| 5 | `ART` | client | 客户端重映射（`--reverse`） |
| 6 | `binarypatcher` | 双侧 | 应用 `data/client.lzma` |

1.20.1 与 NeoForge 21.1 的客户端额外跑 `MCP_DATA`、`MERGE_MAPPING`、`jarsplitter`，但同样**没有** `EXTRACT_FILES` / `BUNDLER_EXTRACT`。

> 现有 `ForgeArtifactPlanner.RunsOnClient` 已按 `sides` 过滤，方向正确。

---

## 2. 各步骤的原生可复刻性

| 步骤 | 实际工作 | 原生实现难度 | 状态 |
|---|---|---|---|
| `EXTRACT_FILES` | 按参数从 installer zip 复制条目，`--exec` 置可执行位，`--optional` 已存在则跳过 | 低 | 未实现（server 侧，回退 Java） |
| `BUNDLER_EXTRACT` | 读 `META-INF/MANIFEST.MF` 的 `Bundler-Format=1.0`，按 `versions.list` / `libraries.list` 取条目 | 低 | 未实现（server 侧，回退 Java） |
| `MCP_DATA` | 读 mcp zip 的 `config.json`，把 `data.<key>` 指向的条目原样写出；`/` 结尾走目录解包 | 低 | **已实现**，单文件/目录两种模式 |
| `DOWNLOAD_MOJMAPS` | 下 Mojang `client.txt`，ProGuard → TSRG；`--sanitize` 后方向为 **official→obf** | 中 | 未实现（回退 Java） |
| `MERGE_MAPPING` | TSRG 合并，`--reverse-right` 先把右侧反转再串接 | 中 | **已实现**，含 Java HashMap 重载顺序 |
| `jarsplitter` | 按映射里的类名把 class 分到 slim，其余进 extra，可选 data | 低 | **已实现** |
| `ART` | ASM `ClassReader → Remapper → ClassWriter(0)`，另加 record/annotation/source/signature 修正 | **高** | **已实现**，三个版本逐字节复现 |
| `binarypatcher` | `.lzma` 解出 patch jar；每条 patch = 名称 + Adler32 + GDIFF delta | **低** | **已实现**，整文件逐字节一致 |

### 2.1 补丁格式（实测）

`data/client.lzma` 是 LZMA-alone 流，解压后是 zip。每个 `*.binpatch`：

```
u8   version = 1
UTF  obf      // 与 clean 里的类名对应（ART 产物，官方名）
UTF  srg
bool exists   // clean 中是否存在
i32  adler32  // 仅 exists=true
i32  length
u8[] data     // GDIFF；exists=false 时是"从空文件到 dirty"的 delta
```

GDIFF 头是 `D1 FF D1 FF 04`（magic + 1 字节版本，**没有长度字段**），随后是
`ADD`（0–248）与 `COPY`（249–255）指令。

### 2.2 补丁规模（1.21.1）

| 指标 | 值 |
|---|---|
| patch 数 | 1365（1355 exists + 10 新增） |
| ADD 字节 | 6,543,897 |
| COPY 字节 | 5,873,050（47.3%） |
| `--data` | 保留非 class 条目并清理 manifest 摘要 |
| `--unpatched` | 未被补丁的 class 原样复制 |

---

## 3. 实测复现链

在本机用 `/opt/java/.../jdk-21` 只做**参考输出**，最终产物仍按无 Java 路径验证。

```text
client-1.21.1.jar
  sha1 30c73b1c5da787909b2f73340419fdf13b9def88   == install_profile MC_UNPACKED_SHA

DOWNLOAD_MOJMAPS --sanitize --version 1.21.1 --side client
  → mojmaps.tsrg（official→obf，6,701,242 B）
  sha1 2a8064cf9359dd03125bc6f4aa99715dbbf50492   == install_profile MOJMAPS_SHA

ART 1.0.6 --reverse --ann-fix --ids-fix --src-fix --record-fix --strip-sigs
  → MC_OFF.jar
  sha1 fd32ce6ddf1bc49a9c1a00f273cbcb2aa7ac9d18   == install_profile MC_OFF_SHA

binarypatcher 1.2.0 --data --unpatched
  → PATCHED.jar
  sha1 2d2537d306a05aeac6ed5fa9bf176ffacb78cb4f   == install_profile PATCHED_SHA
```

三个哈希与 installer 自带的 `*_SHA` 全部一致，说明对流水线的理解完整。

### 3.1 原生 ART（GraalVM native-image）

把 ART 1.0.6 + jopt-simple + ASM 9.5 + srgutils 0.5.1 直接 `native-image`，
补上 `java.io.File` / `Integer` / 配置枚举的反射注册后，得到 20 MB 的可执行文件：

```text
env -i PATH=/usr/bin:/bin ./fart-native ... --reverse
  sha1 fd32ce6ddf1bc49a9c1a00f273cbcb2aa7ac9d18   // 与 JVM ART 完全一致
```

| 运行方式 | 耗时 |
|---|---|
| JVM（JDK 21） | 3.13 s |
| 原生镜像 | 2.77 s |

### 3.2 原生 binarypatcher

用纯 Python（研究脚本，未入库）实现 LZMA + GDIFF + 补丁头解析后，
对 `MC_OFF.jar` 应用同一个 `client.lzma`，产出的 23,526 个条目与 Java 版
`PATCHED.jar` **逐条一致**（0 mismatch / 0 extra）。

这证明 `binarypatcher` 只是 zip + LZMA + 一个约 150 行的 delta 解释器，
不存在“必须跑 JVM”的理由。

### 3.3 “小 Java 环境”：jlink 只保留 `java.base` + `jdk.crypto.ec`

不需要完整 JDK/JRE。用 `jlink` 裁到最小：

```bash
jlink --add-modules java.base,jdk.crypto.ec \
      --strip-debug --no-header-files --no-man-pages --compress=zip-6 \
      --output <runtime>
```

| 项目 | 结果 |
|---|---|
| 体积 | **39 MB**（JDK 21，`--compress=zip-6`） |
| `DOWNLOAD_MOJMAPS` | 正常，产物 sha1 `2a8064cf…` 与 installer 声明一致 |
| `ART` | 正常，`MC_OFF` sha1 `fd32ce6d…` 与 JVM 完全一致 |
| `binarypatcher` | 正常，`PATCHED` sha1 `2d2537d3…` 一致 |

注意必须带 `jdk.crypto.ec`，否则 HTTPS 握手会 `handshake_failure`。
换句话说，所谓“小 Java 环境”可以是一个 39 MB 的裁剪运行时，而不是完整 JRE。

---

### 3.4 C# 原生实现与逐字节验证（2026-11-01）

`NativeProcessorRunner` 按 `MainClass` 分派，未覆盖的步骤回退 `ForgeProcessorRunner`：

| Handler | 接管 | 验证产物 |
|---|---|---|
| `McpDataProcessorHandler` | `installertools --task MCP_DATA` | Forge/NeoForge `-mappings.txt` 逐字节一致 |
| `MergeMappingProcessorHandler` + `MappingMerge` | `--task MERGE_MAPPING` | 两个 `-mappings-merged.txt` 逐字节一致（含 35 320 条重载的 Java HashMap tie-break） |
| `JarSplitterProcessorHandler` | `jarsplitter` | 四个 `-slim.jar` / `-extra.jar` 逐字节一致（Forge 的 `MC_SLIM_SHA` / `MC_EXTRA_SHA` 直接命中） |
| `ArtProcessorHandler` + `ArtRemapper` | `ART` | Forge 1.20.1 / NeoForge 1.21.1 的 `-srg.jar` class 条目与顺序逐字节一致（不生成 fernflower 提示文件） |
| `BinaryPatcherProcessorHandler` + `GDIFF` | `binarypatcher` | `forge-1.20.1-47.4.18-client.jar`、`neoforge-21.1.248-client.jar` 整文件逐字节一致 |

实现要点：

- `.lzma` 是 LZMA-alone 流（13 字节头 + EOS 结尾），用 `Faithlife.Lzma`（MIT、无依赖、纯托管）解码。
- `.binpatch` 为大端：`version=1`、modified UTF-8 的 obf/srg 名、`exists`、可选 Adler-32、GDIFF 长度与载荷。
  `dataLen == 0` 表示删除条目；`exists=false` 表示从空源创建。
- GDIFF 只使用 ADD 与 COPY（COPY 偏移为绝对值），没有 RUN。
- ZIP 写出必须手写：version 20、flags `0x0808`、固定 1989-11-26、局部头 crc/size 置零、
  `PK\x07\x08` 数据描述符、deflate level 6。`JarWriter` 复刻了这套布局。
- 压缩必须与 zlib level 6 完全一致：Linux 走系统 `libz.so.1` P/Invoke（1.3.x）；
  其他平台回退 `Zlib.Portable`（zlib 许可、无依赖），产物语义正确但**不保证逐字节一致**。
  已确认 .NET 自带 `DeflateStream`（zlib-ng）与托管 zlib 1.2.x 移植都不匹配官方输出。
- `binarypatcher` 1.1.1（Forge 1.20.1）用 Java `HashMap` 顺序追加新建条目；1.2.0
  （Forge 1.21.1 / 1.21.8）与 2.1.2（NeoForge 21.1）改为按名字排序。该顺序会改变整文件哈希，
  现按处理器 jar 坐标的版本区分（≥ 1.2.0 排序），未知坐标回退到 NeoForge 判定。
  用官方工具复核：1.1.1 的 `d8e1d916…`、1.2.0 的 `28f7acaa…` / `37543bb2…`、
  2.1.2 的 NeoForge 客户端 jar 均逐字节一致。

### 3.5 Java 17 / Java 25 端到端安装与切换（2026-11-01）

用真实网络与本机共享根 `~/minecraft` 跑完整 `loader.plan → download → commit → repair → verify`：

| 场景 | JVM | 处理器路径 | 结果 |
|---|---|---|---|
| Forge 1.20.1-47.4.20 | 17.0.17 | `DOWNLOAD_MOJMAPS` 走 Java；MCP_DATA / MERGE_MAPPING / jarsplitter / ART / binarypatcher 走原生 | 全链重新生成，`PATCHED_SHA` = `d8e1d916…` 命中 |
| NeoForge 26.3.0.100 | 25.0.1 | `PROCESS_MINECRAFT_JAR`（installertools 4.0.17）走 Java | `verify ok`，patched jar 生成 |
| NeoForge 26.3 → Forge 26.3 | 25.0.1 | Forge 的 `binarypatcher 1.3.4 --store --marker` 走 Java | profile `mainClass` 与 id 正确切换 |
| NeoForge 26.3.0.99 ↔ .100 | 25.0.1 | 每次切换重写 profile；共享产物已存在时 `alreadyValid` | 升级/降级均 `verify ok` |
| Forge 47.4.18 ↔ 47.4.20 | 17.0.17 | 同上 | 升级/降级均 `verify ok` |

### 3.6 全新根 `/tmp` 安装（2026-11-01）

在空的 `ARCHOERACX_HARNESS_ROOT=/tmp/opencode/loader-instances` 上从零安装，确认流程不依赖
已有缓存：

| 场景 | JVM | `requiresJava` / Java 步骤 | 结果 |
|---|---|---|---|
| NeoForge 21.1.248 on 1.21.1 | 21.0.9 | `true`；`DOWNLOAD_MOJMAPS` | `repair` 6 步，`verify ok`，profile `cpw.mods.bootstraplauncher.BootstrapLauncher` |
| Forge 1.21.1-52.1.20 | 21.0.9 | `true`；`DOWNLOAD_MOJMAPS`、`ART --reverse` | `PATCHED_SHA` = `28f7acaa…` 命中，`verify ok` |
| Forge 1.21.8-58.1.26 | 21.0.9 | 同上 | `PATCHED_SHA` = `37543bb2…` 命中，`verify ok`；重复安装 `alreadyValid` |
| Fabric 0.20.1 on 1.21.8 | 不探测 | `false`；无 | 客户端 jar sha1 与 Mojang 一致，profile `net.fabricmc.loader.impl.launch.knot.KnotClient` |

Fabric 的客户端 jar 落在合并 profile 自己的 `versions/<profile id>/` 下，缺失的库只有
macOS / Windows 原生库（Linux 计划不下载），符合预期。

本轮修复：

- **ART 输出顺序**：ART 按映射后的名字排序并补目录条目；`binarypatcher` 会把输入顺序
  写进 `-client.jar`，而 Forge 声明 `PATCHED_SHA`。原生 ART 现在排序并生成目录条目，
  否则全新安装的客户端 jar 哈希不匹配（内容相同、顺序不同）。
- **目录型参数**：NeoForge 26.3 的 `PROCESS_MINECRAFT_JAR` 传入
  `--extract-libraries-to {ROOT}/libraries/`；修复服务的输入检查现在接受已存在的目录。
- **镜像计划**：`loader.plan` 在 `downloadSource=mirror` 时把 Forge/NeoForge Maven
  库映射到 BMCLAPI（此前只有 modpack 安装路径这样做），否则国内网络会卡在
  `maven.neoforged.net`。
- **binarypatcher 新建条目顺序**：1.2.0 起按名字排序，1.1.x 仍是 `HashMap` 顺序；
  旧实现只按 `MainClass` 区分，导致 Forge 1.21.x 的 `PATCHED_SHA` 不匹配。现按 jar 坐标
  版本选择，并新增 Forge 1.21.1 的 `ARCHOERACX_BINARYPATCH_1211FORGE` 夹具回归。
- **Java 预检可读化**：计划阶段处理器 jar 尚未下载，`MainClass` 为空；`loader.plan` 改为
  按 Maven 坐标推断主类，`javaRequiredSteps` 输出
  `1: net.neoforged.installertools.ConsoleTool --task DOWNLOAD_MOJMAPS` 这样的可读条目。

### 产品决策：不再补齐剩余 processor

原生接管只保留已完成的五个主要步骤（MCP_DATA / MERGE_MAPPING / jarsplitter / ART /
binarypatcher）。以下步骤不再做原生实现，遇到时要求 JVM：

- `DOWNLOAD_MOJMAPS`（Forge 1.20.1、Forge 1.21.x、NeoForge 21.1 客户端）。
- `PROCESS_MINECRAFT_JAR`（NeoForge 26.3+）。
- `binarypatcher 1.3.4` 的 `--store` / `--marker`（Forge 26.3+）。
- `ART --reverse`（Forge 1.21.x）等未覆盖参数。

**默认走纯 Java 模式**：安装弹窗勾选“使用官方安装器（纯 Java 模式）”时，
`loader.installJava` 直接运行 `java -jar <installer> --installClient <root>`
（镜像模式追加 `--mirror https://mirrors.ustc.edu.cn/bmclapi/`），再把生成的
`versions/<id>/<id>.json` 与 vanilla 合并写入实例 profile。
它不依赖 processor 覆盖，1.12.2+ / NeoForge 均可；1.7.10 及更早的安装器没有
`--installClient`，会返回 `LOADER_JAVA_INSTALL_UNSUPPORTED`；此时取消勾选后走
原生旧格式路径（`versionInfo` + `install.filePath` universal + `checksums` 旧库）。
取消勾选后才走下面的原生流程。

`loader.plan` 会解析安装器的客户端 processor 链，返回：

```json
{
  "requiresJava": true,
  "javaRequiredSteps": ["1: net.neoforged.installertools.ConsoleTool"]
}
```

Dart 侧先取计划，再决定是否需要 Java：

- `requiresJava=false`（Fabric/Quilt）：完全不探测 Java。
- `requiresJava=true` 且没有可用运行时：**在下载前**直接提示
  “<加载器> 需要 Java 才能完成安装”，并保持安装弹窗打开；不再先下载几百 MiB 再失败。

各加载器当前的实际要求：

| 加载器 | 安装期 Java |
|---|---|
| Fabric / Quilt | 不需要 |
| Forge 1.12.2（spec 0） | 不需要（解压内嵌 universal） |
| Forge ≤ 1.7.10（旧格式） | 不需要（解压 universal + 旧库下载；纯 Java 模式不可用） |
| NeoForge 21.1.x | 需要（`DOWNLOAD_MOJMAPS`） |
| Forge 1.20.1 | 需要（`DOWNLOAD_MOJMAPS`） |
| Forge 1.21.x | 需要（`DOWNLOAD_MOJMAPS --sanitize`、`ART --reverse`） |
| NeoForge 26.3+ | 需要（`PROCESS_MINECRAFT_JAR`） |
| Forge 26.3+ | 需要（`binarypatcher --store/--marker`） |

以上是**原生流程**（取消纯 Java 勾选）的要求；默认的纯 Java 模式统一由官方安装器处理，
Forge/NeoForge 一律需要 Java，Fabric/Quilt 仍然不需要。

---

## 4. 为什么 ART 不能省

假设我们自己写一个“语义正确”的 obf→official 重映射，结果仍然不可用：

1. **键对不上**：Patcher 以 clean jar 的条目名查补丁。原始 client jar 是 `fgi.class`，
   补丁键是 `com/mojang/.../Minecraft.class`；不先重映射，补丁根本匹配不到。
2. **校验过不去**：即使名字对了，`Patch.checksum` 是 clean 类字节的 Adler32；
   我们的输出与 ART 在常量池顺序、属性顺序上不同，哈希立刻失败。
3. **GDIFF 偏移失效**：近一半输出字节来自 `COPY(offset, length)`，offset 是
   clean 文件里的**绝对字节偏移**；类结构一旦重排，复制出来的就是错误字节。

因此“重映射结果语义正确”不够，必须是 **ART 的逐字节输出**。

这就是为什么 `ForgeAutoRenamingTool` 用 `new ClassWriter(0)` 重建类：
它不保留输入的常量池布局，而是按 ASM 的访问顺序重新分配。要复刻就得复刻
ASM 的 `ClassReader` / `ClassWriter` / `ClassRemapper` 以及 ART 的几个 fixer，
不是几百行能解决的。

实测证据（`TickPriority`）：ART 输出的常量池第 6 项是 `SourceFile`，
而 `SourceFile` 在 class 文件里位于末尾——说明 ASM 不是按文件顺序，
而是按自己的 canonical visit order 重放（source → nest → inner → record →
field → method）。方法名/描述符也按方法顺序逐个插入，代码里的引用随指令插入。
所以“保留原常量池、只改字符串”的方案必然对不上。

好消息是：只需要对**被补丁覆盖的 1365 个类**做到逐字节一致；未被补丁的类
可以用任何正确的重映射。这让 C# 重写的验收范围可控（以现成的 MC_OFF 哈希为 oracle）。

---

## 5. 可选路线（已选 C）

| 路线 | 做法 | 优点 | 代价 |
|---|---|---|---|
| **A. 托管 JRE** | 其余步骤原生化，只有 ART 调 Mojang/发行版 JRE | 启动本来就要 JRE，边际成本≈0；现有 `JavaRuntimePlanService` 可复用 | 首装仍要下 JRE（但启动也需要） |
| **A'. 裁剪 JRE** | 用 `jlink` 生成 39 MB 的 `java.base+jdk.crypto.ec` 运行时，专跑 processors | 体积可控、无 LGPL 二进制问题；实测三个工具输出全一致 | 仍是一个 JVM；需要按平台生成 |
| **B. 原生镜像 ART** | 用 GraalVM 把 ART 编成每平台可执行文件 | 真正零 JVM；实测输出一致 | 20 MB/平台；ART/srgutils 为 **LGPL-2.1**，需合规审查与可重链接材料 |
| **C. C# 重写重映射器（已采用）** | 移植 ASM 等价实现 + ART fixer | 无第三方运行时、无 LGPL；已逐字节对齐三个 ART 版本 | 约 8–12k 行，强耦合 ASM 版本，回归面大 |

补充路线 **B'**：自己写一个只依赖 ASM（BSD-3）的小型 remapper（TSRG 解析也自己写），
再用 native-image 编译。这样能避开 LGPL，但仍需维护 Java 源码与多平台二进制。

> 不建议“迷你 JVM 解释器”：ART 依赖 ASM、srgutils、jopt 与大量 `java.base`
> 集合/流/注解 API，做一个能跑它的解释器比移植 ASM 更大。

---

## 6. 落地状态

1. ~~先做原生低风险步骤~~：`binarypatcher`、`jarsplitter`、`MCP_DATA`、`MERGE_MAPPING` 已完成。
2. `loader.repair` 已是“原生优先 + 按步骤回退 Java”：`IProcessorRunner` 暴露
   `CanRunWithoutJava`，`NativeProcessorRunner` 按 `MainClass` 分派，未覆盖的步骤回退
   `ForgeProcessorRunner`。
3. ~~评估路线 B/C~~：已采用路线 C（C# 重写 ART），ART 0.1.22 / 1.0.6 / 2.0.3 全部对齐。

剩余工作：

- 按产品决策，`DOWNLOAD_MOJMAPS`、`PROCESS_MINECRAFT_JAR`、`binarypatcher --store/--marker`、
  `ART --reverse` 不再原生实现，保持 Java 回退。
- `loader.plan` 已返回 `requiresJava` / `javaRequiredSteps`；Dart 在下载前判断并提示，
  不再出现“下载几百 MiB 后才失败”。
- server 侧 `EXTRACT_FILES` / `BUNDLER_EXTRACT` 不在客户端安装路径上，保持 Java 回退。

---

## 7. 未验证与边界

- ART 0.1.22 / 1.0.6 / 2.0.3 已用真实安装器产物（slim jar + mappings-merged +
  安装后的 `-srg.jar`）验证 class 条目与顺序逐字节一致，C# 重映射器与 fixer 链全部对齐
  （`fernflower_abstract_parameter_names.txt` 除外，它不参与安装）。
- 原生 `MCP_DATA`、`MERGE_MAPPING`、`jarsplitter`、`binarypatcher` 已用 Forge 1.20.1 与
  NeoForge 21.1.248 的真实产物逐字节验证。
- `DOWNLOAD_MOJMAPS`、`PROCESS_MINECRAFT_JAR`、`binarypatcher 1.3.4 --store/--marker`、
  `ART --reverse` 按产品决策不做原生实现；`loader.plan` 会标记 `requiresJava`，
  UI 在下载前提示需要 Java。
- `LoaderJavaCoverageTests` 覆盖了 ART 原生、`--store/--marker`、`PROCESS_MINECRAFT_JAR`
  和 `DOWNLOAD_MOJMAPS` 四种判定；`loader_install_dialog_test` 断言无 Java 时弹窗保持
  打开并显示“需要 Java 才能安装”。
- 逐字节 ZIP 输出依赖 zlib level 6：Linux 用系统 `libz.so.1` 可完全匹配；
  其他平台的 `Zlib.Portable` 回退只保证语义正确，整文件哈希可能不同。
- 原生 ART 输出暂不生成 `fernflower_abstract_parameter_names.txt`（反编译提示，不参与启动）。
- Forge 1.21.8 等更新版本未测；processor 组合可能变化。
- OptiFine 的 `Patcher` 是另一条 JVM 链，不在本方案覆盖范围。
- 不下载、不分发任何 `-client.jar` 或 MC_OFF 中间产物；全部本地生成。
- 所有参考实现只依据公开格式与行为，未复制 LGPL 源码。
