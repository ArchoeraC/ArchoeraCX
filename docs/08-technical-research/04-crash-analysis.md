# 04 · 启动失败与崩溃诊断（报错分析）

> 目标：游戏异常退出后 10 秒内给出「发生了什么 / 为什么 / 怎么办」，并生成可上传的脱敏诊断包。
> 归属：C#（`archoera_cs`）业务层；C++ 只负责进程生命周期与事件转发；Dart 只渲染。
> 标记：**[事实]** 官方文档/源码/实测；**[推断]** 有依据但未逐项验证；**[未验证]** 待实测。

## 0. 结论

1. **退出码只是线索**。MC 崩溃后停在崩溃界面，用户关窗可能以 0 退出（**[推断]**，待复现）；存在 crash-report 或日志标记 `#@!@# Game crashed! Crash report saved to: #@!@#` 时一律按崩溃处理（HMCL 用同一标记定位报告）。**[事实-源码]**
2. **先结构化、后正则**：crash-report 按 `Description / -- Head -- / -- Affected level -- / -- System Details --` 切块，hs_err 按 Oracle 的 Header/Thread/Process/System 切块，再对小块匹配；比整篇 10 MB 扫描更快、误报更少。
3. **单条正则不足以定罪**：规则 = 签名 + 上下文门控 + 证据来源 + 评分。HMCL 的平面规则表曾把「缺前置」误判为「OptiFine 与 Forge 不兼容」（issue #2691）。**[事实-issue]**
4. **默认只生成本地诊断包**；上传 paste 必须逐次同意、先脱敏并预览，默认按钮「取消」（Prism 的 `Confirm Upload` 同款）。**[事实-Prism 源码]**
5. 规则外置：YAML 编写 → 构建期编译为 JSON 内嵌；后续可下发**签名**规则集；文案走 i18n key，升级规则不发版。

## 1. 阶段模型

| 阶段 | 首个可靠证据 | 典型原因 |
|---|---|---|
| `spawn` | 启动器日志 `CreateProcess error=` | Java 路径不存在、权限、杀软拦截 |
| `jvm` | stderr、`hs_err_pid*.log` | `-Xmx` 非法、VM option 不支持、32 位 Java |
| `bootstrap` | `Could not find or load main class` | 版本 JSON 缺库、加载器装坏 |
| `mixin` | `Mixin apply failed` / `MixinTransformerError` | mod 与 MC/加载器版本不匹配 |
| `mod_init` | `LoaderExceptionModCrash` / Fabric entrypoint | 依赖、重复 mod |
| `graphics` | `GLFW error` / `Pixel format not accelerated` | 驱动、OpenGL 版本、远程桌面 |
| `game` / `world` | crash-report `Description:`、`-- Affected level --` | 实体/方块 tick、区块损坏 |
| `runtime` | hs_err、退出码 | 原生崩溃、OOM、被系统终止 |

展示按 `jvm→bootstrap→mixin→mod_init→graphics→game→world→runtime` 排序；上游命中时，下游通用规则降级为「相关现象」。

## 2. 数据源

### 2.1 总表

| 数据源 | 路径 | 写入时机 | 关键字段 | 读取 |
|---|---|---|---|---|
| 退出码 | 进程句柄 | 退出瞬间 | NTSTATUS/信号（Windows 可能是负数） | 退出后立即 |
| stdout | 管道 | 逐行 | Log4j 行、崩溃标记、栈 | 实时 + 退出后 |
| stderr | 管道 | 逐行 | JVM 初始化错误、原生库错误（MC 自身日志主要走 stdout） | 实时 + 退出后 |
| `logs/latest.log` | `<gameDir>/logs/` | 每次启动**重建** | 时间/线程/级别/logger、`Stopping!`、崩溃标记 | 退出后兜底 |
| `logs/debug.log` | 同上 | **跨启动追加**、按大小滚动 | DEBUG 级、完整栈、类加载/Mixin 细节 | 退出后按 offset |
| crash-report | `<gameDir>/crash-reports/crash-*.txt` | 崩溃时 | 见 2.4 | 退出后等待 flush |
| hs_err | 工作目录/CWD，默认 `hs_err_pid<pid>.log` | JVM 致命错误 | 见 2.5 | 退出后 |
| 启动器日志 | `%APPDATA%/ArchoeraCX/logs/*.jsonl` | 全程 | 脱敏参数、退出码、err_code、耗时 | 退出后 |

### 2.2 退出码

