# 01 · 版本 JSON 合并与「单文件」有效版本

> 研究对象：Minecraft version JSON 的 `inheritsFrom` 链，以及把 vanilla + 加载器 profile 合并成一份**有效版本（effective version）**。
> 结论基于：Mojang `version_manifest_v2` 实际响应、Fabric Meta 实际响应、公开实现的行为，以及本仓库 `02-loader-install.md` 的实测。
> 本文只描述**我们**要实现的语义，不复制任何 GPL 代码。
>
> **实现状态（2026-10-21）**：C# `VersionChainResolver` / `VersionMergeService` / `VersionMergeJson` 已落地，覆盖本文 §3 的标量、库去重、参数拼接、legacy `minecraftArguments` 和链深/环检测；规则与 `${}` 仍在启动期求值。`EffectiveVersion` 可直接交给 `MojangPlanService.BuildFromEffectiveAsync` 生成原版下载计划。

---

## 0. 结论摘要

1. **Mojang 没有正式的 merge 规范**。`inheritsFrom` 是启动器约定；不同启动器在“重复库怎么处理”“参数拼接顺序”上并不完全一致。我们必须自己定义并写测试，不能“参考某一家”。
2. 合并的产物应是**结构合并、规则后置**：把链拍平成一份 JSON，但 `rules` 与 `${}` 占位符**留到启动时**再求值，否则缓存会被 OS/分辨率/账户状态污染。
3. **库去重键是 `group:artifact:classifier`（不含版本）**；同一 key 出现多个版本时，保留子层版本、位置沿用父层首次出现的位置，并记录 override 警告。
4. **Fabric/Quilt profile 的库可能没有 `sha1`/`size`**（实测 `intermediary`、`fabric-loader` 两条只有 `name`+`url`），下载器必须支持“仅 Maven 坐标”的条目。
5. **Fabric profile 的 `releaseTime`/`time` 是生成时刻**（每次请求都变），**绝不能**作为缓存键或排序依据。
6. 有效版本放**独立缓存目录**，不要伪装成 `versions/<id>/<id>.json`；实例只存输入（游戏版本 + 加载器 + profile 摘要），有效版本可随时重建。
7. Forge/NeoForge 的 `version.json` 里存在 **`url` 为空的库**（本地 processors 产物），合并阶段必须保留它、启动前校验存在性，**不能尝试下载**。

---

## 1. 术语

| 术语 | 含义 |
|---|---|
| layer | `inheritsFrom` 链上的一层 JSON（vanilla / Fabric / Forge / 自定义） |
| effective version | 把链拍平后的结构合并结果（规则未求值） |
| launch plan | 在 effective version 基础上，结合账户/Java/内存/分辨率求值后的 argv、classpath、natives |
| merge schema | 我们自己的合并算法版本号；算法变更必须递增并使缓存失效 |

---

## 2. 真实数据结构（1.20.1）

从 `version_manifest_v2.json` 取 `1.20.1` 的 URL 后实际拉取，顶层字段：

```
arguments, assetIndex, assets, complianceLevel, downloads, id,
javaVersion, libraries, logging, mainClass, minimumLauncherVersion,
releaseTime, time, type
```

关键值（实测）：

| 字段 | 值 |
|---|---|
| `mainClass` | `net.minecraft.client.main.Main` |
| `assets` / `assetIndex.id` | `5` |
| `javaVersion` | `{ component: "java-runtime-gamma", majorVersion: 17 }` |
| `logging.client` | `-Dlog4j.configurationFile=${path}` + `client-1.12.xml` |
| `libraries` | 88 条 |
| `minimumLauncherVersion` | 21 |
| `arguments.jvm` | 含 rules 的数组（osx 的 `-XstartOnFirstThread`、x86 的 `-Xss1M` 等） |

> 1.20.1 已经是纯结构化 `arguments`；不再有 `minecraftArguments`。旧版本（≤1.12.2）需要兼容。

Fabric（`meta.fabricmc.net/v2/versions/loader/1.20.1/0.15.11/profile/json`）实际响应：

```json
{
  "id": "fabric-loader-0.15.11-1.20.1",
  "inheritsFrom": "1.20.1",
  "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
  "arguments": { "game": [], "jvm": ["-DFabricMcEmu= net.minecraft.client.main.Main "] },
  "libraries": [
    { "name": "org.ow2.asm:asm:9.6", "url": "https://maven.fabricmc.net/", "sha1": "...", "size": 123598 },
    { "name": "net.fabricmc:intermediary:1.20.1", "url": "https://maven.fabricmc.net/" },
    { "name": "net.fabricmc:fabric-loader:0.15.11", "url": "https://maven.fabricmc.net/" }
  ],
  "releaseTime": "2026-10-20T09:54:00+0000",
  "time": "2026-10-20T09:54:00+0000",
  "type": "release"
}
```

