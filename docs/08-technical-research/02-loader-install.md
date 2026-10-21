# 02 · 加载器安装：到底需不需要 Java？

> 核心问题：**SJMCL 是不是真的不用 Java 就能装 Forge/NeoForge？**
> 答案：**不是。** SJMCL 的 Fabric/Quilt 与 ≤1.12.2 的 Forge 确实只用 HTTP + 解压；
> 但 Forge/NeoForge/Cleanroom 的 `processors`、OptiFine 的 `Patcher` 都是**用 Java 进程跑的**。
> 「事实」标注来源（仓库 + commit + 文件行号，或实测命令），检索日期 **2026-10-20**；未复现内容进 §9。

---

## 0. 结论速览

| 加载器 | 安装本质 | 需要 Java？ | 证据 |
|---|---|---|---|
| Fabric | 拉 meta profile JSON，合并版本 JSON，下库 | **否** | 实测 profile JSON + `fabric.rs:23` |
| Quilt | 同上（v3 meta，多一个 `hashed` 库） | **否** | 实测 + `quilt.rs` |
| Forge ≤ 1.12.2 | installer 是 zip，取内嵌 universal jar + version.json | **否** | 实测 1.7.10 / 1.12.2 installer |
| Forge 1.13 – 1.21.x | processors 生成 `-client.jar`（Cleanroom 同构） | **是** | 实测 1.20.1 / 1.21.1 / 1.21.8 |
| NeoForge 1.20.1 | 同 Forge（坐标 `net.neoforged:forge`） | **是** | 实测 + `neoforge.rs:34` 特判 |
| NeoForge 1.20.2+ | 同 Forge（坐标 `net.neoforged:neoforge`） | **是** | 实测 21.1.209 |
| OptiFine | 无 API；installer 内 `optifine.Patcher` | **是** | `optifine.rs:275` |

**一句话**：SJMCL 做的是「**不运行 installer 主程序，只挑 processors 跑**」，不是「不用 Java」。
真正无 Java 的边界是：**凡要产出 `-client.jar`（重映射 + 二进制补丁）的加载器，都必须有 JVM。**

---

## 1. Fabric / Quilt：纯 HTTP 安装（无 Java）

```
GET https://meta.fabricmc.net/v2/versions/loader/{mc}/{loader}/profile/json
GET https://meta.fabricmc.net/v2/versions/loader/{mc}/{loader}     # launcherMeta 形态
GET https://meta.quiltmc.org/v3/versions/loader/{mc}/{loader}/profile/json
```

实测 `1.21.1 + fabric-loader 0.16.10` 的 `profile/json` 是**完整版本 JSON**：

| 字段 | 实测值 |
|---|---|
| `id` / `inheritsFrom` | `fabric-loader-0.16.10-1.21.1` / `1.21.1` |
| `mainClass` | `net.fabricmc.loader.impl.launch.knot.KnotClient` |
| `arguments` | `game` / `jvm` 两段（1.13+ 结构） |
| `libraries` | 8 个，含 fabric-loader、intermediary、ASM，带 sha1/size/url |

`launcherMeta` 形态额外给 `libraries.common/server/client` 与 `min_java_version`（SJMCL 用这个）；
`profile/json` 适合直接当子版本 JSON。

**合并规则**（child 覆盖 parent）：`mainClass` 覆盖；`libraries` 按 `group:artifact[:classifier]` 去重、
child 优先；`arguments.game/.jvm` **追加**；`inheritsFrom` 保留，运行期展开。

**坑**：
1. ASM 版本随 loader 变，**不要**固定自己的 ASM。
2. Quilt 的 `net.fabricmc:*` 不在 Quilt Maven，SJMCL 按前缀切源（`quilt.rs`），否则 404。
3. 1.13 以前只有 `minecraftArguments` 字符串，合并要双轨；Fabric API / QFAPI 是独立 mod（Modrinth `P7dR8mSH` / `qvIfYCYJ`），别混进加载器事务。
4. 版本选择：`GET /v2/versions/loader/{mc}` 按 `stable`/`version` 排序即可，无需 Java 探测。