| 值 | 平台 | 含义 | 首查 |
|---|---|---|---|
| `0` | 全 | 正常退出，**也可能**是崩溃界面关窗 | 是否存在 crash-report |
| `1` / `255` | 全 | 未捕获异常 / `System.exit(-1)` | `latest.log` 末尾 ERROR + 栈 |
| `-1073741819` (0xC0000005) | Win | ACCESS_VIOLATION | hs_err、驱动、native mod |
| `-1073741571` (0xC00000FD) | Win | 栈溢出 | hs_err、`StackOverflowError` |
| `-1073740791` (0xC0000409) | Win | fail-fast / 堆破坏 | hs_err、杀软注入 |
| `-1073741515` / `-1073741502` | Win | DLL 未找到 / 初始化失败 | VC++ 运行库、Java 安装 |
| `137` / `139` / `134` | Linux | SIGKILL（常见 OOM killer）/ SIGSEGV / SIGABRT | `dmesg`、hs_err |

> POSIX 的 `128+signal` 在 .NET/Java `ExitCode` 上是否原样返回需实测；内部保存 `raw_code + platform + 推断信号`。**[未验证]**

### 2.3 文本流与日志文件

- stdout/stderr 必须**异步读取**，否则管道写满会卡死游戏（.NET `RedirectStandard*` 的经典坑）。MC 的 ERROR 也写 stdout，只读 stderr 会漏掉多数崩溃。
- 切行处理 `\r\n`、chunk 截断多字节 UTF-8（增量解码）、末尾无换行；环形缓冲 `min(50_000 行, 8 MB)`，记 `dropped_lines`。
- `latest.log` 格式 `[HH:mm:ss] [thread/LEVEL] [logger]: msg`（1.7–1.12 可能无 logger）；每次启动重建，是天然会话边界。中文 Windows 旧版本可能非 UTF-8，解码顺序：UTF-8 严格 → 系统 ANSI（GBK/CP1252）→ 替换字符。
- `debug.log` **跨启动追加**，直接 grep 会把上周的 OOM 当本次原因。启动前记 `size`，退出只读 `[offset, EOF]`（上限 20 MB）。它常含 `latest.log` 省略的完整异常链。**[事实-原版 log4j2 配置；滚动策略按版本核实]**

### 2.4 crash-report

| 区域 | 字段 | 用途 |
|---|---|---|
| 头部 | `Description:`、首个异常、`Caused by:` 链 | 「发生了什么」 |
| `-- Head --` | `Thread:`、`Suspected Mods:`（Forge）、`Stacktrace:`、`Mixins in Stacktrace:`（Fabric） | 判责：首个非 vanilla 栈帧 |
| `-- Affected level --` | `Level dimension`、`All players`、实体/方块位置 | 世界问题 |
| `-- System Details --` | `Minecraft Version`、`Java Version`、`Memory`、`JVM Flags`、`Fabric Mods:`/`FML: ... mods loaded`、`Graphics card #0`、`OpenGL` | 校验实际 Java/内存/Mod，覆盖启动器侧假设 |

文件名 `crash-YYYY-MM-DD_HH.MM.SS-client.txt`；以 `mtime ≥ 启动时间−2s` 过滤，并校验报告内 `Time:` 与启动相差 <10 分钟；日志给了绝对路径则优先信任。

### 2.5 hs_err_pid*.log

**[事实-Oracle]** 默认写进程工作目录，不可写则写临时目录（Linux `/tmp`，Windows `%TMP%`）；可用 `-XX:ErrorFile=<dir>/hs_err_pid%p.log` 固定。

| 区域 | 关键行 | 判读 |
|---|---|---|
| Header | `# A fatal error...`、`# SIGSEGV`、`# Problematic frame:` | 信号 + 出错模块 |
| Thread | 当前栈、`Java frames:` | 有 Java 帧 → Java 层；纯 native → 驱动/mod |
| Process | `Command Line:`、`VM Arguments:`、`Environment Variables:` | **含 token/路径，必须脱敏** |
| System | OS、CPU、内存、`Dynamic libraries:` | 驱动与 native 库清单 |

`Problematic frame` 命中 `nvoglv64.dll / atio6axx.dll / igdumdim64.dll / libGL.so / swrast_dri.so` → 显卡驱动；命中 mod 自带 `.dll/.so` → native mod。

### 2.6 启动器日志与采集时序

JSONL 字段：`ts, level, event, instance_id, exit_code, duration_ms, err_code, msg`；不记完整命令行 token。游戏日志为空（JVM 没起来）时，它是唯一证据。

```text
OnLaunchStart: snapshot={t0, debugLogSize, crashDir/hsErrDir 列表}; ring.clear()
OnStdout/Err(line): ring.push; 每 500ms AnalyzeEarly(ring.tail(2000))   // 只跑 jvm/bootstrap 规则
OnExit(code): wait 1200ms（重试 glob 3 次）→ Locate（崩溃标记优先，其次 mtime）
              → BuildCorpus(artifacts, ring, debugSlice, launcherEvents)
              → AnalyzeFull(context) → persist + UI
```

## 3. 错误分类法（34 类）