注意三件事：

1. `intermediary` / `fabric-loader` **没有 hash、没有 size**。
2. `releaseTime` 是**服务端生成时间**，每次都变。
3. `arguments.jvm` 是一个带尾随空格的字符串；合并/展开时不要 trim 掉有意义的空格（这里无意义，但规则上不要假设）。

---

## 3. 合并规则

### 3.1 标量字段

| 字段 | 规则 |
|---|---|
| `id` | 取叶子层；effective 里同时记录 `chain` 数组 |
| `mainClass` | 叶子有则覆盖，否则继承最近祖先 |
| `type` / `releaseTime` / `time` | 叶子；**不参与缓存键** |
| `assets` / `assetIndex` | 叶子有则覆盖；通常继承 vanilla |
| `downloads.client` | 叶子有则覆盖，否则继承 |
| `javaVersion` | 叶子有则覆盖；加载器通常不写 |
| `logging` | 叶子有则覆盖；否则继承（老版本必须有） |
| `minimumLauncherVersion` | 取链上**最大值**（保守） |
| `complianceLevel` | 取链上**最大值** |
| `inheritsFrom` | effective 中删除，仅保留原始链信息 |

### 3.2 libraries

**键**：`group:artifact:classifier:extension`（版本不参与；extension 默认 `jar`）。

**顺序**：从根到叶依次遍历；首次出现的 key 决定位置。

**冲突**：同一 key 出现不同版本时，用叶子版本替换值、**位置不变**，并记录：

```
override: com.google.guava:guava 32.1.2 -> 31.1 (from 1.20.1-forge-47.2.0)
```

理由：

- 位置不变可保证 vanilla 的 classpath 顺序稳定，减少“某版本能跑、另一版本不能跑”的随机性；
- 叶子版本胜出符合加载器作者的预期（它明确覆盖了依赖）；
- 如果直接保留两份，classpath 上谁先谁后是隐式行为，极难排查。

**例外**：`classifier` 不同（如 `:natives-windows`）视为不同 key，全部保留。

**natives（≤1.12.2）**：

- `natives` 是 `{ "windows": "natives-windows", ... }` 映射；
- 按当前 OS 取 classifier，去 `downloads.classifiers[classifier]`；
- `extract.exclude` 叶子覆盖；
- 解压时跳过 `META-INF/`。

**现代（1.19+）**：natives 就是普通库，`name` 里带 `:natives-windows`，无需 `natives` 字段。

**没有 `downloads` 的条目**（Fabric/Forge 常见）：

```
name = group:artifact:version[:classifier][@ext]
url  = 该库的 Maven 根（缺省 https://libraries.minecraft.net/）
path = group.replace('.', '/') + '/' + artifact + '/' + version + '/' + artifact + '-' + version + [-classifier] + '.' + ext
```

hash/size 可能缺失：

| 情况 | 处理 |
|---|---|
| 有 `downloads.artifact.sha1` | 校验 |
| 有顶层 `sha1` / `size`（Fabric 风格） | 校验 |
| 两者都没有 | 下载但记录 `unverified`，失败时重试一次；不要因此拒绝安装 |

### 3.3 arguments

- `arguments.jvm`：父 → 子依次拼接（数组拼接，不排序、不去重）。
- `arguments.game`：同上。
- 带 `rules` 的元素**原样保留**，启动时求值。
- 字符串里的 `${...}` **原样保留**，启动时替换。
- 如果某层只有旧式 `minecraftArguments`：
  - 用能识别引号的 tokenizer 拆成数组，转成 `arguments.game`；
  - 若叶子层已有结构化 `arguments.game`，以结构化为准，旧字段忽略（记录警告）。

### 3.4 rules 求值时机

| 对象 | 求值时机 | 原因 |
|---|---|---|
| libraries | 启动时（构建 launch plan） | OS/arch 稳定，但保持与官方实现一致；也便于诊断 |
| arguments | 启动时 | `is_demo_user`、`has_custom_resolution` 依赖本次启动参数 |
| 默认值 | deny；按顺序覆盖 | 与官方一致 |

> 合并阶段**不要**过滤掉“当前 OS 不适用”的库。否则同一份缓存换平台（或用户改分辨率）就失效，且诊断页看不到完整信息。

### 3.5 下载与资源

- `downloads.client` 指向 client jar；加载器可能提供自己的 client 产物（见 §4）。
- `assetIndex.url` 下载索引，`objects` 按 hash 落到 `assets/objects/xx/xxxx`。
- `logging.client.file` 也要下载并校验（Log4Shell 缓解依赖它）。
- `javaVersion.component` 用于选择 Mojang runtime；缺失时按 `majorVersion` 走通用规则。

---

## 4. 加载器特例