---

## 2. Forge / NeoForge：installer 结构与 processors

### 2.1 installer jar 结构（实测 `forge-1.21.1-52.1.0-installer.jar`，6.4 MB）

```
install_profile.json   28.9 KB  ← 安装脚本（spec 1）
version.json           24.7 KB  ← 要合并进启动的版本 JSON
data/client.lzma        2.9 MB  ← 二进制补丁包（binpatch）；server.lzma 同尺寸
data/run.sh|run.bat|unix_args.txt|win_args.txt
maven/…/forge-…-shim.jar        ← 唯一内嵌 maven 产物
META-INF/MANIFEST.MF   Main-Class: net.minecraftforge.installer.ClientInstall
```

关键事实：**现代 installer 几乎不内嵌 maven 产物**——1.21.1 只有 shim，1.20.1 与 NeoForge 21.1 连 `maven/` 目录都没有；
`installertools`/`jarsplitter`/`ATR`/`binarypatcher` 都在 `install_profile.json.libraries` 里，带 `url + sha1`，从 Maven 现下。
→「直接下 Maven 产物」对**工具**成立，对**最终 `-client.jar` 不成立**（见 §2.5）。

### 2.2 install_profile.json：spec 0 与 spec 1

| 版本 | `spec` | 结构 | processors |
|---|---|---|---|
| 1.7.10 / 1.12.2 | 0 | `install{path,filePath}`+`versionInfo{}` 或 `path`+`json`+内嵌 `maven/` | 无（1.7.10 还**无 version.json**） |
| 1.20.1 / 1.21.1 / 1.21.8 / NeoForge 21.1.209 | 1 | `data{}` + `processors[]` + `libraries[]` + `json` | 有 |

spec 0 的安装 = 解压 + 拷 jar + 合并 version.json，**完全不需要 Java**。spec 1 的 `data` 是变量表，值三类（实测 1.21.1）：

| 形态 | 例子 | 含义 |
|---|---|---|
| Maven 坐标 | `[net.minecraft:client:1.21.1:mappings@tsrg]` | 解析到 `libraries/…`；Mojang 侧产物要自己下 |
| installer 内路径 | `/data/client.lzma` | 从 installer zip 解出 |
| 字面量 | `'2d2537d3…'` | 直接当字符串（多为 sha1） |

`libraries[]` 是**安装期 classpath**；启动库只看 `version.json.libraries`。SJMCL 这个边界分得对。

### 2.3 processors 链

| 工具 | 任务 | 作用 | 为什么传统上必须 Java |
|---|---|---|---|
| `installertools` | `EXTRACT_FILES` | 抽 `run.sh`（仅 server） | 无必要，但没写原生版 |
| `installertools` | `BUNDLER_EXTRACT` | 拆 server bundler（仅 server） | 需读 jar 内嵌套 jar |
| `installertools` | `MCP_DATA`/`MERGE_MAPPING` | 解 MCP 映射、合并 mojmap | 需解析 tsrg/srg |
| `installertools` | `DOWNLOAD_MOJMAPS` | 拉 Mojang 混淆映射 | 纯 HTTP，**启动器可替代** |
| `jarsplitter` | — | client.jar 拆 `slim`(类)+`extra`(资源)，按 srg 过滤 | 需读 class 常量池 |
| `ForgeAutoRenamingTool`/`SpecialSource` | — | official → srg 全量重映射 | ASM 级改写 + 继承图 |
| `binarypatcher` | — | 解 LZMA（GDIFF/javaxdelta）打补丁，产出 `-client.jar` | LZMA + delta + class 校验 |

按 `sides` 过滤后的 **client 侧** processor（实测）：Forge 1.20.1 = 6 个（3×installertools + jarsplitter + ATR + binarypatcher）；
Forge 1.21.1 / 1.21.8 = 3 个（installertools/DOWNLOAD_MOJMAPS + ATR + binarypatcher）；NeoForge 21.1.209 = 6 个（同 1.20.1）。

> DOWNLOAD_MOJMAPS 可用「直接下 `version.downloads.client_mappings`」替掉，
> 于是**纯客户端最少只剩 2 个真处理器：ATR + binarypatcher**。