| 域 | 规则 ID | 代表签名 | 级别 |
|---|---|---|---|
| Java | `java.too_old` | `UnsupportedClassVersionError ... 61.0 ... up to 52.0` | 阻断 |
| Java | `java.too_new` | `Unsupported class file major version`、`NoSuchFieldException: ucp` | 阻断 |
| JVM | `jvm.heap_invalid` | `Could not reserve enough space for ... object heap` | 阻断 |
| JVM | `java.oom_heap` | `OutOfMemoryError: Java heap space` | 崩溃 |
| JVM | `java.oom_metaspace` | `OutOfMemoryError: Metaspace` / `PermGen space` | 崩溃 |
| JVM | `jvm.native_oom` | `insufficient memory for the Java Runtime Environment` | 崩溃 |
| JVM | `jvm.bad_option` | `Unrecognized VM option` | 阻断 |
| JVM | `jvm.native_crash` | hs_err `SIGSEGV` / `EXCEPTION_ACCESS_VIOLATION` | 崩溃 |
| Mixin | `mixin.apply_failed` | `Mixin apply failed`、`MixinTransformerError`、`Critical injection failure` | 阻断 |
| 依赖 | `deps.fabric_missing` | `Could not find required mod` / `Unmet dependency listing` | 阻断 |
| 依赖 | `deps.forge_missing` | `Missing or unsupported mandatory dependencies:` | 阻断 |
| 依赖 | `deps.class_not_found` | `NoClassDefFoundError` / `ClassNotFoundException` | 阻断 |
| Mod | `mods.duplicate` | `Found a duplicate mod` / `Found duplicate mods:` | 阻断 |
| 加载器 | `loader.main_class_missing` | `Could not find or load main class` | 阻断 |
| 加载器 | `loader.native_missing` | `UnsatisfiedLinkError: Failed to locate library` | 阻断 |
| 加载器 | `loader.forge_incomplete` | `Invalid paths argument, contained no existing paths` | 阻断 |
| 加载器 | `loader.jar_modified` | `SecurityException: SHA1 digest error for` | 阻断 |
| 图形 | `gfx.glfw_init` | `GLFW error`、`Pixel format not accelerated` | 阻断 |
| 图形 | `gfx.driver_crash` | hs_err 帧在 `nvoglv64.dll` 等 | 崩溃 |
| 图形 | `gfx.gl_error` | `GL_INVALID_OPERATION`、`1282: Invalid operation` | 降级 |
| Mod | `mods.optifine_conflict` | `NoSuchMethodError` + `net.optifine` 栈 | 崩溃 |
| Mod | `mods.fabric_entrypoint` | `Could not execute entrypoint stage` | 崩溃 |
| Mod | `mods.forge_crash` | `LoaderExceptionModCrash: Caught exception from` | 崩溃 |
| Mod | `mods.version_mismatch` | `NoSuchMethodError: 'void net.minecraft...` | 崩溃 |
| Mod | `mods.extracted_jars` | `extracted jar files` | 阻断 |
| Mod | `mods.id_limit` | `maximum id range exceeded` | 阻断 |
| 世界 | `world.ticking_entity` | `Entity Type:` + `-- Entity being ticked --` | 崩溃 |
| 世界 | `world.feature_order` | `Feature order cycle found` | 崩溃 |
| 世界 | `world.chunk_corrupt` | `Chunk file ... missing` / `Region file ... corrupted` | 崩溃 |
| 认证 | `auth.invalid_session` | `Invalid session`、`AuthenticationException` | 阻断 |
| IO | `io.file_locked` | `being used by another process`、`AccessDeniedException` | 阻断 |
| IO | `io.port_in_use` | `Address already in use` | 降级 |
| 启动器 | `launcher.java_missing` | `CreateProcess error=2` | 阻断 |
| 未知 | `unknown.exit_code` | 无规则命中 + 非 0 退出码 | 未知 |

## 4. 规则引擎

### 4.1 区域模型

解析器先把文本变成「带来源/行号的区域」，规则只声明 `scope`：

| scope | 内容 | 解析器 |
|---|---|---|
| `crash.head` | `Description` + 首个异常 + `-- Head --` 栈/疑似 mod | `CrashReportParser` |
| `crash.system` / `crash.level` | System Details、Mod 列表、Affected level | 同上 |
| `hserr.header` / `hserr.stack` | 信号、Problematic frame、Java frames | `HsErrParser` |
| `latest.log` / `stdout` | 行（level/thread/logger/line_no） | `MinecraftLogParser` |
| `launcher.events` | 结构化事件 | 启动器自身 |

每个区域保留 `(source, line_no, text)`，证据可跳回原文；展示和上传使用同一份脱敏副本。

### 4.2 规则格式（YAML → 构建期 JSON）

