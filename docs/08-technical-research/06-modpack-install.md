# 06 · 整合包：安装、导入导出与解压安全

> 范围：CurseForge zip、Modrinth `.mrpack`、MultiMC/Prism 实例包，以及 ATLauncher/FTB/Technic/HMCL/PCL 的常见形态；安装流水线、事务恢复、导出与恶意 zip 防护。
> 标记：**[事实]** 有官方规范/源码/实测；**[推断]** 基于协议与实现的判断；**[未验证]** 需用真实样本确认。检索日期 **2026-10-20**。
> 来源：Modrinth 格式说明、CurseForge 导出说明、MultiMC Wiki、Technic Solder 文档、HMCL 文档、ATLauncher/Prism 源码；GPL/AGPL 只读行为，不复制代码。配套：[02](02-loader-install.md)、[03](03-mod-search.md)、[05](05-download-engine.md)。

---

## 0. 结论速览

| # | 结论 | 依据 |
|---|---|---|
| 1 | **格式探测只能看 zip 内容**，扩展名不可信：`.mrpack` 就是 zip，`.zip` 可能是 mrpack，常见还有“外层 zip 套内层 zip/单层目录”。 | [事实] 两种格式均为 ZIP；本项目样本 |
| 2 | CurseForge 是**配方**：只有 `(projectID, fileID)`，没有路径、文件名、hash；必须调 CF API 二次解析，不能“找不到就装最新”。 | [事实] manifest 字段；[03 §2.3] |
| 3 | Modrinth 是**配方 + 路径 + 双哈希**：`files[].path` 是实例相对路径，可能落在 `resourcepacks/`、`shaderpacks/`，不能一律塞进 `mods/`。 | [事实] 官方格式说明 |
| 4 | `overrides` 是最后一层；客户端 `overrides → client-overrides`，服务端 `overrides → server-overrides`；同路径冲突时 overrides 胜出。 | [事实] Modrinth；[推断] CF 同义 |
| 5 | 顺序：**解析/校验 → 解析远端 → staging 下载与解压 → 后处理 → 安装加载器 → 原子提交 → 注册**。加载器失败不碰实例。 | [推断] 本项目设计 |
| 6 | 解压必须自己写，不要用 `ZipFile.ExtractToDirectory`；Zip Slip、绝对路径、保留名、ADS、大小写冲突、symlink、zip bomb 全要显式拒绝。 | [事实] .NET API 不覆盖这些策略 |
| 7 | **Rust 只下载，C# 解压并管事务**：解压是业务/安全策略，必须与实例语义和 SQLite 单写者同侧；Rust 保持“字节→文件”可替换。 | [推断] 与 02/05 契约一致 |
| 8 | 导入已有实例默认 **merge + managed 清单**；被移除文件进隔离区而非直接删；中断用 journal 回滚/续跑。 | [推断] 本项目设计 |

---

## 1. 格式与结构

### 1.1 CurseForge zip 与 Modrinth `.mrpack` 字段对照

| 字段/能力 | CurseForge `manifest.json` | Modrinth `modrinth.index.json` | 处理规则 |
|---|---|---|---|
| 根标记 | `manifestType=="minecraftModpack"` | 根级 `modrinth.index.json` | `manifest.json` 可能是资源包，必须看 `manifestType` |
| 格式版本 | `manifestVersion:1` | `formatVersion:1` | 只接受 1；更高版本报“启动器过旧”，不做 best-effort |
| 游戏版本 | `minecraft.version`（如 `1.20.1`） | `dependencies.minecraft` | 精确匹配；快照/别名原样传给加载器 |
| 加载器 | `minecraft.modLoaders:[{id:"forge-47.2.0",primary:true}]` | `dependencies` 的 `forge`/`neoforge`/`fabric-loader`/`quilt-loader` | 按 `id` 前缀映射；`primary` 只能一个，无 primary 取第一个并警告 |
| 包元数据 | `name`/`version`/`author` | `name`/`versionId`/`summary?` | 仅展示与命名；`author` 可空 |
| 文件列表 | `files:[{projectID,fileID,required,isLocked?}]` | `files:[{path,hashes,env?,downloads,fileSize}]` | CF 走 API；MR 直链；两者都做 schema 校验 |
| 路径 | **无**；API 返回 `fileName`，惯例落 `mods/` | `path` 为实例相对路径 | MR 保留原路径；CF 用 `mods/<basename>`，冲突加 `-<fileID>` |
| 完整性 | API `hashes[]`（通常 SHA1）+ `fileLength` | `hashes.sha1` + `hashes.sha512` 必填，`fileSize` | 至少 SHA1；MR 优先 SHA512；下载后必须校验 |
| 侧别 | `required:true/false`（可选） | `env:{client,server}`：`required/optional/unsupported` | 缺省视为双侧 required；`required:false` 等同 optional |
| 覆盖层 | `overrides:"overrides"`（可改名） | `overrides/`、`client-overrides/`、`server-overrides/` | 只接受单段相对目录名；层序见 §1.2 |
| 其他 | `modlist.html` 可忽略 | 无 | 不参与安装；未知字段保留/忽略 |