### 2.4 产出物与 version.json

```json
{ "name": "net.minecraftforge:forge:1.21.1-52.1.0:client",
  "downloads": { "artifact": { "url": "", "sha1": "2d2537d3…", "size": 28054104 } } }
```

`url` 为空 = **本地生成物，别去下**。启动器必须：跳过空 url 的库 → 用 processors 在 `libraries/` 生成 →
按 `sha1` 校验（`data.PATCHED_SHA` 或 `outputs`）。

### 2.5 「不跑 Java」的六条路

| 方案 | 可行性 | 合法性 | 维护成本 | 版本覆盖 |
|---|---|---|---|---|
| A. 直接下 Maven 全量产物 | **不行**：`forge-*-client.jar` 实测 404（1.20.1/1.21.1/NeoForge 21.1） | — | — | 0 |
| B. 第三方托管预生成 `-client.jar` | 可行（自建 CDN） | **高风险**：含 Mojang 版权 class，公开分发违反 EULA | 极高（每版本×每加载器都要构建） | 可覆盖但不可持续 |
| C. Rust/C# 重写 processors | 可行：LZMA+GDIFF+zip 都能写 | 自研，合法 | **极高**：ATR 等价物 = ASM 级重映射 | 低 |
| D. 只重写 binarypatcher | 没意义（ATR 仍要 JVM） | — | 中 | — |
| E. 延迟到首次启动（ForgeWrapper 模式） | **可行**，Prism 在用 | 合法（跑官方工具） | 低 | 全覆盖 |
| F. 安装期用 Mojang runtime 跑 processors | **可行，最稳** | 合法 | 低 | 全覆盖 |

**结论**：A/B 不可取；C 是研究项目不是排期；**E+F 是工程解**。无 Java 只覆盖
Fabric/Quilt/≤1.12.2，而这块本来就无 Java。

### 2.6 1.20.5+ / 1.21+ 的变化

| 变化 | 事实 | 影响 |
|---|---|---|
| Java 21 / 25 | 1.20.5 起 `javaVersion.majorVersion=21`（实测 1.20.5/1.21.1/1.21.4/1.21.8；1.20.1 为 17）；26.1-snapshot-1 起 25（`jre_selector.rs:52-88`） | 装 1.20.5+ 必须 21；runtime 映射要可扩展 |
| NeoForge 拆分 | 1.20.1 用 `net.neoforged:forge:1.20.1-47.x`；1.20.2+ 用 `net.neoforged:neoforge:20.2/21.1/21.4.x`（Maven 实测 200） | 坐标按 MC 版本分支 |
| 版本号漂移 | `1.20.1-47.4.0`、`1.21.8-58.1.26`；新版出现 `66.0.13`（Prism meta，26.3） | **禁止解析版本号反推 MC 版本** |
| 主类变化 | 1.20.1 `cpw.mods.bootstraplauncher.BootstrapLauncher`；1.21.1 `net.minecraftforge.bootstrap.ForgeBootstrap`；NeoForge 仍 BootstrapLauncher | 主类只从 version.json 读 |
| 新增 shim | 1.21.1 起 `install_profile.path` 指向 `:shim` | 当普通库下载 |
| client 不再 jarsplitter | 1.21.1/1.21.8 只剩 3 个 client processor | 重写量变小，ATR 仍是硬骨头 |

### 2.7 合法性：一句被忽略的注释

1.12.2 的 `install_profile.json` 写着：`"_comment_": ["Please do not automate the download and installation of Forge. Our efforts are supported by ads from the download page."]`

**事实**：Forge 团队明确请求不要自动化安装。**推断**：启动器普遍直接走 Maven 的同一 installer 产物、
不抓下载页广告链，实践中被默许，但这是灰区不是许可。我们的做法：走 Maven、标注来源、不镜像 installer、不内置。

---

## 3. OptiFine：没有 API，也不许再分发