```yaml
schema: 1
id: mixin.apply_failed
version: 3
category: loader            # java|jvm|loader|mod|graphics|auth|io|world|launcher|native
phase: mixin
severity: blocker           # blocker|crash|degraded|info
base_score: 78
root_cause: mixin_failure
scope: [crash.head, latest.log, stdout]
literals: ["Mixin apply failed", "MixinTransformerError", "Critical injection failure"]
match:
  any: ['Mixin apply failed (?<config>[\w.-]+\.mixins\.json)', '(?:MixinTransformerError|InvalidMixinException|Critical injection failure)']
  none: ['(?:disabled|ignored) by config']
frames:                     # 可选：栈帧匹配，与 match 同时满足
  any: [{ package_prefix: 'com.examplemod.mixin', max_depth: 12 }]
context: { loader: [forge, neoforge, fabric, quilt], mod_present: [] }
extract: { config: '$config' }
explain_key: crash.mixin.apply_failed
explain_args: { config: '$config' }
actions:
  - { kind: open_path, path: '${instance}/mods' }
  - { kind: disable_mod, mod_id: '$mod_id', requires_confirm: true }
  - { kind: upload_log }
suppress_if_matched: [java.too_old]    # 上游已命中 → 降级为“相关”，不删除
tests: [{ fixture: mixin_apply_failed.log, expect: hit }, { fixture: mixin_warn_only.log, expect: miss }]
```

签名有三类：多行正则（`[\s\S]` 或按 scope 切片）、栈帧前缀 + 深度（`frames`）、上下文条件（`context`：Java 主版本、加载器、mod 是否存在）。三者 AND。

约束：正则限 RE2 子集（无反向引用/环视），.NET 用 `RegexOptions.NonBacktracking` + 50 ms 超时，CI 校验；每条规则 ≥1 正例/≥1 反例，`explain_key` 全语言存在；规则集带 `ruleset_version + min_launcher_version`，远程下发用 Ed25519 签名，验签失败回退内置。YAML 仅用于编写，运行时是 JSON（避免 NativeAOT 下反射型 YAML 库的裁剪问题）。**[事实：.NET 7+ 支持 NonBacktracking]**

### 4.3 匹配器伪代码

```csharp
AnalysisReport Analyze(Corpus c, LaunchContext ctx) {
  var hits = new List<Finding>();
  foreach (var rule in rules) {
    if (!rule.Context.Allows(ctx)) continue;                 // 版本/加载器/Java/Mod 门控
    if (!rule.Literals.Any(k => c.Prefilter.Contains(k))) continue;  // Aho-Corasick
    foreach (var scope in rule.Scope) {
      var doc = c.Region(scope); if (doc is null) continue;
      if (!rule.Frames.Allows(doc.StackFrames)) continue;    // 可选栈帧门控
      var m = rule.Regex.Match(doc.Text);                    // NonBacktracking + timeout
      if (!m.Success || rule.None.Any(n => n.IsMatch(doc.Text))) continue;
      hits.Add(Evaluate(rule, m, doc, ctx));                 // 提取组、行号、算分
      break;                                                 // 每规则只取最强证据
    }
  }
  return Rank(MergeByRootCause(Suppress(hits)), ctx);        // Top3 + 其余折叠
}
```

`Evaluate`：组值去控制字符并截断 200 字符；模板替换（如 class file version 减 44 得 Java 主版本）；记录证据行号与来源并集。

### 4.4 评分与合并

```text
score = base(blocker 80 / crash 70 / degraded 40 / info 10)
      + source(crash.head 15, hs_err 15, loader 12, latest 8, stdout 6, launcher 4)
      + specificity(命名 mod/config 10, 异常+字段 8, 泛化 0)
      + context_confirmed(10) + near_tail(5) + min(9, 3*(distinct_sources-1))
      - generic_penalty(10)
```

| 分数 | 置信度 | 文案 |
|---|---|---|
| ≥85 | 确定 | 「原因：…」 |
| 65–84 | 很可能 | 「很可能：…」 |
| 45–64 | 可能 | 「可能：…」 |
| <45 | 参考 | 折叠到「其他线索」 |

合并：同 `root_cause` 取最高分并合并证据；分差 ≤5 时按阶段取上游；最多展示 3 条主因，其余「还发现 N 条线索」，被抑制的只在调试面板可见；无命中时非 0 退出码 → `unknown.exit_code` + 末尾 5 条 ERROR + 上传入口，0 且无报告 → 「正常退出」。

示例：`mixin.apply_failed`（78+12+10+5=105）与 `deps.class_not_found`（78，且被抑制）→ 主因 Mixin，副因「类缺失，可能是 Mixin 后果」。

### 4.5 上下文与防误报