### 4.1 Fabric / Quilt

- 只返回 profile JSON，**无 Java 参与**。
- `libraries` 追加到 vanilla 之后；`mainClass` 覆盖；`arguments.jvm` 追加 `-DFabricMcEmu=...`。
- 两条无 hash 的库必须能下载（见 §3.2）。
- Quilt 的 Maven 根与 group 前缀不同，但结构同构。

### 4.2 Forge / NeoForge

来自 `02-loader-install.md` 的实测结论：

| 现象 | 合并/启动时的处理 |
|---|---|
| `version.json` 中有 `url` 为空的库（`net.minecraftforge:forge:...:client`） | 保留；启动前检查文件存在；**不要下载** |
| JVM 参数使用 module path（`-p`、`--add-modules ALL-MODULE-PATH`） | 原样拼接；`${library_directory}`、`${classpath_separator}` 启动时替换 |
| `-DignoreList=...`、`-DmergeModules=...` | 原样保留，不要“优化” |
| `mainClass` 随版本变化（BootstrapLauncher / ForgeBootstrap） | 只从 JSON 读，绝不硬编码 |
| 1.21.1 起 `install_profile.path` 指向 `:shim` | 当作普通库下载 |
| 客户端 processor 产物 | 安装阶段生成并校验 sha1；合并阶段只记录期望路径 |

> Forge 的 `<id>.json` 可能包含对本地生成物的引用；**合并算法不需要知道它是怎么生成的**，只需要保留。

### 4.3 自定义 inheritsFrom

允许用户/整合包作者写自己的 profile（例如 `my-pack` → `1.20.1-forge-47.2.0` → `1.20.1`）。

- 链深上限建议 8，超过报错（防循环/恶意）。
- 检测环：记录已访问 id，重复即失败。
- 叶子 `id` 必须唯一；重复 id 的不同来源要在诊断里标出。

---

## 5. 合并算法（伪代码）

```csharp
EffectiveVersion Merge(IReadOnlyList<VersionJson> chain /* root → leaf */)
{
    var eff = new EffectiveVersion {
        Chain = chain.Select(v => v.Id).ToArray(),
        Libraries = new OrderedDictionary<LibKey, Library>(),
        JvmArgs = new List<Arg>(),
        GameArgs = new List<Arg>(),
        Warnings = new List<MergeWarning>()
    };

    foreach (var layer in chain)
    {
        // 1. 标量：后者覆盖前者
        eff.ApplyScalars(layer);

        // 2. 库：按 key 去重，叶子版本胜出，位置不变
        foreach (var lib in layer.Libraries)
        {
            var (key, ver) = LibCoordinate.Parse(lib.Name); // key = group:artifact:classifier:ext
            if (eff.Libraries.TryGetValue(key, out var existing))
            {
                if (existing.Version != ver)
                    eff.Warnings.Add(Override(key, existing.Version, ver, layer.Id));
                eff.Libraries[key] = lib;   // 原位替换（OrderedDictionary 语义）
            }
            else eff.Libraries.Add(key, lib);
        }

        // 3. 参数：直接拼接，保留 rules 与占位符
        eff.JvmArgs.AddRange(layer.Arguments?.Jvm ?? Empty);
        eff.GameArgs.AddRange(layer.Arguments?.Game ?? LegacyToArgs(layer.MinecraftArguments));
    }

    eff.MergeSchema = 1;
    return eff;
}
```

启动时：

```
resolve(effective, ctx):
  libs = effective.Libraries.Values
           .Where(l => RuleEvaluator.Allows(l.Rules, ctx))
  classpath = libs.Where(IsClasspath).Select(ResolvePath)
  natives   = libs.Where(IsNative).Select(ResolveNative)
  jvmArgs   = Expand(effective.JvmArgs, ctx)   // rules + ${}
  gameArgs  = Expand(effective.GameArgs, ctx)
  return LaunchPlan(classpath, natives, jvmArgs, gameArgs)
```

---

## 6. C# 数据结构（建议）

```csharp
sealed record VersionJson(
    string Id, string? InheritsFrom, string? MainClass,
    AssetIndex? AssetIndex, string? Assets,
    Downloads? Downloads, IReadOnlyList<Library> Libraries,
    Arguments? Arguments, string? MinecraftArguments,
    JavaVersion? JavaVersion, Logging? Logging,
    int? MinimumLauncherVersion, int? ComplianceLevel,
    DateTimeOffset? ReleaseTime, DateTimeOffset? Time, string? Type,
    IReadOnlyDictionary<string, JsonElement>? Extra);

sealed record Library(
    string Name, string? Url, Downloads? Downloads,
    IReadOnlyList<Rule>? Rules,
    IReadOnlyDictionary<string, string>? Natives,
    Extract? Extract,
    string? Sha1, long? Size);       // Fabric 风格的顶层 hash

sealed record LibKey(string Group, string Artifact, string Classifier, string Extension);
```