| 项 | 结论 | 依据 |
|---|---|---|
| 公开 API | **无**，只有 HTML 下载页；链接经 `adfoc.us` 广告网关跳 `optifine.net/adloadx?f=…` | 实测 `optifine.net/downloads` |
| 版本元数据 | 解析页面，或用 BMCLAPI `optifine/versionlist` | `loader_meta/optifine.rs` 只用 BMCLAPI |
| 版权 | "may not be placed on any web site or otherwise distributed publicly without advance written permission" | `optifine.net/copyright` |
| 安装机制 | `java -cp <installer> optifine.Patcher <mc.jar> <installer> <out>` | `optifine.rs:275-317` |
| 结论 | **不内置、不镜像、不抓广告页**；做「手动导入 jar + 解析 + 自动配置」 | 法务风险最低 |

BMCLAPI 的 `optifine/...` 本质是第三方再分发，**我们不接**；要做只能作「用户显式开启的第三方源」并写明风险。

---

## 4. SJMCL 实证（核心）

### 4.1 结论

它**不运行 installer 主类**（从不调用 `ClientInstall`），而是：解包 installer → 自己下库 →
重写占位符 → **用 Java 逐个跑 processors**。Fabric/Quilt 是真·无 Java；OptiFine 用 Java 跑 `Patcher`。

### 4.2 证据表

仓库 `https://github.com/UNIkeEN/SJMCL`，commit **`ba24b6dc7f6a147369f81de0c96b08b6df0cc0e2`**（2026-10-20，v1.3.2）。
路径基准：`src-tauri/src/instance/`；loader 模块在 `src-tauri/src/instance/helpers/loader/`（以下已按 main 分支复核）。

| 关注点 | 文件:行 | 观察到的实现 |
|---|---|---|
| 处理器执行 | `instance/helpers/loader/common.rs:90` `execute_processors` | 逐个 processor：读 jar 的 `META-INF/MANIFEST.MF` 取 `Main-Class`；`Command::new(selected_java.exec_path)` + `-cp <classpath>` + 主类 + args（第 124/139 行）；非 0 退出 → `ProcessorExecutionFailed` |
| 无窗口 | `common.rs:128` | Windows `creation_flags(0x08000000)`（CREATE_NO_WINDOW） |
| Forge 准备 | `instance/helpers/loader/forge.rs:112` `download_forge_libraries` | 解 `maven/*` 与 `data/client.lzma`；读 `install_profile.json` + `version.json`；替换 `{MINECRAFT_JAR}/{BINPATCH}/{INSTALLER}/{SIDE}/{ROOT}` 与 data 变量；重写后的 profile 写回实例目录 |
| NeoForge | `instance/helpers/loader/neoforge.rs:80` | 同构；`version.starts_with("1.20.1-")` 用 `net.neoforged:forge`，否则 `:neoforge` |
| MOJMAPS 特判 | `forge.rs:198-215` | args 含 `DOWNLOAD_MOJMAPS` 时不跑处理器，改下 `version.downloads.client_mappings` 并清空 args |
| 两阶段入口 | `instance/commands.rs:1280` `finish_mod_loader_install` | `Downloading → Installing` 时读 `<instance>/install_profile.json` 并 `execute_processors`（调用点在 1315 行）；成功后 `Installed` |
| 前端触发 | `src/contexts/task.tsx:465-500` | `forge-libraries`/`neoforge-libraries` 任务组完成回调里调 `finishModLoaderInstall(instanceId)` |
| 状态机 | `instance/models/misc.rs:70-84` | `NotDownloaded/Downloading/Installing/Installed/DownloadFailed` |
| 断点恢复 | `instance/helpers/misc.rs:160-270` | 启动时发现 `Downloading|Installing` 且首次运行 → 置 `DownloadFailed`，下次刷新重下 |
| OptiFine | `instance/helpers/loader/optifine.rs:275` `run_optifine_patcher` | `java -cp <installer> optifine.Patcher <base jar> <installer> <out>`；另抽 `launchwrapper-of-*.jar` 并加 `--tweakClass optifine.OptiFineTweaker` |
| Fabric / Quilt | `instance/helpers/loader/fabric.rs:23`、`quilt.rs` | Fabric 拉 `v2/versions/loader/{mc}/{ver}`；Quilt 拉 v3，`net.fabricmc` 前缀切 Fabric Maven；都只把库塞进 patch，无进程 |
| Java 定位与补装 | `jre_selector.rs`、`java.rs`（路径未二次复核） | 按 MC 版本算最低 Java（26.1→25、24w14a→21、1.18-pre2→17、21w19a→16、17w13a→8），优先「等于最低版本」；缺则下载 Mojang runtime（`8→jre-legacy`、`21→java-runtime-delta`、`25→java-runtime-epsilon`、其余 gamma） |