`LaunchContext`：实例/版本、加载器（类型/版本）、Java（路径/主版本/厂商/架构，区分自动探测与手动）、`-Xms/-Xmx`、物理内存、JVM 参数、Mod 列表（id/名称/版本/文件/sha1/启用/来源）、最近变更（24h/7d 内 mod、Java、加载器变更）、OS/GPU/驱动。

1. **时间窗**：`debug.log` 用 offset；crash-report 用 mtime + 报告 `Time:`；旧报告不参与。
2. **证据等级**：crash.head > loader 明确文案 > latest.log > stdout > 退出码；低级证据不能单独定「确定」。
3. **崩溃头优先**：`NoSuchMethodError` 在普通日志可能只是探测可选 API，只有 crash.head 或导致终止才算致命。
4. **否定语境**：`not installed / not present / disabled / optional` 不触发对应规则。
5. **上下文门控**：OptiFine 冲突必须 `mod_present: optifine`；`java.too_old` 要求上下文 Java 主版本确实低于要求，若 crash-report 的 `Java Version` 与启动参数不一致，先报「启动器用错 Java」。
6. **上游优先**：明确「缺前置」时抑制 OptiFine/版本不匹配的猜测（#2691 教训）。
7. **不臆造 mod**：栈帧启发式匹配不到 mod 列表时只显示包名 + 「疑似」。
8. **未知即未知**：无证据就展示退出码/最后错误/hs_err 是否存在，不生成原因。

## 5. 20 条规则示例

> 省略 `scope/context/actions/tests`；`re` 为 .NET 命名组语法。备选组（`mod2`/`id2`）为空时回退到主组，避免文案出现空值。