`Extra` 保留未知字段，避免加载器新增字段被我们丢掉；但**不要**把未知字段当语义使用。

---

## 7. 缓存与失效

### 7.1 缓存键

```
key = SHA256(
    mergeSchemaVersion
  + for each layer: id + contentHash
  + launcherMergeOptions
)
```

`contentHash`：

- 来自 manifest 的版本：用 manifest 的 `sha1`；
- 加载器 profile：对响应字节做 SHA-256（**不要**用 `releaseTime`）；
- 本地自定义 profile：文件内容 SHA-256。

### 7.2 存放

```
<cache>/effective/<key>.json          # 结构合并结果
<cache>/effective/<key>.inputs.json   # 链、各层 hash、生成时间、mergeSchema
```

- 不放进 `versions/`，避免被“修复版本/更新版本”逻辑误删或误认。
- 实例只存：`gameVersion`、`loader {type, version}`、`profileHash`（可选）。
- 启动时：有缓存且 inputs 匹配 → 直接用；否则重建。
- 提供“重建有效版本”按钮，用于排障。

### 7.3 失效场景

| 场景 | 是否失效 |
|---|---|
| 游戏版本 JSON 更新（sha1 变） | 是 |
| 加载器 profile 更新 | 是 |
| 启动器 mergeSchema 升级 | 是 |
| 用户改内存/分辨率/账户 | **否**（属于 launch plan） |
| Fabric `releaseTime` 变化 | **否**（不参与 key） |

---

## 8. 测试用例（必须覆盖）

| # | 输入 | 期望 |
|---|---|---|
| 1 | vanilla 1.20.1 单层 | 88 个库；mainClass/assetIndex/javaVersion 不变 |
| 2 | vanilla + Fabric 0.15.11 | mainClass=KnotClient；库 = 88+8；两条无 hash 库可解析出 Maven 路径 |
| 3 | 同一库两个版本（parent 32.1.2 / child 31.1） | 只保留一条，版本 31.1，位置 = 父层位置，产生 override 警告 |
| 4 | 同 group/artifact 不同 classifier | 两条都保留 |
| 5 | 1.7.10 + Forge（`minecraftArguments` + `natives`） | 旧参数被 tokenize；natives 按 OS 选 classifier；`extract.exclude` 生效 |
| 6 | Forge `url=""` 的 client 库 | 合并保留；启动前缺文件 → 明确报“安装未完成”，不发起下载 |
| 7 | 三层链 A→B→C | 标量 C 胜；参数顺序 A,B,C；链深与环检测正确 |
| 8 | rules：仅 osx 的库在 Windows | 合并保留；launch plan 中不出现 |
| 9 | `assetIndex` 在子层覆盖 | 子层胜，assets 目录按新索引 |
| 10 | Fabric `releaseTime` 两次不同 | 缓存命中，不重建 |
| 11 | 缺 `downloads` 且缺 `sha1` | 可下载，标记 unverified；校验失败重试一次 |
| 12 | 未知顶层字段 | 保留在 Extra，不影响行为 |

---

## 9. 坑清单

1. **不要用 `releaseTime` 排序或做缓存键**（Fabric 每次请求都变）。
2. **不要 trim 参数字符串**；也不要对参数数组排序。
3. **不要把 rules 在合并阶段过滤掉**；否则换 OS/分辨率后缓存错。
4. **不要假设所有库都有 `downloads`**；Fabric/Forge 大量只有 Maven 坐标。
5. **不要下载 `url` 为空的库**；那是本地生成物。
6. **不要硬编码 mainClass**；Forge 1.21 已换主类。
7. **不要忽略 `logging.client`**；老版本的 Log4Shell 缓解依赖它。
8. **不要用“库数量”判断安装完整性**；以文件哈希 + 关键产物为准。
9. **不要在多处各自 merge**（C# 与 Rust 各一份）；有效版本只有一个生产者。
10. **不要忘了 `minimumLauncherVersion`/`complianceLevel` 取最大值**；否则可能放行不兼容版本。

---

## 10. 未验证 / 待实测

- Forge/NeoForge 各版本 `version.json` 的完整字段差异（本文只覆盖 02 文档实测到的部分）。
- Quilt 的 Maven 根与 group 前缀重写细节（需拉真实 profile 对比）。
- 旧版（≤1.7.10）`natives` 与 `extract.exclude` 的边界行为。
- 某些第三方 profile 使用非标准字段（如 `+libraries`、`jar` 修改器）——遇到时按“未知字段保留 + 警告”处理。
- 是否需要支持 `inheritsFrom` 指向非本地 id（远程解析）——当前设计：**不支持**，必须能解析到本地链。