### 4.3 它的真实流程

```
installer.jar ──unzip──► install_profile.json / version.json / data/client.lzma
                              │ 解析 data → 收集 libraries（profile + version）
                              ▼
                DownloadPlan（installer + 库 + client.jar + mappings）
                              │
                     [下载任务组完成] → finish_mod_loader_install(id)
                              │
        for p in processors.filter(side == client):
            java -cp <p.classpath>:<p.jar> <Main-Class> <args…>
                              │
        libraries/…/forge-<v>-client.jar 生成 → status = Installed
```

### 4.4 缺陷（我们**不要**照抄）

1. **不校验 `outputs` 的 sha1**：退出码 0 就算成功，半成品 jar 会被当成功（HMCL 有前后两次校验）。
2. `maven/` 解压无哈希校验；且实测 1.20.1 / NeoForge 21.1 **没有** `maven/` 目录、1.21.1 只有 shim，这段逻辑基本空转。
3. 重写后的 `install_profile.json` 写进实例目录且含**绝对路径** → 迁移/换机后是脏数据。
4. Forge 版本列表**只有 BMCLAPI**，官方分支是 `TODO`（`loader_meta/forge.rs:61`）→ 镜像挂了就不能装。
5. 失败恢复靠「用户重启应用」（`is_first_run` 才降级），无自动重试/续跑。
6. Java 选择在 processor 阶段才做；刚下完 runtime 是否已注册进 `java_list` 有竞态可能（见 §9）。
7. 不读 `install_profile.spec`，只靠「有没有 version.json」分支。

---

## 5. 横向对比（只读结论，不抄代码）

| 项目 | 许可证 | 安装方式 | spawn Java | 亮点 / 代价 |
|---|---|---|---|---|
| **SJMCL** | GPL-3.0 + 附加条款 | 解包 installer + 跑 processors | 是 | 两阶段 UI（下载/安装分离）；缺陷见 §4.4 |
| **Prism** | GPL-3.0 | meta 组件 + installer，**首启由 ForgeWrapper 调 `installer.install` 跑 processors** | 是（运行期） | 安装快、可离线装完；首启慢、错误延后 |
| **HMCL** | GPL-3.0 | `ForgeNewInstallTask`，external java | 是 | `outputs` **预检+后检** sha1，可续跑；DOWNLOAD_MOJMAPS 本地化 |
| **ATLauncher** | GPL-3.0 | 启动器自身 JVM 内 `URLClassLoader` 反射调 processor 主类 | 否（同进程） | 无子进程；崩溃带崩启动器 |
| **CmlLib.Core** | MIT | 拆分包 `CmlLib.Core.Installer.Forge`，spawn java | 是 | 实现干净，适合抄设计 |
| **ProjBobcat** | MIT | `HighVersionForgeInstaller`，spawn java | 是 | 功能全；字符串拼 `Arguments` 有引号风险 |

核验点：Prism meta 的 Forge 组件 `mainClass = io.github.zekerzhayard.forgewrapper.installer.Main`，
`mavenFiles` 带 installer + 全部处理器库（`meta.prismlauncher.org/v1/net.minecraftforge/52.1.0.json`）；
ForgeWrapper `Main.java` 在启动时 `loadClass("…installer.Installer")` 并调 `install(libDir, mcJar, installerJar)`；
ATLauncher `loaders/forge/Processor.java` 用 `URLClassLoader` + `Method.invoke`。

---

## 6. 我们的推荐

### 6.1 每种加载器的路径（决策表）