```yaml
- {id: java.too_old, sev: blocker, re: 'java\.lang\.UnsupportedClassVersionError: .*?class file version (?<actual>\d+)\.0.*?up to (?<supported>\d+)\.0',
   why: '当前 Java 只支持到 class {supported}（Java {current}），类需要 {actual}（Java {needed}）', fix: '切到 Java {needed}+；1.17=16，1.18–1.20.4=17，1.20.5+=21'}
- {id: java.too_new, sev: blocker, re: '(?:Unsupported class file major version (?<v>\d+)|Unable to make protected final java\.lang\.Class java\.lang\.ClassLoader\.defineClass|java\.lang\.NoSuchFieldException: ucp|because module java\.base does not export)',
   why: '旧版 Forge/LaunchWrapper 不兼容 Java 9+（常见 1.12.2 配 Java 17）', fix: '1.12.2/1.16.5 用 Java 8；1.17 用 16；不要全局改 Java'}
- {id: jvm.heap_invalid, sev: blocker, re: '(?:Could not reserve enough space for (?<kb>\d+)KB object heap|Invalid maximum heap size|Too small maximum heap)',
   why: '分配内存超出可用/可表示范围，或使用了 32 位 Java', fix: '降低 -Xmx（≤ 物理内存 60%），确认 Java 为 64 位'}
- {id: java.oom_heap, sev: crash, re: 'java\.lang\.OutOfMemoryError: (?:Java heap space|GC overhead limit exceeded)',
   why: '堆内存耗尽（当前 -Xmx {max_mb} MB）', fix: '提高 -Xmx 到 4–8 GB，同时降视距/移除高占用 mod'}
- {id: java.oom_metaspace, sev: crash, re: 'java\.lang\.OutOfMemoryError: (?:Metaspace|Compressed class space|PermGen space)',
   why: '类元数据区耗尽，通常 mod 过多或元空间上限过小', fix: 'Java 8+ 设 -XX:MaxMetaspaceSize=512m；Java 7 及更早设 -XX:MaxPermSize=256m；减少 mod'}
- {id: mixin.apply_failed, sev: blocker, re: '(?:Mixin apply failed (?<config>[\w.-]+\.mixins\.json)|MixinTransformerError|MixinApplyError|InvalidMixinException|Critical injection failure|mixin\.injection\.throwables\.|Mixin apply failed)',
   why: 'Mixin {config} 注入失败，通常是 mod 与当前 MC/加载器不匹配', fix: '更新该 mod；仍失败则禁用最近安装的 mod 二分定位'}
- {id: deps.fabric_missing, sev: blocker, re: '(?:Could not find required mod: (?<mod>.+?) requires |Mod \x27(?<mod2>[^\x27]+)\x27[^\n]*?requires any version of )(?<dep>[^,\n]+)',
   why: '{mod} 缺少前置 {dep}', fix: '安装 {dep} 的匹配版本（注意 MC 与加载器）'}
- {id: deps.forge_missing, sev: blocker, re: 'Missing or unsupported mandatory dependencies:\s*(?<body>(?:[ \t]*Mod ID:[^\n]*\n?)+)',
   why: 'Forge 强制依赖缺失/版本不符（解析 Mod ID、Requested by、Expected range）', fix: '按列表补齐前置；范围不符时升级/降级前置'}
- {id: mods.duplicate, sev: blocker, re: '(?:Found a duplicate mod (?<mod>\S+) at (?<path>.+)|Found duplicate mods:|Module optifine reads another module named optifine)',
   why: '同一 mod 存在多份 jar，或 OptiFine 被重复安装', fix: '删除 mods 中的旧版本；检查启动器是否又自动装了一份'}
- {id: loader.main_class_missing, sev: blocker, re: '(?:Could not find or load main class (?<cls>\S+)|ClassNotFoundException: net\.minecraft\.client\.main\.Main|Invalid or corrupt jarfile|Could not find net/minecraft/client/Minecraft\.class)',
   why: '加载器主类 {cls} 找不到，通常是版本 JSON 缺库或安装不完整', fix: '重新安装加载器/版本，校验 libraries'}
- {id: loader.native_missing, sev: blocker, re: '(?:UnsatisfiedLinkError: Failed to locate library: (?<lib>.+)|no lwjgl in java\.library\.path|Failed to find Minecraft resource version)',
   why: '原生库 {lib} 缺失（natives 解压失败或被杀软隔离）', fix: '重新下载版本；检查杀软隔离区与目录权限'}
- {id: loader.forge_incomplete, sev: blocker, re: '(?:Invalid paths argument, contained no existing paths:.*?(?:forge-.+?-client\.jar|fmlcore-.+?\.jar)|Cannot find launch target fmlclient)',
   why: 'Forge 安装文件缺失（forge-client.jar/fmlcore）', fix: '删除该 Forge 版本重装；检查磁盘与下载源'}
- {id: gfx.glfw_init, sev: blocker, re: '(?:GLFW error|Failed to create window|Pixel format not accelerated|The driver does not appear to support OpenGL|GLXBadFBConfig|Couldn.t set pixel format)',
   why: '无法创建 OpenGL 上下文：驱动过旧、远程桌面、虚拟机', fix: '更新驱动；用独显运行；确认支持 OpenGL 3.2+'}
- {id: gfx.driver_crash, sev: crash, re: 'Problematic frame:[\s\S]{0,200}?(?<lib>nvoglv64\.dll|atio6axx\.dll|igdumdim64\.dll|libGL\.so|swrast_dri\.so)',
   why: 'JVM 原生崩溃在显卡驱动 {lib}', fix: '清洁安装驱动；关闭超频/覆盖层（RTSS、Discord、Steam）'}
- {id: mods.optifine_conflict, sev: crash, re: '(?:NoSuchMethodError: \x27.*?(?:net\.minecraft\.client\.renderer|com\.mojang\.blaze3d)|net\.optifine\.reflect\.Reflector|Invalid Services found OptiFine|Shaders Mod detected\. Please remove it)',
   why: 'OptiFine 与当前 Forge/渲染 mod 冲突，或版本不匹配（仅当 mod 列表含 OptiFine 时启用）', fix: '移除 OptiFine 改用 Embeddium/Sodium；或换匹配版本；勿与 Shaders Mod 同装'}
- {id: auth.invalid_session, sev: blocker, re: '(?:Invalid session(?: \(Try restarting your game\))?|AuthenticationException|Failed to login: (?:Invalid|Authentication)|401 Unauthorized|Invalid credentials)',
   why: '登录凭证失效/被拒绝（正版会话过期或网络拦截）', fix: '退出账户重登；检查系统时间与 Mojang/MS 服务连通性'}
- {id: io.file_locked, sev: blocker, re: '(?:The process cannot access the file because it is being used by another process|java\.nio\.file\.(?:FileSystemException|AccessDeniedException).*?(?:Permission denied|Access is denied)|java\.io\.FileNotFoundException: .*?\(Permission denied\))',
   why: '文件被占用或无权限（另一实例/启动器、杀软、OneDrive）', fix: '关闭其它实例；移出 OneDrive；检查目录权限'}
- {id: mods.fabric_entrypoint, sev: crash, re: 'Could not execute entrypoint stage \x27(?<stage>[^\x27]+)\x27 due to errors, provided by \x27(?<mod>[^\x27]+)\x27!',
   why: 'Fabric 模组 {mod} 在 {stage} 阶段抛异常', fix: '更新/移除 {mod}；整合包向作者报告并附 crash-report'}
- {id: mods.forge_crash, sev: crash, re: '(?:LoaderExceptionModCrash: Caught exception from (?<name>.+?) \((?<id>[^)]+)\)|Failed to create mod instance\. ModID: (?<id2>[^,]+))',
   why: 'Forge 模组 {name}/{id} 初始化失败', fix: '更新/移除该 mod；检查前置与 MC 版本；二分定位冲突'}
- {id: world.ticking_entity, sev: crash, re: 'Entity Type: (?<type>\S+)[\s\S]{0,400}?Entity\x27s Exact location',
   why: '实体 {type} tick 时崩溃，通常是世界数据/实体类 mod 问题', fix: '先备份存档；移除最近添加的实体 mod；必要时用编辑器删除该实体'}
```