**CF 细节**：`manifestType` 严格匹配，`manifestVersion` 只认 1；`isLocked` 无语义定义，原样保存、不作安全判断。`files[].required` 缺省按 `true`，`false` 为可选、默认不装。`fileName` 来自 API，仍按不可信输入校验；`downloadUrl` 为 null 且 `/download-url` 仍拿不到 → `DISTRIBUTION_DENIED`，禁止重试或拼第三方镜像。**[事实/推断]** 批量用 `POST /v1/mods`、`POST /v1/mods/files`（各 100/批），URL 按 fileId 缓存；API key 只留 C#，不进 DownloadPlan/日志。

**MR 细节**：`hashes` 必须同时有 SHA1/SHA512；`downloads` 是非空 HTTPS 数组，官方上传白名单为 `cdn.modrinth.com`、`github.com`、`raw.githubusercontent.com`、`gitlab.com`，并要求至少 3 次重定向。`path` 不得含 `..`、不得以盘符/`/`/`\` 开头。`env` 缺省视为双侧 required；`server` 指专用服务器，server-only 文件不装客户端。`dependencies` 可能新增键，未知 loader 键必须 `UNSUPPORTED_LOADER`，不能忽略。**[事实/推断]**

### 1.2 overrides 层序

```
client:  overrides/  →  client-overrides/     （后者覆盖前者）
server:  overrides/  →  server-overrides/     （后者覆盖前者）
```
官方只明确 “server-overrides 在 overrides 之后”，client-overrides 是“仅客户端、同样的层叠方式”；客户端不应用 server-overrides，服务端不应用 client-overrides。`overrides` 与 `files[]` 同路径时 overrides 是最终内容，建议计划阶段删掉对应下载项；若仍下载，先校验再覆盖并记录 warning。**[事实/推断]**

### 1.3 MultiMC / Prism 实例导出

| 路径 | 作用 | 处理 |
|---|---|---|
| `instance.cfg` | 无 section 的 INI（Qt QSettings）：名称、内存、Java、启动参数 | 只读取展示/可选设置；`JavaPath`、`JvmArgs`、`GameArgs`、`JoinServerOnLaunch` 不可信，丢弃或让用户确认（可能执行任意二进制） |
| `mmc-pack.json` | `{formatVersion:1, components:[{uid,version,cachedName?,cachedRequires?}]}` | `formatVersion` 必须 1；数组顺序即合并顺序 |
| `.minecraft/` 或 `minecraft/` | 游戏目录（mods/config/saves/…） | 两种名字都探测；导出保留原样 |
| `patches/<uid>.json` | 旧版 Forge/LiteLoader 的 JSON patch | 仅旧实例；按 uid 读取，不执行未知字段 |
| `instance.json`（部分 Prism） | 实例元数据 | 可选，不依赖 |

**components → 加载器**（UID 来自 MultiMC/Prism；版本字段需样本复核）：`net.minecraft`→原版；`net.minecraftforge`→Forge（去掉 `1.20.1-` 前缀）；`net.neoforged`→NeoForge；`net.fabricmc.fabric-loader`→Fabric；`org.quiltmc.quilt-loader`→Quilt；`org.lwjgl3` 忽略；`optifine.OptiFine` 本地 jar 单独确认；`custom.jarmod.*` 跳过并警告，不自动执行。**[事实]**

MMC 原生包是**胖包**：mods 已在 `.minecraft/mods`，没有远端 `files[]`，也没有统一 hash；安装 = 安全解压 + 按 components 装加载器 + 注册。Prism 额外支持 CF、ATL、FTB Legacy、FTB App Import、Modrinth、Technic；MultiMC 官方 Wiki 只列 native/MR/Technic。**[事实/推断]**

### 1.4 其他常见格式（能确认多少写多少）

| 格式 | 可确认特征 | 处理与置信度 |
|---|---|---|
| ATLauncher 原生 | 实例根 `instance.json`，含 `launcher`（loader/loaderVersion）、`mods`；导出支持 CF/MR/MMC（源码 `Instance.export`） | 原生字段未逐一验证；导出格式复用 CF/MR/MMC 解析器 **[事实/未验证]** |
| FTB App / Legacy | FTB App 通过 FTB API 装自家包，也支持 CF zip 导入；不支持 Modrinth；Legacy 包可能以 CF 兼容 manifest 分发 | 不把 FTB 平台包当通用 zip；先探测 CF，再走 FTB API **[事实/未验证]** |
| Technic / Solder | Solder 返回 `{minecraft, forge, mods:[{name,version,md5,filesize,url}]}`；每个 mod 是小 zip，按顺序解到实例根；Forge 用 `bin/modpack.jar`，普通 mod 在 `mods/` | MD5 必校验；无中央 manifest 的旧包靠 `bin/modpack.jar` 嗅探 **[事实]** |
| HMCL / PCL2 | HMCL 标准导出可选 CF/MR，服务器自动更新包为 `server-manifest.json`+`overrides/`；PCL2 导入 `.zip`/`.mrpack`，自有导出格式未确认 | HMCL 旧 `pack.json` 不作主路径；PCL2 只按 CF/MR/MMC 探测 **[事实/未验证]** |
| packwiz | `pack.toml` + `index.toml`（TOML，常见于 git/HTTP） | 可作后续格式；先识别并明确“不支持”，不要误判成 CF **[事实]** |

### 1.5 统一描述（C# DTO）

```csharp
enum PackFormat { CurseForge, Modrinth, MultiMC, ATLauncher, Technic, HmclServer, Unknown }
enum LoaderKind { Vanilla, Forge, NeoForge, Fabric, Quilt, OptiFine }
enum PackSide { Client, Server }