| 场景 | 路径 | 需要 Java | 备注 |
|---|---|---|---|
| Fabric / Quilt | meta → 合并 → 下库 | 否 | P0，先做 |
| Forge ≤1.12.2 | 解 installer，取 universal + version.json | 否 | spec 0 分支 |
| Forge 1.13–1.21.x | installer + processors | **是** | 用游戏同款 JRE；先校验 outputs 再决定是否跑 |
| NeoForge 1.20.1 | 同 Forge，坐标 `net.neoforged:forge` | **是** | 版本带 `1.20.1-` 前缀 |
| NeoForge 1.20.2+ | 同 Forge，坐标 `net.neoforged:neoforge` | **是** | 20.2/21.1/21.4… |
| OptiFine | **手动导入 jar** 为默认 | 是（Patcher） | 见 §3 |
| Cleanroom | 同 Forge | **是** | 低优先级 |

**默认策略**：安装期拿到合适 JRE 就立刻跑 processors；拿不到就做完「下载阶段」并落盘，
processors 标记 `PendingProcessors`，**首次启动前**用启动 JRE 执行（ForgeWrapper 思路），避免死锁。

### 6.2 Java 的定位与隔离

```
JavaLocator
  ├─ 1. 用户指定 JRE（校验 major）
  ├─ 2. 托管 runtime：<data>/runtime/java-<major>/
  ├─ 3. Mojang java-runtime manifest（按 version.json javaVersion.component）
  └─ 4. 系统 PATH / 注册表 / /usr/libexec/java_home（只兜底）
```

| 约束 | 做法 |
|---|---|
| 不弹窗 | Windows `CREATE_NO_WINDOW`；Unix 不用 shell |
| 参数安全 | **argv 数组**，不拼字符串（空格/中文/引号） |
| 工作目录/内存 | 实例临时目录；`-Xmx512m`、`-Dfile.encoding=UTF-8` |
| 环境 | 清空 `JAVA_TOOL_OPTIONS`/`_JAVA_OPTIONS`/`CLASSPATH` |
| 超时 | 单 processor 5 min、整体 20 min；超时杀**进程树**（Job Object / 进程组） |
| 输出 | 环形缓冲 64 KB，失败落日志，不把 classpath 打进 UI |
| 版本校验 | 先 `java -version`，major 不匹配直接报错 |
| 并发 | `libraries/` 写入加文件锁；全局最多 1 个安装事务 |

### 6.3 事务与回滚

```
txid = uuid();  stage = <instance>/.staging/<txid>/;  store = <data>/objects/<sha1[0:2]>/<sha1>
1. 解析 → InstallPlan（纯数据，可存盘、可离线重放）
2. 下载到 store（临时文件 + sha1 + rename）
3. processors：输出写 stage；每个输出按 install_profile.outputs 校验
4. 全部通过：stage → 正式路径（rename）；version.json 临时文件 + rename
5. 最后写实例配置（status=Installed）—— 唯一提交点
失败：删 stage、保留 store、status=Failed + 失败阶段 + 缺失清单；重试从 InstallPlan 重放
```

- processors 幂等：`outputs` 已存在且 sha1 匹配就跳过 → 天然断点续跑（HMCL 做法）。
- 重装/换加载器：旧 `version.json` 备份 `*.bak`，失败可回退。
- 共享 `libraries/` 只增不删，清理交给独立「空间回收」任务。

### 6.4 增量 / 离线

| 需求 | 做法 |
|---|---|
| 增量 | 每个 artifact 带 sha1，store 命中即跳过；outputs 校验后跳过执行 |
| 镜像/离线 | 官方 ↔ BMCLAPI 双源，记录实际来源；离线只读 store，缺件输出 `missing[]`（name/url/sha1/size）不半途失败 |
| 预置/迁移 | 导出 `InstallPlan + 缺失清单` 做离线包；全路径相对实例根，**不要**把绝对路径写进 JSON |

### 6.5 「无 Java 安装」值不值得做？