## 6. 测试用例（伪造片段）

| # | 关键输入 | 上下文 | 期望 |
|---|---|---|---|
| T1 | `UnsupportedClassVersionError ... 61.0 ... up to 52.0` | Java 8 | `java.too_old`，`needed=17` |
| T2 | `OutOfMemoryError: Java heap space` + `-Xmx2G` | 物理 8 GB | `java.oom_heap`，建议 4096 MB |
| T3 | `Unmet dependency listing:` + `requires any version of fabric-api, which is missing!` | Fabric | `deps.fabric_missing` |
| T4 | `Missing or unsupported mandatory dependencies:` + 2 行 `Mod ID: 'cloth_config'` | Forge | `deps.forge_missing`，解析 2 组 |
| T5 | `Mixin apply failed examplemod.mixins.json` + 随后 `NoClassDefFoundError` | Forge | 主因 Mixin；类缺失降级为相关 |
| T6 | `GLFW error 65542: ... does not appear to support OpenGL` | Windows | `gfx.glfw_init` |
| T7 | WARN `Error loading class ... ClassNotFoundException`（游戏继续）+ `OptiFine is not installed` | 无 OptiFine | **不命中** OptiFine 冲突 |
| T8 | `Failed to login: Invalid session` | 正版 | `auth.invalid_session` |
| T9 | 退出码 0，但存在本次 crash-report | — | `outcome=crashed`，仍完整分析 |
| T10 | `debug.log` offset 之前有 OOM，之后无异常 | 本次正常 | **不命中** `java.oom_heap` |
| T11 | `FileSystemException: C:\Users\张三\...\x.jar: ... being used` | Windows | `io.file_locked`；证据路径 `<redacted>` |
| T12 | hs_err `SIGSEGV` + `Problematic frame: C [nvoglv64.dll+...]` | NVIDIA | `gfx.driver_crash` |

非功能测试：每条规则在 NonBacktracking 下编译，1 MB 随机串 + 对抗串单条 <50 ms；脱敏回归（token/邮箱/UUID/IP/MAC/三种用户目录，输出无原文且幂等）；i18n key 缺失即构建失败；每条规则 ≥1 正例/≥1 反例。

## 7. 脱敏与上传

### 7.1 脱敏（按顺序）

| 类型 | 匹配 | 替换 |
|---|---|---|
| 启动参数 | `(--accessToken\|--clientId\|--xuid\|--uuid\|--username\|--userProperties)\s+\S+` | `$1 <redacted:token>` |
| Authorization | `(Bearer\s+)[A-Za-z0-9._~+/=-]+` | `$1<redacted:token>` |
| 键值密钥 | `(?i)\b(access[_-]?token\|refresh[_-]?token\|client[_-]?token\|id[_-]?token\|session[_-]?id\|password\|passwd\|secret\|api[_-]?key)\b(\s*[=:]\s*["']?)[^\s"',;]+` | `$1$2<redacted:secret>` |
| JWT | `eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}` | `<redacted:jwt>` |
| 邮箱 / UUID | email 正则；带连字符 UUID（32 位 hex 仅在 uuid 上下文替换） | `<redacted:email>` / `<redacted:uuid>` |
| IP / MAC | IPv4/IPv6；`([0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}` | `<redacted:ip>` / `<redacted:mac>` |
| 用户目录 | `([A-Za-z]:\\Users\\)[^\\\s]+`、`(/home/\|/Users/)[^/\s]+` | 保留结构，只替换用户名 |
| 服务器 / 种子 | `(--server\s+)\S+`、`(Level seed:\s*)\S+` | `<redacted:host>` / `<redacted:seed>` |

要点：**保留路径结构**（盘符、`.minecraft/mods/xxx.jar`），否则栈与 mod 路径无法诊断；写启动器日志前先脱敏一次（命令行 token 尤其不能落盘），上传前再脱敏并**二次扫描**；占位符带类型、可稳定编号，便于多份日志对照。

### 7.2 上传流程与同意