sealed record PackFile(string Path, string? Url, string? Sha1, string? Sha512,
    long Size, bool Required, EnvSide Env, bool Disabled, FileOrigin Origin);
sealed record ModpackDescriptor(PackFormat Format, string Name, string Version, string? Author,
    string GameVersion, LoaderKind Loader, string? LoaderVersion, PackSide TargetSide,
    IReadOnlyList<PackFile> Files, IReadOnlyList<OverrideLayer> Overrides,
    IReadOnlyList<string> Warnings);
```
`Path` 永远是规范化相对路径（`/` 分隔、NFC、无 `..`）；`Origin` 区分 `Manifest`/`Override`/`Local`；`LoaderVersion` 为空只允许 Vanilla。

---

## 2. 格式探测（状态机）

```
输入（文件/目录）
  ├─ 目录：mmc-pack.json + instance.cfg ───────────────► MultiMC/Prism
  │        instance.json + mods/ ──────────────────────► ATLauncher（尝试）
  │        只有一个 zip ──────────────────────────────► 递归（深度+1）
  └─ 文件：读 magic
       ├─ PK\x03\x04 / PK\x05\x06 / PK\x07\x08 ───────► ZIP 分支
       ├─ Rar!\x1a\x07 / 7z\xbc\xaf\x27\x1c / 1f 8b ► 明确拒绝，提示解压后重试
       └─ 其他 ──────────────────────────────────────► UnknownPack

ZIP：枚举 central directory → 规范化 → 若所有条目共享单一顶层目录则剥掉
  ├─ 根 modrinth.index.json ──────────────────────────► Modrinth
  ├─ 根 manifest.json 且 manifestType=="minecraftModpack" ► CurseForge
  ├─ 根 mmc-pack.json + instance.cfg ─────────────────► MultiMC/Prism
  ├─ 根 instance.json + mods/ ────────────────────────► ATLauncher
  ├─ 根 server-manifest.json ─────────────────────────► HMCL Server（仅服务端）
  ├─ 根 bin/modpack.jar（± mods/） ───────────────────► Technic Legacy
  ├─ 根 pack.toml + index.toml ───────────────────────► packwiz（暂不支持）
  ├─ 只有一个内层 zip 且深度<2 ───────────────────────► 解包后重跑（共享预算）
  └─ 其他 ────────────────────────────────────────────► UnknownPack