| 判断 | 结论 |
|---|---|
| 作为 P0/P1 | **不值得**：JRE 本来就要为启动准备，复用到安装的边际成本≈0 |
| 作为卖点 | 只对 Fabric/Quilt/≤1.12.2 成立，而这块已无 Java |
| 真正该做 | ① 安装前自动补 Mojang runtime；② PendingProcessors 延迟执行；③ 失败可续跑 |
| 一定要做的话 | 只做 `binarypatcher` 的 Rust 版，ATR 仍交给 JVM；收益/成本不划算 |

---

## 7. 测试用例

| # | 用例 | 期望 |
|---|---|---|
| 1 | Fabric 1.21.1 + loader 0.16.10 | 合并结果与官方 `profile/json` 语义等价 |
| 2 | Quilt 1.21.1 | `net.fabricmc:*` 走 Fabric Maven |
| 3 | Forge 1.12.2 | 不启 Java，universal jar 落位 |
| 4 | Forge 1.20.1 / 1.21.8 全新 | 分别 6 / 3 个 client processor；`PATCHED_SHA` 匹配、shim 到位 |
| 5 | NeoForge 21.1.209 / 1.20.1 | 坐标分别是 `neoforge` / `forge` |
| 6 | 无 Java / 断网 | 自动下 `java-runtime-delta`；断网给缺失清单 |
| 7 | 安装中 kill | 重启后按 outputs 跳过，继续剩余 |
| 8 | Java 版本错（17 跑 1.21） | 预检报错，不进 processor |
| 9 | 路径 `D:\我的 游戏\inst` | argv 传参正常，无引号问题 |
| 10 | 并发装同一 Forge / 磁盘满 | 文件锁 + 明确错误码 + 回滚，无残留 stage |

---

## 8. 参考链接

| 内容 | 链接 |
|---|---|
| SJMCL 源码（commit `ba24b6dc`） | https://github.com/UNIkeEN/SJMCL |
| Fabric / Quilt meta 与文档 | https://meta.fabricmc.net/ · https://meta.quiltmc.org/ · https://wiki.fabricmc.net/documentation:fabric_installer |
| Forge Installer 源码 | https://github.com/MinecraftForge/Installer |
| Forge / NeoForge Maven | https://maven.minecraftforge.net/net/minecraftforge/forge/ · https://maven.neoforged.net/releases/net/neoforged/neoforge/ |
| ForgeWrapper（Prism 方案） | https://github.com/ZekerZhayard/ForgeWrapper |
| Prism meta 组件 | https://meta.prismlauncher.org/v1/net.minecraftforge/52.1.0.json |
| HMCL / ATLauncher | `HMCLCore/…/download/forge/ForgeNewInstallTask.java` · `…/loaders/forge/Processor.java` |
| CmlLib / ProjBobcat | https://github.com/CmlLib/CmlLib.Core.Installer.Forge · `…/ForgeInstaller/HighVersionForgeInstaller.cs` |
| Mojang Java runtime | https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json |
| OptiFine 版权 | https://optifine.net/copyright |

---

## 9. 未验证项

1. **SJMCL 其他分支/新版本**是否已改为无 Java：只审计 `main@ba24b6d`（v1.3.2）。
2. **SJMCL 在完全无 Java 机器上的行为**：未实机跑；`select_java_runtime` 会返回 `NoSuitableJava`，
   自动下载发生在下载阶段，runtime 何时注册进 `java_list` 未验证（竞态可能）。
3. **Forge 1.13–1.16.5 逐版本 processor 组合**：只实测 1.20.1/1.21.1/1.21.8/21.1.209/1.12.2/1.7.10。
4. **Forge `66.0.13` 新版本号规则**：来自 Prism meta 索引（requires 26.3），未查官方公告。
5. **OptiFine 条款完整解读**：只摘了 copyright 一段；「启动器代为下载」是否被允许，法律上无定论。
6. **Prism ForgeWrapper 首启耗时/失败率**、**ATLauncher 同进程 classpath 冲突**：均未实测。
7. **Mojang `java-runtime-epsilon`（Java 25）在 26.1 的可用性**：SJMCL 代码如此映射，未验证 manifest；Forge 1.13–1.16 的 SpecialSource 参数同样未实机跑。