1. 默认「生成诊断包（本地 zip）」：`latest.log` 尾部 5 MB、`debug.log` 本次切片、最新 crash-report、hs_err、启动器日志尾部、`context.json`、`modlist.txt`、`README.txt`。**不联网**。
2. 「上传并获取链接」：脱敏 → 预览（替换计数 + 脱敏文本）→ 同意弹窗（域名、行数/大小、公开可见、保留期）→ 默认「取消」。mclo.gs：`POST https://api.mclo.gs/1/log`，字段 `content`，响应取 `url`。**[事实-Prism 源码]**
3. 超长：mclo.gs 保留前 10000 + 后 14900 行，中间插截断标记（Prism 的 25000 行策略）；本地包不截断。**[事实-Prism 源码]**
4. 失败只提示并保留本地包，**不静默重试、不自动换服务**；成功后 URL 写入崩溃历史并复制剪贴板。
5. 隐私说明写明：链接**知道即可访问**；保留至最后一次查看后 3 个月（mclo.gs 声明）；可能含 Mod 列表/硬件/路径（已脱敏）；不含令牌/邮箱/UUID/IP/MAC/用户名。**[事实-mclo.gs 页面]**
6. 绝不自动上传；「记住选择」只用于服务选择，不能跳过同意；上传他人日志需对方同意。

## 8. 参考实现

| 项目 | 做法（证据） | 借鉴 | 不采用 |
|---|---|---|---|
| HMCL | `CrashReportAnalyzer` 约 60 条扁平正则 + 命名捕获；扫描「控制台 + latest.log」；按规则去重，多条提示「多个原因」；无命中用栈帧关键字 + 包名黑名单；`#@!@#` 定位报告；导出 zip 只收启动后修改的文件；崩溃窗展示启动器/实例/内存/Java/OS。**[事实-源码]** | 规则表、崩溃标记、导出过滤、信息面板 | 无评分/上下文门控；整篇扫描易误报（#2691）**[事实-issue]** |
| Prism | 日志页上传；`Confirm Upload` 提示检查个人信息，默认 No；上传前 `anonymizeLog`；mclo.gs 超 25000 行首尾截断；支持 4 种 paste 服务。**[事实-源码]** | 同意交互、截断、上传前匿名化 | 匿名化仅 7 条（目录、Session ID、refresh token、device code），覆盖不足；无规则诊断 |
| LauncherX | 崩溃窗口分 `ModCaused/BlockCaused/EntityCaused/LoaderExceptionModCrash/Other`；读 `latest.log` + `crash-reports`；日志分 `LauncherLog/VerboseLog/Crashes/GameLog`；多组脱敏正则。**[事实-逆向报告 3.11/5.3]** | 原因分类的 UI 表达、日志分层 | 未见评分与规则外置（**[推断]**）；闭源，仅行为参考 |

> 证据位置：HMCL `HMCLCore/.../game/CrashReportAnalyzer.java`、`HMCL/.../ui/GameCrashWindow.java`；Prism `launcher/ui/GuiUtil.cpp`、`launcher/net/PasteUpload.cpp`、`launcher/logs/AnonymizeLog.cpp`；Oracle《Troubleshooting Guide》Fatal Error Log。
> 许可证：Prism 与 HMCL 均为 GPL-3.0；本项目闭源，**只读设计、独立实现**，不复制代码、正则表或文案。

## 9. 落地

C# 模块 `Archoera.Diagnostics`：`LogCollector`（双管道/增量解码/环形缓冲）、`ArtifactLocator`（启动快照 diff/崩溃标记/mtime）、`CrashReportParser`、`HsErrParser`、`MinecraftLogParser`、`RuleSetLoader`（内置 JSON + 签名远程）、`Analyzer`（预过滤/匹配/评分/合并）、`Redactor`、`DiagnosticPackager`、`CrashHistoryStore`（SQLite：report_id、ts、instance、primary_rule、exit_code、url）。

事件：`diagnostics.finding`（运行中早失败）、`diagnostics.completed`、`diagnostics.progress`。后台线程，预算 10 MB + 60 规则 <500 ms（**[未验证]**），不得阻塞退出/重启。

分期：① 数据源 + 崩溃标记 + 15 条高频规则 + 本地包 + 脱敏；② 评分/合并、运行中提示、崩溃历史、Mod/最近变更上下文、40+ 规则；③ 签名远程规则、未知原因统计、引导式二分（禁用一半 mod 重启）。

风险：正则 ReDoS（NonBacktracking + 超时 + 字面量预过滤）；`debug.log` 过大（offset + 20 MB 上限）；乱码（UTF-8/ANSI 回退）；规则漂移（按版本分组 fixture）；误报（置信度 + 门控）；隐私（默认本地 + 逐次同意）；GPL 污染（清洁室 + 来源记录）。

## 10. 未验证

1. 崩溃界面关窗时 MC 的真实退出码（Win/Linux，1.20.1/1.21 + Fabric/Forge）。
2. .NET/Java 在 POSIX 上对信号终止返回 `128+n` 还是原始信号值。
3. 各版本 `debug.log` 滚动策略与是否始终追加。
4. 1.7–1.12 `latest.log` 的确切格式与中文 Windows 编码。
5. mclo.gs 服务端大小限制与响应字段全集（当前只按 Prism 用 `success/url/error`）。
6. 10 MB 日志 + 60 条规则的实际耗时与内存峰值。