```
- 先 magic 再 marker；扩展名只作 UI 提示。marker 匹配大小写不敏感，但若同时存在仅大小写不同的两个 marker，拒绝。**[事实]**
- 单层目录只剥一次；顶层是 `overrides`/`.minecraft`/`mods` 时不剥；剥后必须仍有 marker。
- 嵌套最多 2 层（外层包装 + 实际包），每层共享文件数/解压总量预算；绝不递归解 `mods/*.jar`。
- 多个不同格式 marker 同时出现 → `AMBIGUOUS_PACK`，不要猜；返回 `{format, confidence, reasons[]}` 给 UI。

---

## 3. 安装流水线

```
Import(path, mode, side)
  ├─ 1 Probe/Parse    读 zip/目录，格式判定，JSON schema 校验
  ├─ 2 Validate       游戏版本、loader 支持、路径策略、数量/大小预算
  ├─ 3 Resolve        CF: (projectID,fileID)→API；MR: downloads/hashes
  ├─ 4 Plan           Descriptor + DownloadPlan；overrides 去重；optional/disabled
  ├─ 5 Preflight      磁盘、权限、实例 id、大小写冲突、路径长度
  ├─ 6 Stage          staging/ 下载（Rust）+ 解 overrides（C#）+ 后处理
  ├─ 7 Loader         安装到 shared（幂等；Forge/NeoForge 需 JVM，见 02）
  ├─ 8 Verify         逐文件 hash/大小；最终文件集审计
  ├─ 9 Commit         new: rename；merge: journal 逐文件替换
  └─ 10 Register      SQLite + instance.json；失败由启动扫描补偿
```

**为什么加载器在提交前**：加载器最慢、最易失败（网络/JVM/processors），失败只删 staging，实例从未被触碰；提交是短临界区（new 一次 rename，merge 只做文件替换）；加载器产物在 `shared/{libraries,versions}`，与实例内容解耦，残留版本目录标 `.incomplete` 由 GC 清理。若用会写 `.minecraft` 的旧 installer，必须先让它写 staging/临时目录再合并，**禁止直接写活动实例**。**[推断]**

| 模式 | 行为 |
|---|---|
| `DownloadOnly` | 只解析 + 生成计划，可导出 JSON；不写实例、不装加载器 |
| `Materialize` | 下载到指定输出目录，保留相对路径；不注册、不装加载器 |
| `InstallNew` | 全流程；实例目录必须不存在 |
| `ImportMerge` | 合并进已有实例（默认）；按 managed 清单三方比较 |
| `ImportReplace` | 旧实例整体移到 `.trash/<ts>`，再按新实例安装；需二次确认 |
| `ServerPack` | `side=server`：过滤 `env.server=unsupported`、只用 server-overrides、装服务端加载器 |

**冲突策略**

| 场景 | 策略 |
|---|---|
| `overrides` vs `files[]` 同路径 | overrides 胜；计划阶段删下载项；记录 warning |
| `client-overrides` vs `overrides` | 客户端后者胜；服务端不读 client 层 |
| `server-overrides` | 仅服务端；在 overrides 之后覆盖 |
| `files[]` 两条同路径 / 仅大小写或 NFC 不同 | 拒绝（`DUPLICATE_PATH`），不采用“后者胜” |
| 已有实例：同名不同 hash（未托管 / 托管未变） | 前者默认备份后覆盖（保留模式则跳过）；后者按新 hash 替换 |
| 已有实例：托管但用户改过 | 冲突；默认保留用户文件，写 `.archoera/conflicts/<txid>.json`，UI 提供覆盖/跳过 |
| `foo.jar` 与 `foo.jar.disabled` 同时存在 | 冲突；不自动猜启用状态 |
| 新 manifest 删除了旧托管文件 | 移到 `.archoera/removed/<txid>/`，保留 30 天 |

**后处理**：`env` 过滤（客户端跳过 `client=unsupported`，服务端跳过 `server=unsupported`）；`optional`/`required=false` 默认不装，交互 UI 勾选，`--yes` 只装 required；`disabled` 用 `.disabled` 后缀（Prism/MultiMC/HMCL 生态常见），远端文件先按原路径下载校验再 rename 并记入 managed；服务端包不复制 `options.txt`/`screenshots/`/client-overrides，保留 `server.properties`、`world/`（若提供），不自动写 `eula.txt`；`mods/` 重复 modId 通过 `fabric.mod.json`/`META-INF/mods.toml` 检测并警告；配置整文件替换，不做键级 merge。

---

## 4. 解压安全（重点）

### 4.1 预算（可配置，先保守）

| 项 | 默认上限 | 说明 |
|---|---|---|
| 归档文件 / 单条目 | 8 GiB / 8 GiB | 超过拒绝 |
| 条目数 | 100,000 | 含目录；防 inode 耗尽 |
| 总解压量 | 16 GiB | 另需可用空间 ≥ payload×1.1 + 64 MiB |
| 压缩比 | 单条目 >1 MiB 且 >200:1 | 42.zip 类；更小的条目按绝对上限约束 |
| 嵌套深度 | 2 | 外层包装 + 实际包 |
| JSON 清单 / central directory | 8 MiB / 64 MiB | 防元数据炸弹 |

拒绝时返回稳定错误码（`PACK_ARCHIVE_BOMB`、`PACK_SYMLINK_ENTRY`、`PACK_PATH_ESCAPE`、`PACK_TOO_LARGE`）。前三个与“明显破坏性”相关，UI 用阻塞式警告对话框说明“试图写出实例目录/创建链接/解压到不合理大小”，并提示核对来源；普通损坏包仍用 toast。

### 4.2 逐条目校验清单

1. **名字解码**：优先 UTF-8 flag；无 flag 的非 ASCII 按 CP437 处理并 warning；非法 UTF-8 拒绝。
2. **分隔符/段**：`\` 一律视为 `/` 再分段；拒绝空段、`.`、`..`。
3. **绝对路径**：拒绝 `/`、`\`、`//`、`\\` 开头；拒绝 `^[A-Za-z]:` 盘符；拒绝 UNC。
4. **NUL/控制字符**：拒绝 `0x00–0x1F`、`0x7F`；JSON 清单同样拒绝 NUL。
5. **Windows 保留名**：任一段 basename（去扩展名、去尾随点/空格）不区分大小写等于 `CON/PRN/AUX/NUL/COM1-9/LPT1-9/CONIN$/CONOUT$`，含 `COM¹²³`/`LPT¹²³` 变体。
6. **尾随点/空格**：任一段以 `.` 或空格结尾 → 拒绝（Windows 会静默截断）。
7. **ADS**：任一段含 `:` → 拒绝（`file.jar:evil`）。
8. **长度**：单段 ≤255 UTF-16 单元；相对路径 ≤1024；绝对 ≤32767；Windows 超 240 用 `\\?\` 前缀，但仍受上限约束。
9. **大小写/NFC**：key = `NFC(path).ToUpperInvariant()`；重复拒绝；与目标已有文件比较同样规则。
10. **条目类型**：只允许普通文件/目录。读 `ExternalAttributes >> 16` 的 Unix mode；`S_IFLNK`（0xA000）、FIFO、设备、socket 拒绝；硬链接在 ZIP 无标准表示，不创建。
11. **加密/压缩**：GP bit0 置位拒绝；只接受 store(0)/deflate(8)；多磁盘/分卷拒绝；ZIP64 允许。
12. **重复条目**：规范化后同 key 重复 → 拒绝，不采用“后者覆盖”。
13. **符号链接祖先**：staging 必须是新建空目录；merge 时逐级检查父目录，`ReparsePoint`/symlink 拒绝；写入用 `FileMode.CreateNew`，绝不跟随已有链接。
14. **macOS 垃圾**：跳过 `__MACOSX/`、`._*`、`.DS_Store`、`Thumbs.db`、`desktop.ini`；不保留 xattr/resource fork。
15. **实际字节**：复制时累计 actual；超过 `entry.Length` 或全局预算立即 abort；声明长度不可信。

### 4.3 解压伪代码（C#）

```csharp
foreach (var e in zip.Entries) {
    var rel = ValidateEntry(e);                 // 上述 1–12；非法抛 PackSecurityException
    if (rel is null) continue;
    var dst = SafeJoin(staging, rel);           // GetFullPath + 相对路径前缀校验
    if (IsDir(e)) { Directory.CreateDirectory(dst); continue; }
    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
    using var input  = e.Open();
    using var output = new FileStream(dst, FileMode.CreateNew, FileAccess.Write,
                                      FileShare.None, 64 * 1024, FileOptions.SequentialScan);
    long actual = 0; var buf = new byte[64 * 1024]; int n;
    while ((n = input.Read(buf, 0, buf.Length)) > 0) {
        actual += n;
        if (actual > e.Length || actual > globalRemaining) throw new PackSecurityException("BOMB");
        output.Write(buf, 0, n);
    }
    output.Flush();                             // staging 可丢；提交前再 fsync
    SetMode(dst, executableAllowlist.Contains(rel) ? 0755 : 0644); // Windows 忽略
}
```
`SafeJoin` 不要只写 `StartsWith`：
```csharp
var full = Path.GetFullPath(Path.Combine(root, rel));
var back = Path.GetRelativePath(root, full);
if (back == ".." || back.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(back))
    throw new PackSecurityException("ESCAPE");
```
Windows/macOS 比较用 `OrdinalIgnoreCase`；Linux 建议也按大小写不敏感拒绝以保证可移植。不要用 `ZipFile.ExtractToDirectory`：它不做保留名/ADS/case/重复/symlink/预算策略，失败还留半成品。.NET `ZipArchive` 不暴露 local header offset，重叠条目检测有限；高安全场景换能暴露 offset 的解析器（如 MIT SharpZipLib）或自写 EOCD 校验。**[事实/未验证]**

### 4.4 权限与平台

Unix 目录 0755、文件 0644；仅 `bin/*.sh` 等白名单可 0755；清除 setuid/setgid/sticky。Windows 忽略执行位，不写 ACL；拒绝 ADS/保留名/尾随点空格，长路径用 `\\?\`，只读属性导致替换失败时先报错。macOS 不还原 resource fork/xattr，文件名按 NFC 存储，避免 HFS+/APFS 归一化碰撞。

---

## 5. 事务与恢复

### 5.1 目录与 journal

```
instances/<id>/
  instance.json                 # 我们的实例元数据（可重建）
  .minecraft/                   # 或 minecraft/
  .archoera/
    managed.json                # path → {sha1, origin, disabled}
    tx/<txid>/journal.json      # 事务日志
    conflicts/<txid>.json
    removed/<txid>/...
instances/.staging/<txid>/      # 与实例同卷，提交时可 rename
```
```json
{"txid":"01J...","instance":"abc","mode":"new|merge","phase":"staging|applying|committed|rolledback",
 "archiveSha256":"...","createdDirs":["config"],
 "ops":[{"op":"replace","dest":"mods/a.jar","tmp":".staging/.../mods/a.jar",
         "backup":"tx/.../backup/0","sha1":"..."}]}
```

### 5.2 提交/回滚伪代码

```text
Commit(tx):
  journal.phase = applying; write+fsync(journal)
  for op in ops:                       # 顺序执行；目录先建
    ensure parent dirs (记录 createdDirs)
    if dest exists:
      backup = tx/backup/<n>; link(dest, backup) or copy(dest, backup)  # 同卷硬链接省空间
    fsync(op.tmp); rename(op.tmp, dest) # 先刷数据，再同卷原子替换；Windows 用 MoveFileEx
    fsync(parent)                       # 目录 fsync 在 Unix；Windows 尽力
  journal.phase = committed; fsync
  delete staging + backup

Rollback(tx):
  for op in reverse(ops):
    if backup exists: delete(dest); rename(backup, dest)
    else if dest created by tx: delete(dest)
  for dir in reverse(createdDirs): rmdir if empty
  journal.phase = rolledback; fsync
```
- **new**：staging 完成后 `rename(staging, instances/<id>)`，目标必须不存在；失败删 staging，实例不存在。**merge**：不能整目录 rename，逐文件 op + backup + journal，回滚逆序恢复。
- **注册失败**：实例目录已提交但 SQLite 写失败时，启动扫描 `instances/*/instance.json` 收养孤儿；DB 有行但目录缺失标 `missing`。**[推断]**
- **续跑**：Rust 的 `.part` 管下载断点；staging 的 `stage.json` 记 phase 与已完成条目；解压中断优先从内容缓存/已下载文件重建 staging；提交中断由 journal 回滚。**[推断]**
- **磁盘满**：预检 `downloadBytes + uncompressedBytes*1.1 + 64 MiB`（同卷）；`ENOSPC`/Windows 112 → 停写、删 staging 临时文件、按空间决定保留 `.part`；未到提交阶段不会污染实例。
- **权限/锁**：`UnauthorizedAccessException`/Windows 5 → `PERMISSION_DENIED`；共享冲突 32/33 短重试 5 次（100ms 起）→ `FILE_LOCKED`；绝不删除用户已有 dest。
- **导入已有实例**：默认 merge；`ImportReplace` 先把旧目录 rename 到 `.trash/<ts>`（同卷），失败可整体恢复；更新用 `managed.json` 做旧 manifest/新 manifest/本地 hash 三方比较，而不是全量覆盖。

---

## 6. 与 Rust 下载器的职责边界

**建议：Rust 只下载；C# 解压、后处理、事务。**
1. 解压策略是安全与产品语义（目录、环境、disabled、冲突），不是“字节→文件”；放 Rust 会把业务塞进可替换层。
2. 避免两套 Zip Slip/保留名/大小写实现；C# 已拥有路径规则、SQLite 单写者和实例模型。
3. Rust 的 `.part`/Range/并发优势在下载；解压是顺序 IO，C# `ZipArchive` 足够，NativeAOT 可用。
4. 失败语义统一：Rust 返回 `DISK_FULL/FILE_LOCKED/HASH_MISMATCH`，C# 决定回滚/续跑；若 Rust 解压，错误要跨 ABI 再翻译一次。
5. 例外：加载器 installer 的嵌套解包仍由 C# 加载器模块处理（需要 processors/占位符），不复用通用解压器。

DownloadPlan 的 `dest` 相对 staging 根；Rust 不需要知道“这是整合包”。overrides 不进 DownloadPlan，由 C# 直接解到 staging；远端文件可先落内容缓存再硬链接到 staging（`mode:"link"`），便于重试与去重。

## 7. 导入与导出

### 7.1 导出包含/排除

| 默认包含 | 默认排除 |
|---|---|
| `mods/`、`config/`、`defaultconfigs/`、`kubejs/`、`scripts/`、`resourcepacks/`、`shaderpacks/`、`options.txt`、`optionsof.txt`、`patchouli_books/`、`structures/`、`datapacks/`、`icon.png` | `logs/`、`crash-reports/`、`debug/`、`.mixin.out/`、`hs_err_pid*`、`replay_pid*`、`screenshots/`、`cache/`、`downloads/`、`.archoera/`、`*.log`、`*.tmp` |
| `saves/`（用户勾选，默认关）；`server.properties`、`world/`（仅服务端包） | `usercache.json`、`usernamecache.json`、`launcher_profiles.json`、`launcher_accounts*.json`、`servers.dat`（含地址，默认关）；`__MACOSX/`、`.DS_Store`、`Thumbs.db`、`desktop.ini`、`.git/` |

`config/` 可能含 token/密码/server key：导出前提示，可选“扫描敏感键名”，不因命中就阻断。`mods/` 再分发受各 mod 许可证约束；优先“配方”格式，胖包明确警告。**[事实/推断]**

### 7.2 三种导出格式的取舍

| 格式 | 内容 | 体积 | 可复现 | 兼容 | 主要风险 |
|---|---|---|---|---|---|
| `.mrpack` | `modrinth.index.json` + overrides；远程文件只存 URL/hash | 小 | 高 | Modrinth/Prism/ATL/HMCL | 上传仅 4 个域名；本地/非 Modrinth mod 只能进 overrides |
| CF zip | `manifest.json` + overrides；files 只存 projectID/fileID | 小 | 高 | CF/Prism/FTB/HMCL | 需 CF API；非 CF mod 只能进 overrides；分发条款 |
| MultiMC zip | 整个实例：`instance.cfg` + `mmc-pack.json` + 游戏目录 | 大 | 中 | MultiMC/Prism | 泄露存档/服务器/配置；无法按需下载 |

**生成可复现 manifest**：按规范化相对路径 `Ordinal` 排序；拒绝 symlink；流式计算 SHA1/SHA512/size；JSON 手写固定属性顺序，不写时间戳/主机名/绝对路径。MR 用 `formatVersion=1`、`game="minecraft"`、`versionId` 用实例包版本（不用当前时间）；已知远程文件进 `files[]`，其余进 `overrides/`。CF 通过 fingerprint 匹配到的进 `files[]`，否则进 `overrides/`；`manifestVersion=1`、`manifestType="minecraftModpack"`、`overrides="overrides"`。MMC 删 `JavaPath`/账户/绝对路径，只保留可映射 components，不写 `.archoera/`。字节级可复现需固定时间戳 `1980-01-01`、固定顺序、`Store` 或固定 deflate 参数；否则只保证内容/manifest 可复现。**[推断]**

**disabled**：保留 `.disabled` 后缀；MR 可把 `path` 写成 `mods/foo.jar.disabled`（hash 仍对应文件），CF 在安装后 rename；两边都记录原文件名，避免更新丢失。

---

## 8. 参考实现（只读，不抄代码）

| 项目/许可 | overrides | 加载器顺序 | 失败/恢复 | 可借鉴点 |
|---|---|---|---|---|
| Prism / MultiMC（GPL-3） | 包解到实例；MR/CF 的 overrides 最后铺；native 胖包直接带文件 | `mmc-pack.json` components 顺序 + patches；加载器由组件图决定 | 安装失败通常删新实例；更新时覆盖配置（社区反馈会丢客户端设置） | 组件化版本模型；导入格式多；不要照搬“更新即覆盖” |
| HMCL（GPL-3） | ModpackProvider 按 CF/MR 解析；overrides 铺到实例 | 先建版本/加载器，再落文件；服务端包用 `server-manifest.json` | 任务失败清理未完成文件；provider 接口清晰 | provider 分层、错误可读 |
| Modrinth App（GPL-3） | `.mrpack` 解包，overrides/client-overrides 分层 | 先解析依赖，再装 loader，再落文件 | 安装 job 失败清理；更新按 manifest 对比 | 配方优先、更新模型 |
| XMCL（MIT） | 支持 CF/MR/MMC/FTB 导入；导出 MR/CF | 解析后装 loader，再铺文件 | 导入有进度与错误提示；早期版本有重复实例问题 | **唯一可代码级借鉴**：格式探测、CF/MR 映射、导出；安全层仍重写 |
| ATLauncher（GPL-3） | 原生实例 + 导出 CF/MR/MMC | 按实例 launcher 类型装 loader | 下载队列；导出在临时目录组装 | 三格式互转 |
| CurseForge App（闭源） | manifest + overrides；CF API 解析 | 先 loader 后文件（观察） | 失败重试/重装 | 只作行为参考 |

共同点：都把 overrides 当最终层；都把加载器安装与文件落盘分开；多数对“导入已有实例”只覆盖，不做三方合并/回滚——这是可差异化的点。不要因为参考实现没做路径校验就省略。

---

## 9. 测试用例（20 条，含恶意 zip 样本）

| # | 样本 | 期望 |
|---|---|---|
| 1 | 合法 MR：`modrinth.index.json` + `overrides/config/a.toml` + `client-overrides/options.txt` + `server-overrides/server.properties` | 客户端只应用 overrides+client；服务端只应用 overrides+server |
| 2 | 合法 CF：fabric primary + `overrides/`；CF API mock 返回 fileName/hash/url | 解析 `(projectID,fileID)`；落 `mods/<fileName>`；不请求最新版 |
| 3 | MMC：`instance.cfg` + `mmc-pack.json`（net.minecraft 1.20.1 + net.minecraftforge 47.2.0）+ `.minecraft/mods/a.jar` | 识别 MMC；映射 forge 47.2.0；两种游戏目录名都处理 |
| 4 | ATL `instance.json` / Technic `bin/modpack.jar`+`mods/` / HMCL `server-manifest.json` | 分别识别；Technic 校验 MD5；HMCL 仅服务端 |
| 5 | `.mrpack` 改名 `.zip`；外层 zip 套单层目录再套内层 zip | 按内容识别；最多剥 2 层；不递归 `mods/*.jar` |
| 6 | `formatVersion:2` / `manifestVersion:2` / `game:"terraria"` | `UNSUPPORTED_FORMAT`；不尝试安装 |
| 7 | `../../evil.txt`、`mods/../../../evil` | 拒绝整包；staging 不落越界文件 |
| 8 | `/etc/passwd`、`C:\Windows\evil`、`\\server\share\x`、`//host/x` | 拒绝；记录 `ABSOLUTE_PATH` |
| 9 | `..\\..\\evil.txt`、`mods\\..\\..\\evil` | 反斜杠归一化后仍拒绝 |
| 10 | 条目名含 `\0`、`\x01`、`\x7f`；JSON 字符串含 NUL | 拒绝；不进入文件系统 |
| 11 | `CON`、`aux.txt`、`COM1.jar`、`mods/LPT9`、`foo.`、`bar ` | 全部拒绝（含扩展名/尾随点空格变体） |
| 12 | `mods/a.jar:evil`、`config/x:stream` | 拒绝 ADS |
| 13 | `Mods/A.jar` 与 `mods/a.jar`；`café` NFC 与 NFD | 归一化后重复 → 拒绝 |
| 14 | Unix mode `0xA1FF` 的 symlink 指向 `/etc/passwd`；FIFO/设备条目 | 拒绝；不创建链接、不读目标 |
| 15 | 同一路径两条不同内容（central directory 重复） | `DUPLICATE_ENTRY`；不采用后者 |
| 16 | 42.zip 类高压缩比、单条目 >8 GiB；或 200k+ 空条目 | 触发预算，abort；删 staging；UI 说明原因 |
| 17 | 嵌套 3 层 zip，每层接近预算；或 quine 式自复制 | 深度/共享预算拦截；不展开 |
| 18 | 加密 zip（GP bit0）、压缩方法 12/14/93、截断 central directory | 拒绝并给明确错误；不部分解压 |
| 19 | MR `env.server=unsupported`+`client=required`；CF `required:false`；`mods/a.jar.disabled`；server-overrides 同名覆盖 | 客户端跳过 server-only；optional 默认不装；disabled 保持；服务端层序正确 |
| 20 | 已有实例：用户改过托管文件 + 新包更新；提交中 kill 进程；加载器安装失败 | 冲突保留用户文件并记录；重启后 journal 回滚/续跑；加载器失败不改实例目录 |

补充运维用例（不计入 20）：磁盘满、只读目录、Windows 文件被游戏占用、导出 500 MB 包、macOS 解 Windows 包。

---

## 10. 未验证项（写代码前必须确认）

1. CF `manifest.json` 无官方 schema；`isLocked`、`recommendedRam`、根级 `projectID/fileID` 语义未定义。**[未验证]**
2. CF API 是否有批量 `download-url` 端点、批大小与 403/429 行为；`allowModDistribution=false` 时 `/download-url` 的确切响应。**[未验证]**
3. MR `formatVersion` 升级策略；`env` 缺省是否严格双侧 required；client/server overrides 同时存在时的官方优先级。**[未验证]**
4. MMC `.minecraft` vs `minecraft/` 的版本差异、完整 UID 列表、`cachedRequires` 语义。**[未验证]**
5. ATL 原生 `instance.json` 字段、FTB Legacy 包结构、PCL2 自有格式、HMCL 旧 `pack.json`。**[未验证]**
6. .NET `ZipArchive` 对重叠条目/local header 不一致的检测；Windows 上 Unix mode 来源；长路径在 Win10/11 的行为。**[未验证]**
7. 加载器 installer 是否写实例目录（尤其 1.7.10/旧 Forge）；若会，staging 方案需实测调整。**[未验证]**
8. Prism/HMCL/Modrinth App 失败恢复的精确行为仅按文档/issue/观察总结，未逐行核对。**[未验证]**
9. 胖包导出中 mods 的再分发合规性；CF API 条款对导出/转格式的限制；服务端包对 `optional` 的默认选择、`world/` 是否默认包含、`eula.txt` 是否生成。**[未验证]**
