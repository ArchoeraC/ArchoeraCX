# 10 · 下载源矩阵与 `DownloadPlan` 边界

> 目标：把「从哪下、下什么、什么不能下、下完怎么验」写成一张可执行的源矩阵，并明确 C#（生产者）与 Rust（执行者）的边界。
> 范围：原版、Forge/NeoForge、Fabric/Quilt、Modrinth、CurseForge、Java runtime、镜像，以及 Mod/光影/资源包的源选择。
> 标记：**[事实]** 官方文档/源码/实测；**[推断]** 基于协议与实现的判断；**[未验证]** 需要真实 key/样本/实机确认。
> 说明：表格为压缩索引，未逐格重复标记；关键结论与风险项在正文/§7 标注。表格字段若来自官方文档或本次实测，按 [事实] 处理；带「可能/需确认」的按 [推断]/[未验证]。
> 实测日期 **2026-10-21**。配套：[05-download-engine.md](05-download-engine.md)（Rust 执行器）、[06-modpack-install.md](06-modpack-install.md)（整合包）、[03-mod-search.md](03-mod-search.md)（搜索/更新）、[09-forge-generated-artifacts.md](09-forge-generated-artifacts.md)（生成物）。
> 发行/合规策略以 [`../09-distribution-and-download-policy.md`](../09-distribution-and-download-policy.md) 与 [`07-compliance-gate.md`](07-compliance-gate.md) 为准；本文只谈技术源与计划字段。
>
> **实现状态（2026-10-21）**：Rust `DownloadPlan` 执行器 MVP、C# `DownloadPlan` 契约、Modrinth provider、Mojang 原版计划、Forge/NeoForge installer/依赖计划、Mojang Java runtime 计划均已落地；CurseForge 因鉴权后置暂缓；计划尚未提交到 Rust 执行器。

---

## 0. 结论速览

| # | 结论 | 依据 |
|---|---|---|
| 1 | **源分三类：官方必需（Mojang/加载器/Java）、内容平台（Modrinth/CF）、用户提供（本地文件/JDK/账户）**。三类的默认等级、鉴权、镜像策略完全不同。 | [事实] 09 政策、06 生态接口 |
| 2 | **Modrinth 默认内容源，CurseForge 补充源**。Modrinth 无 key、有 SHA-512/直链/批量 hash 反查；CF 受 key、配额、作者禁转限制。 | [事实] 03 §0–§2 |
| 3 | **`DownloadPlan` 只描述「远端字节 → 本地文件」**。生成物（Forge `-client.jar`、`client-extra`、mappings）绝不进入下载计划；它们属于 loader 安装事务。 | [推断] 05 §1、09 §3 |
| 4 | **每个文件必须带 hash 和 size**；镜像只提供字节，不提供信任。镜像 hash 不匹配直接丢弃并回退官方。 | [事实] 05 §3、09 政策 §4.2 |
| 5 | **协议 JSON 没有 hash 的（Fabric/Quilt profile）不能当不可变文件**；按 TTL/ETag 缓存，合并结果由 C# 记录来源与抓取时间。 | [事实] 02 §1、06 §4；[推断] 缓存策略 |
| 6 | **失败要分类**：403 可能是 key 无效、配额、签名 URL 过期或作者禁转；429 才是明确限流；`DISTRIBUTION_DENIED` 不可重试、不可换第三方镜像。 | [事实] 03 §2.4；[未验证] CF 403 具体语义 |
| 7 | **CF key 永不进 Dart/日志/计划/构建产物**；用户自备或独立网关；`official-cn` L0 不提供内容下载。 | [事实] 03 §2.4、09 政策 §4.1 |
| 8 | **Java runtime 不只一个 URL**：Mojang `all.json` 按平台/组件给 manifest，再按文件下载；必须校验每文件 SHA-1/size，保留 executable 位。 | [事实] 本次实测 `all.json` + `java-runtime-delta` manifest |

**一句话**：下载源不是 URL 列表，而是「来源 + 鉴权 + 哈希 + 条款 + 失败语义」的组合；Rust 只执行，C# 负责选择与解释。

---

## 1. 源矩阵

### 1.1 游戏、加载器、运行时

| 源 | 典型端点 | 鉴权/UA | 下载什么 | 本地生成什么 | 哈希/校验 | 限流/镜像 |
|---|---|---|---|---|---|---|
| **Mojang 版本清单** | `https://piston-meta.mojang.com/mc/game/version_manifest_v2.json` | 无 key；建议 UA | 版本列表、`latest`、每版本 JSON 的 url/sha1 | 本地缓存/索引 | 每版本 JSON 的 `sha1` | 未见公开速率；缓存 + 条件请求；BMCLAPI 可镜像 |
| **Mojang 版本 JSON** | `piston-meta.../v1/packages/<sha1>/<id>.json` | 无 | 版本元数据、libraries、assetIndex、downloads.client、logging、javaVersion | 合并后的有效版本（C#） | 清单给的 `sha1` | 同上；快照也会变，按 sha1 缓存 |
| **Mojang client jar** | `piston-data.../v1/objects/<sha1>/client.jar` | 无 | 原版 client jar | 不解压；只作为输入 | `downloads.client.sha1/size` | 大文件；BMCLAPI 可镜像 |
| **Mojang libraries** | `https://libraries.minecraft.net/<maven path>` | 无 | 版本 JSON 的每个库 | natives 解压（C#） | `downloads.artifact.sha1/size` | 可镜像；必须官方 hash |
| **Mojang assets** | `https://resources.download.minecraft.net/<xx>/<hash>` | 无 | 资源对象 | 虚拟/legacy 映射（C#） | 索引里的 `sha1/size` | 大量小文件；BMCLAPI 可镜像 |
| **Mojang logging** | `piston-data.../objects/<sha1>/client-1.12.xml` 等 | 无 | log4j 配置 | 不生成 | `logging.client.file.sha1/size` | 小文件；必须用官方配置 |
| **Mojang Java runtime** | `https://launchermeta.mojang.com/v1/products/java-runtime/<hash>/all.json` | 无 | 每平台/组件的 manifest，再下文件 | 按 manifest 落位到托管 runtime（C#） | 每层 `sha1/size`；文件 `downloads.raw/lzma` | 大文件、多文件；可缓存；镜像需谨慎 |
| **Adoptium（可选）** | `https://api.adoptium.net/v3/binary/latest/{major}/ga/{os}/{arch}/jre/hotspot/normal/eclipse` | 无 key | JRE 归档 | 解压到托管目录 | 归档 SHA-256 + 签名 | 未见公开速率；遵守条款；不打包 Oracle |
| **Forge Maven** | `https://maven.minecraftforge.net/` | 无 key；UA | installer、processor 工具、库、shim | installer 抽取 + processors（见 09） | installer/库 `sha1/size`；输出见 09 | 未见公开速率；BMCLAPI 镜像可用（本次样本） |
| **NeoForge Maven** | `https://maven.neoforged.net/releases/` | 无 key；UA | installer、库 | 同上 | `sha1/size` | 未见公开速率；BMCLAPI 镜像可用（本次样本） |
| **Fabric Meta** | `https://meta.fabricmc.net/v2/versions/loader/{mc}/{loader}/profile/json` | 无 key；UA | profile JSON + loader 库 | 合并进有效版本（C#） | profile JSON 无官方 hash；库有 sha1 | 未见公开速率；缓存/TTL；可镜像 |
| **Quilt Meta** | `https://meta.quiltmc.org/v3/versions/loader/{mc}/{loader}/profile/json` | 无 key；UA | profile JSON + loader 库 | 合并（C#） | profile JSON 无官方 hash；库有 sha1 | 同上；`net.fabricmc:*` 走 Fabric Maven |

### 1.2 内容平台

| 源 | 端点 | 鉴权 | 下载什么 | 本地生成什么 | 哈希 | 限流/镜像 |
|---|---|---|---|---|---|---|
| **Modrinth** | `https://api.modrinth.com/v2/`；文件在 `cdn.modrinth.com`/白名单域名 | 无 key；**必须可识别 UA** | 项目/版本元数据、文件直链 | 本地 DTO/缓存/更新状态 | `sha1` + `sha512`；优先 SHA-512 | **300 req/min/IP**（含 token）；`X-Ratelimit-*`；429 + `Retry-After`；不镜像 |
| **CurseForge** | `https://api.curseforge.com/v1/`；文件在 `edge.forgecdn.net` 等 | **`x-api-key`**；无匿名 | 项目/文件元数据、`downloadUrl`/`download-url` | 本地 DTO/缓存；fingerprint | `sha1` + `md5`；作者禁转时可能无 URL | 官方未公布数值；社区反馈超限可能 403；不镜像 |
| **BMCLAPI（镜像）** | `https://bmclapi2.bangbang93.com/` | 无 key | 仅镜像官方游戏/加载器文件 | 不生成 | **必须官方 hash** | 社区维护；遵守说明；失败回退官方；不镜像 Modrinth/CF 内容 |
| **用户自定义镜像** | 用户填根地址 | 由用户配置 | 仅官方 artifact | 不生成 | 官方 hash 白名单 | 需白名单 + hash；不得用于 CF/Modrinth 内容 |

**发行通道**：`official-cn`（L0）只读元数据，不下载内容；`official-global`（L1）允许官方必需内容，L2 的 Mod/整合包/资源包/光影需应用内确认；`dev`（L2）用于开发。源矩阵中的「可用」不等于当前构建默认允许。**[事实]** 见 [`../09-distribution-and-download-policy.md`](../09-distribution-and-download-policy.md)。

### 1.3 下载 vs 生成 vs 用户提供

| 场景 | 下载 | 本地生成 | 用户提供 |
|---|---|---|---|
| 原版 | 清单、版本 JSON、client jar、libraries、assets、logging、Java runtime | 有效版本合并、natives 解压、LaunchPlan | 账户 token、可选 JDK、options |
| Forge/NeoForge | installer、processor 工具/库、原版 client/mappings、shim（1.21.1） | `install_profile`/`version.json` 抽取、`client.lzma` 抽取、`MC_SLIM/MC_EXTRA/MC_SRG/MC_OFF/PATCHED` 等（09） | 可选 JDK；**不提供预生成 `-client.jar`** |
| Fabric/Quilt | profile JSON、loader 库 | 合并后的有效版本 | 无 |
| Mod/资源包/光影 | 平台元数据 + 文件 | 本地扫描/依赖状态/更新状态 | 本地导入的文件、配置 |
| Java | Mojang runtime 或 Adoptium | 解压/托管目录/版本探测 | 用户指定 JDK |
| CurseForge | 元数据 +（若允许）文件 | 本地缓存/指纹 | **API key** |

---

## 2. 各源细节

### 2.1 Mojang 原版

**版本清单**：`version_manifest_v2.json` 的 `versions[]` 给 `id/type/url/sha1/releaseTime/complianceLevel`；`latest.release/snapshot` 只用于 UI。**[事实]** 清单本身没有官方 signature；依赖 HTTPS + 缓存 + 版本 JSON 的 SHA-1。**[推断]**

**版本 JSON**关键字段：

| 字段 | 下载/生成 | 说明 |
|---|---|---|
| `downloads.client` | 下载 | `url/sha1/size`；client jar |
| `libraries[]` | 下载 | `downloads.artifact`；`natives` 需本地解压 |
| `assetIndex` | 下载 | 索引 JSON 的 `url/sha1/size/totalSize`；对象按 hash 下 |
| `logging.client.file` | 下载 | `url/sha1/size/type`；Log4Shell 必须用官方 |
| `javaVersion.component/majorVersion` | 读取 | 决定 runtime 组件与最低 Java |
| `arguments`/`minecraftArguments` | 读取 | 启动期求值；不下载 |

**资产**：`https://resources.download.minecraft.net/<hash[0:2]>/<hash>`；按索引的 `objects` 校验 SHA-1；虚拟/legacy 目录由 C# 映射。**[事实]** 大量小文件，Rust 用 HTTP/2 + 连接池，不要逐文件新建连接。**[推断]**

**日志配置**：1.7–1.18 必须使用版本 JSON 的 `logging.client`，不要自己拼 `-Dlog4j.configurationFile`；下载后按 SHA-1 校验。**[事实]**

**Mojang mappings**：版本 JSON 的 `downloads.client_mappings`（或 `client_mappings`）有 `url/sha1/size`。它**不一定**等于 Forge `DOWNLOAD_MOJMAPS` 的输出：Forge 1.21.1 的该处理器带 `--sanitize`，原始 `client_mappings` 的 SHA-1（`2244b6...`）与 `MOJMAPS_SHA`（`2a8064...`）不同。默认跑处理器；只有无 sanitize 且输出哈希可验证时才可直下替代（见 09 §5.4）。**[事实/推断]**

### 2.2 Java runtime

Mojang `all.json` 的结构（2026-10-21 实测）：

```
{
  "gamecore": { "java-runtime-alpha": [], ... },   // 空数组是正常的；不是错误
  "linux": { "java-runtime-delta": [{ availability, manifest:{sha1,size,url}, version:{name,released} }] },
  "windows-x64": {...},
  "mac-os-arm64": {...}
}
```

- 顶层是**平台**（`linux`/`linux-i386`/`mac-os`/`mac-os-arm64`/`windows-x64`/`windows-x86`/`windows-arm64`/`gamecore`），不是组件。**[事实]**
- 选 `os + arch + javaVersion.component`；拿到 `manifest.url` 后再下载该平台的 runtime manifest。**[事实]**
- runtime manifest 的 `files` 是路径 → `{type, downloads:{raw,lzma}, executable}`；`raw`/`lzma` 都有 `sha1/size/url`。**[事实]** 我们优先 `raw`，避免在 Rust 里解 LZMA；若某文件只有 `lzma`，解压由 C# 做，Rust 仍只下压缩包。`executable=true` 要保留执行位。**[推断]**
- 版本示例：`java-runtime-delta` 21.0.7；1.20.5+ 需要 21，1.20.1 需要 17；以版本 JSON 的 `javaVersion` 为准。**[事实/推断]**
- Adoptium 作为可选源：`/v3/binary/latest/...`，归档 SHA-256，Temurin GPLv2+CE；不要打包 Oracle JDK。**[事实]**

> 注意：`gamecore` 下全空数组不代表 runtime 不可用；按平台键取。若实现只读 `gamecore`，会误判「没有 Java runtime」。**[事实]**

### 2.3 Forge / NeoForge Maven

| 项 | Forge | NeoForge |
|---|---|---|
| Maven 根 | `https://maven.minecraftforge.net/` | `https://maven.neoforged.net/releases/` |
| installer | `.../net/minecraftforge/forge/<game>-<loader>/forge-<game>-<loader>-installer.jar` | `.../net/neoforged/neoforge/<loader>/neoforge-<loader>-installer.jar` |
| processor 工具 | `installertools`、`jarsplitter`、`ForgeAutoRenamingTool`、`binarypatcher` | `installertools`、`jarsplitter`、`AutoRenamingTool`、`binarypatcher` |
| shim | 1.21.1 有 `:shim`，在 `install_profile.libraries` | 无（本样本） |
| `-client.jar` | 官方 Maven 404（实测 1.21.1） | 官方 Maven 不提供（不下载） |
| 生成物 | 见 09 | 见 09 |

- installer 约 6–7 MB；`install_profile.json` 与 `version.json` 是安装期的元数据，不是下载库。**[事实]**
- Forge 1.12.2 的 `install_profile.json` 有注释请求「不要自动化下载/安装 Forge」；我们走官方 Maven、标注来源、不内置/不自建镜像。这是灰区，不是明确许可；详见 02 §2.7。**[事实/推断]**
- `install_profile.libraries` 是**安装期 classpath**；`version.json.libraries` 是**启动期 classpath**，两者不能混。**[事实]**
- Maven 路径可由 `install_profile.libraries[].downloads.artifact` 直接得到；没有 `downloads` 的库按坐标拼 URL。**[推断]**
- 未见官方速率限制文档；按普通 Maven 客户端礼貌使用（串行/少量并发、UA、缓存）。**[未验证]**
- BMCLAPI 的 `/maven/...` 路径可镜像 Forge/NeoForge；本次样本下载成功；**hash 仍按官方/原始值校验**。**[事实]** 我们**不自建 installer 镜像、不内置 installer**；BMCLAPI 只作为可选第三方源，失败回退官方。

### 2.4 Fabric / Quilt

```
GET https://meta.fabricmc.net/v2/versions/loader/{mc}/{loader}/profile/json
GET https://meta.quiltmc.org/v3/versions/loader/{mc}/{loader}/profile/json
```

- `profile/json` 是可直接合并的版本 JSON：`id`、`inheritsFrom`、`mainClass`、`arguments`、`libraries`（含 sha1/size/url）。**[事实]**
- Fabric `launcherMeta` 额外给 `libraries.common/server/client` 与 `min_java_version`；SJMCL 用这个形态。**[事实]**
- profile JSON **没有官方顶层 hash**（本次 Fabric 实测；Quilt 同构，但未复测）：不要把它当不可变 artifact；缓存用 TTL/ETag（若服务端给），合并缓存键用 loader 版本 + MC 版本 + profile 内容 hash（抓取后计算）。**[事实/推断]**
- Quilt 的 `net.fabricmc:*` 不在 Quilt Maven，按前缀切到 Fabric Maven，否则 404。**[事实]**
- 两者都不需要 Java 跑处理器；只需下载库。**[事实]**
- 速率限制未见公开数值；设置 UA、缓存、失败退避。**[未验证]**

### 2.5 Modrinth

| 项 | 规则 |
|---|---|
| API | `https://api.modrinth.com/v2/` |
| 搜索 | `/search?query=&facets=&index=&offset=&limit=`；`limit<=100`；内层 OR、外层 AND |
| 详情/版本 | `/project/{id}`、`/project/{id}/version`、`/version/{id}` |
| 批量 hash | `POST /version_files`、`POST /version_files/update` |
| 文件 | `files[].url`（CDN/白名单）、`hashes.sha1/sha512`、`size`、`filename`、`primary` |
| UA | 必须可识别应用，如 `ArchoeraCX/0.0.1+1 (contact@example.com)`；不要伪装 okhttp |
| 限流 | **300 请求/分钟/IP**；响应头 `X-Ratelimit-Limit/Remaining/Reset`；429 读 `Retry-After` |
| 缓存 | 搜索 60s、详情 10min、版本 5min、标签 24h、更新检查 6h；有 ETag 用条件请求，没有就 TTL |
| 镜像 | **不镜像**；CDN 直链；下载必须校验 SHA-1/SHA-512 |

- 项目级 `loaders/game_versions` 是历史并集，**不能**用于安装候选；必须用 version 级字段。**[事实]** 见 03 §3.3。
- 资源包版本 `loaders=["minecraft"]`；光影版本 `loaders=["iris","optifine"]`，过滤语义不同。**[事实]**
- 文件被删除/项目转移时可能 404；不要拿项目级最新版硬填。**[推断]**

### 2.6 CurseForge

| 项 | 规则 |
|---|---|
| API | `https://api.curseforge.com/v1/`；`x-api-key` |
| 固定 id | `gameId=432`；`classId` 用 `/categories` 或项目详情校验 |
| 搜索 | `/mods/search`；`gameVersion` + `modLoaderType`；`index+pageSize<=10000` |
| 文件 | `/mods/{id}/files`；`/mods/{id}/files/{fileId}/download-url`；批量 `POST /mods` / `POST /mods/files` |
| 文件字段 | `fileName/fileLength/hashes[]`（sha1/md5）/`downloadUrl/gameVersions/sortableGameVersions/dependencies/fileFingerprint/isAvailable` |
| 分发 | `allowModDistribution=false` 时下载 URL 可能为 null；`/download-url` 也可能拿不到 |
| 限流 | 官方未公布；社区反馈超限可能 **403**（不是 429）；按 key 区分 `PROVIDER_AUTH` 与 `RATE_LIMITED` |
| 镜像 | **不镜像**；不拼接第三方 URL；作者禁转就引导官网 |
| key | 用户自备或独立网关；不内置、不进 Dart/日志/计划；AGPL 下内置必泄露 |

- `gameVersions` 把游戏版本与 loader 名混在一起；判断 loader 用 `sortableGameVersions[].gameVersionTypeId` 或已知名单。**[事实]**
- 文件列表返回顺序未承诺，按 `fileDate` 排序后再选。**[事实]**
- fingerprint：MurmurHash2 32-bit，seed=1，过滤 `0x09/0x0A/0x0D/0x20`；用于本地文件反查。**[事实/社区逆向]**
- `allowModDistribution` 的具体服务端行为、批量端点上限、403 语义仍需实测。**[未验证]**

### 2.7 镜像

| 镜像 | 可镜像 | 不可镜像 | 规则 |
|---|---|---|---|
| BMCLAPI | 版本清单/版本 JSON/libraries/assets/Forge/NeoForge Maven | Modrinth/CF 内容、用户 token | 官方 hash 校验；失败回退官方；标注实际来源 |
| 用户自定义 | 官方 artifact（按白名单） | 任何需要 API key/签名的内容 | 只接受 HTTPS；hash 必须来自官方/可信元数据 |
| 自建 CDN | 自己的启动器更新/自有资源 | Mojang 游戏文件/assets、Forge `-client.jar` | 不做游戏文件镜像；不公开分发版权内容 |

- 镜像 URL 只替换 host/前缀，**path 与字节必须一致**；不允许镜像改写内容/压缩/加壳。**[推断]**
- 同一文件的多源必须字节一致；Rust 对每个源都按同一个 hash 校验，失败源进 `(hash,host)` 黑名单。**[事实]** 05 §3。
- 记录实际成功来源（`sourceHost`），用于诊断与条款展示；不要只记计划里的首选 URL。**[推断]**

---

## 3. Mod / 光影 / 资源包的源选择

### 3.1 默认策略

```
默认：Modrinth
补充：CurseForge（仅当用户配置了 key，或项目只在 CF 上）
不默认：第三方聚合站/网盘/论坛附件
```

理由：Modrinth 无 key、直链、双哈希、批量反查/更新；CF 覆盖老 mod/整合包，但受 key、配额、作者禁转限制。**[事实]** 03 §0。

| 维度 | Modrinth | CurseForge |
|---|---|---|
| key | 不需要 | 需要 `x-api-key`（用户/网关） |
| 文件 URL | 版本对象直接给 `files[].url` | 可能要给 `downloadUrl` 或 `/download-url` 二次解析 |
| 哈希 | SHA-1 + SHA-512（优先 SHA-512） | SHA-1 + MD5（fingerprint 另算） |
| 版本级兼容 | `game_versions[]` + `loaders[]` | `gameVersions` 混 loader；用 `sortableGameVersions` 判 |
| 依赖 | 直接给 project/version 依赖 | 通常只给 modId，需再查兼容文件 |
| 更新 | 批量 hash 更新 | fingerprint + `latestFilesIndexes`，无批量最新兼容 |
| 分发限制 | 作者可删文件/版本 | `allowModDistribution=false` 时无 URL |
| 镜像 | 不镜像 | 不镜像 |
| 默认 | **首选** | 补充/用户显式选择 |

**选择顺序**（`provider=auto` 时）：

1. 用户在实例/项目上显式选择的 provider 优先。
2. 用统一 DTO 搜索 Modrinth；命中且版本级兼容 → 选 Modrinth。
3. 未命中/不兼容，且用户有 CF key → 查 CF；仍不兼容 → `NO_COMPATIBLE_BUILD`。
4. 两边都有：按 `(sha1|sha512)` 去重；优先用户选择/下载量更高/更新更近；**不要**把两个文件都装进 `mods/`。
5. 不允许「找不到指定版本就装最新」；必须锁定 `(provider, projectId, versionId/fileId)`。

### 3.2 版本级兼容

```text
accept(version, target):
  gameVersion 必须精确出现在 version.game_versions   // 1.21 != 1.21.1
  kind == mod       → version.loaders 包含 target.loader（forge/neoforge/fabric/quilt）
  kind == shader    → version.loaders 包含 target.shaderLoader（iris|optifine）
  kind == resourcepack → version.loaders 包含 "minecraft"
  side == client    → 项目/版本 client_side != unsupported
  snapshots         → 默认排除，用户显式开启才接受
```

- Quilt 对 Fabric mod 的兼容是「常见但非保证」，默认不放行；高级设置可开。**[推断]**
- 资源包还要读 `pack.mcmeta` 的 `pack_format`；Modrinth API 不直接给，需下载后校验。**[未验证/需补表]**
- 光影 loader 是 `iris`/`optifine`，不是 `fabric`/`forge`；不要拿实例 loader 直接过滤。**[事实]**

### 3.3 依赖解析

| 来源 | 类型 | 处理 |
|---|---|---|
| Modrinth | `required` / `optional` / `incompatible` / `embedded` | 递归解析；可选默认不装；冲突阻断；已内置不重复 |
| CurseForge | `3 RequiredDependency` / `2 OptionalDependency` / `1 EmbeddedLibrary` | 递归解析；CF 只给 modId，需再查兼容文件 |
| CurseForge | `5 Incompatible` / `4 Tool` / `6 Include` | 冲突阻断 / 工具不单独装 / 打包内容 |

- 深度上限 8；按 `(provider, projectId)` 去环；一次展示批量清单让用户确认。**[事实/推断]**
- CF 依赖只给 `modId`，解析结果会随作者更新漂移；**记录解析时选择的 fileId 并锁定**，重装/更新时用锁定值，除非用户明确重解。**[推断]**
- 缺 required 依赖：`DEPENDENCY_UNRESOLVED`，阻塞安装/启动并列出缺失项；optional 缺失只 warning。**[推断]**
- 不自动安装 `Tool`/`Include` 为独立 mod；不把 `Embedded` 再下一份。**[事实/推断]**

### 3.4 更新检查

| 源 | 方法 | 注意 |
|---|---|---|
| Modrinth | `POST /version_files/update`，body `{hashes, algorithm:"sha1", loaders, game_versions, version_types}` | 批量首选；工程按 500/批；比较主文件 SHA-1 |
| CurseForge | fingerprint 反查 → `POST /mods` 拿 `latestFilesIndexes` → 过滤 `(gameVersion, modLoader, releaseType)` → `fileDate` 选最新 | 无批量「最新兼容」端点；限流/缓存 6h |
| 本地 | `(path,size,mtime) → sha1/fingerprint` 缓存 | mtime 变才重算；改过的 jar 反查失败，回退 modId+version 低置信度匹配 |

- 状态：`UpToDate | UpdateAvailable | NoCompatibleBuild | UnknownLocal | DistributionDenied | Error`。**[事实]** 03 §4.3。
- 默认不自动更新、不降级、不跨大版本；beta/alpha 需显式开启；用户 pin 的 mod 跳过。**[事实]**
- 更新前展示 changelog、目标 loader/gameVersion、文件 hash；更新后重新校验并更新 `managed.json`。**[推断]**
- `DistributionDenied` 不重试、不换源；让用户去官网手动下载。**[事实/推断]**

### 3.5 CurseForge key 政策

| 方案 | 可行性 | 风险 | 建议 |
|---|---|---|---|
| 内置 key | 技术可行 | 二进制可提取；AGPL 源码公开必泄露；条款可能不允许 | **不要** |
| 用户自备 key | 可行 | 用户配置成本；key 泄露风险由用户承担 | 设置页填写，存 OS 凭据库；不进日志/计划 |
| 自建薄网关 | 可行 | 需要自己的条款/隐私/限流/防滥用；不能借此绕过平台条款 | 推荐给 official-global |
| 不接 CF，仅跳官网 | 可行 | 体验差 | 可作为 L0/无 key 默认 |

- key 只存在 C# 配置/凭据库；Dart 只看到 `configured: true/false`。**[事实]** 03 §2.4。
- **不要把 `x-api-key` 放进 `DownloadPlan.headers`**：CF 文件下载应由 C# 先解析出短期 URL；Rust 只拿 URL/hash，不持有 key。**[推断]**
- 403 先看 body/上下文：`PROVIDER_AUTH`（key 无效/未授权）vs `RATE_LIMITED`（配额）；不要盲目重试。**[未验证]** 社区反馈。
- `official-cn` L0 不注册 L2 provider；`official-global` 默认 L1，L2 需确认。**[事实]** 09 政策。

### 3.6 源选择伪代码

```text
resolve(request):
  target = {gameVersion, loader, loaderVersion, side, shaderLoader}
  candidates = []
  for provider in request.providers or [modrinth, curseforge if key]:
      if provider == curseforge and not key_configured: continue
      hits = search(provider, request, target)
      for h in hits:
          versions = get_versions(provider, h, target)
          for v in versions:
              if accept(v, target): candidates.add((provider, h, v))
  dedupe by (provider, projectId) and by file hash
  rank by: user pin > exact gameVersion > loader match > release > downloads > updated
  if none: return NO_COMPATIBLE_BUILD
  resolve_dependencies(candidates, depth<=8, lock fileIds)
  return ResolvedSet with per-file {provider, projectId, fileId, url, hash, size, terms}
```

- `ResolvedSet` 再交给 `PlanBuilder` 生成 `DownloadPlan`；**不要**在 Rust 里做上面的任何判断。**[推断]**

---

## 4. `DownloadPlan`：生产者/消费者边界

### 4.1 职责

```
C# Provider/Resolver/Compliance
  → 选择源、解析版本/依赖、求值策略、生成 URL/mirrors/hash/dest
  → DownloadPlan（纯数据）
Rust archoera_dl
  → 只执行：HTTPS 下载、分片/并发/限速/重试/镜像回退、hash 校验、原子落盘、进度
  → 返回结果；不理解 Mojang/Forge/Modrinth 语义
```

- **C# 是唯一 API owner**；Rust 不发业务 API 请求、不解析版本 JSON、不生成 URL。**[事实]** 架构 10 §9.1/§13.1。
- **生成物不进 DownloadPlan**：`-client.jar`、`client-extra`、mappings 由 loader 安装事务（C# + 外部 Java）产生；`DownloadPlan` 只包含远端输入。**[推断]** 09 §3。
- 解压也不在 Rust：natives/整合包 overrides 由 C# 执行；Rust 只做 `chmod/remove/copy` 等 post（架构 10 §9.2）。**[事实]**

### 4.2 字段（v1 + 建议扩展）

| 字段 | 必填 | 生产者语义 | Rust 行为 |
|---|---|---|---|
| `schemaVersion` | 是 | 计划版本 | 只接受支持版本 |
| `planId` | 是 | C# 生成；幂等键 | 重复提交返回同一 task；完成后短期缓存 |
| `root` | 是 | 实例/共享根的绝对路径 | canonicalize；所有 dest 必须在其内 |
| `concurrency` | 否 | 期望并发 | 受全局/每 host 配额约束 |
| `totalBytes` | 否 | 进度用 | 重算为 sum(size) |
| `files[].url` | 是 | 官方/首选 URL | 仅 HTTPS；拒绝 userinfo |
| `files[].mirrors` | 否 | 镜像候选 | 去重；失败回退；每个源都校验同一 hash |
| `files[].hash` | **是** | 来源 hash（sha1/sha256/sha512） | 落盘前/提交前校验；失败换源 |
| `files[].size` | **是** | 字节数 | 预分配/进度/大小校验 |
| `files[].dest` | 是 | 相对路径 | 拒绝绝对路径/`..`/保留名/ADS/大小写冲突 |
| `files[].mode` | 否 | `ensure/replace/link/symlink/skip` | 按 05 §1.1 语义 |
| `files[].priority` | 否 | 计划内排序 | 只在 plan 内生效 |
| `files[].executable` | 否 | Java runtime/脚本 | Unix chmod；Windows 忽略 |
| `files[].headers` | 否 | 短期 token/签名 | 仅白名单；不写日志 |
| `files[].provider` | 否 | `mojang/forge/neoforge/fabric/quilt/modrinth/curseforge/adoptium` | **忽略**；只用于 UI/审计 |
| `files[].source` | 否 | 页面/来源标识 | 忽略 |
| `files[].termsUrl` | 否 | 条款链接 | 忽略；UI 展示 |
| `files[].license` | 否 | SPDX/名称 | 忽略 |
| `files[].distribution` | 否 | `allowed/denied/unknown` | 忽略；C# 已在生成前拒绝 denied |
| `files[].origin` | 建议 | 必须 `remote` | **拒绝 `generated`/`local`**，强制边界 |
| `files[].cacheKey` | 否 | 去重键（如 `modrinth:<versionId>`） | 可作为诊断；不替代 hash |
| `files[].expectedContentType` | 否 | 诊断用 | 不强制，但可帮助识别 HTML 错误页 |

> 扩展字段必须可选；Rust 对未知字段忽略，但 `origin=generated` 必须显式拒绝（或在 C# 侧根本不生成）。**[推断]**
>
> **当前实现对齐（只读）**：`app/modules/downloader/src/plan.rs` 的 `FileSpec` 目前只认 `url/mirrors/hash/sha1/sha256/sha512/size/dest/mode/priority/executable/headers`；上表中的 `provider/source/termsUrl/license/distribution/origin/cacheKey/expectedContentType` 是**建议扩展**，落地前必须同步 Rust 结构体、JSON schema 与 C# DTO；在扩展前不要在 C# 里写入并期待 Rust 使用。`origin=generated` 的拒绝也需要在 `validate_plan_inner` 增加，不应只靠文档约定。**[事实/推断]**

### 4.3 什么永远不进入 DownloadPlan

| 内容 | 原因 | 正确位置 |
|---|---|---|
| Forge/NeoForge `-client.jar` | 本地 processor 生成；官方 Maven 404 | `LoaderArtifactManifest` + 修复事务（09） |
| `client-extra`/`MC_SLIM`/`MC_SRG`/`MC_OFF` | 中间生成物 | 同上 |
| 重写后的 `install_profile.json` | 含安装期占位符/路径；不是远端文件 | 运行时临时生成，不落盘为权威 |
| natives 解压结果 | 本地解压 | C# post/continuation |
| 整合包 overrides | 压缩包内本地内容 | C# 解压到 staging |
| 用户本地 Mod/配置 | 用户文件 | 实例目录；不下载 |
| 第三方预生成 `-client.jar` | 合规/供应链风险 | 不使用 |

### 4.4 hash 与镜像规则

| 来源 | 首选 hash | 说明 |
|---|---|---|
| Mojang manifest/version/client/libraries/assets/logging | SHA-1 | 官方字段只有 SHA-1；size 必填 |
| Forge/NeoForge installer/libraries | SHA-1 | `install_profile`/version JSON 提供 |
| Fabric/Quilt profile 库 | SHA-1 | profile JSON 的条目可能带顶层 `sha1`/`size`；缺失时读取 Maven `.sha1` sidecar 与 `Content-Length` 后再进入计划 |
| Fabric/Quilt 文件 | SHA-1（若提供） | 缺 hash 的库先补 sidecar；`DownloadPlan` 仍拒绝无 hash 条目 |
| Modrinth | **SHA-512 优先**，SHA-1 兼容 | `files[].hashes` 双哈希；去重键用 SHA-512 |
| CurseForge | SHA-1（+ MD5 辅助） | API `hashes[]`；作者禁转时无 URL |
| Java runtime | SHA-1 + size | 每文件 `downloads.raw/lzma` |
| Adoptium | SHA-256 | 归档校验；签名另验 |
| 用户本地文件 | SHA-256（优先） | 本地计算；不信任文件名 |

- 镜像必须与官方 hash 一致；**不能**用镜像自己返回的 hash 作为信任根。**[事实]** 09 政策 §4.2。
- 同一文件多源必须字节一致；C# 不能保证时只给一个源。**[事实]** 05 §3。
- hash 失败：同源不重试，换源；记录 `(hash, host)` 黑名单；全失败 `HASH_MISMATCH`，结果带每源实际 hash。**[事实]** 05 §3。

### 4.5 来源/条款元数据

建议每个 `PlanFile` 带：

```json
{
  "provider": "modrinth",
  "source": "https://modrinth.com/mod/sodium",
  "termsUrl": "https://modrinth.com/legal/terms",
  "license": "LGPL-3.0-only",
  "distribution": "allowed",
  "origin": "remote",
  "cacheKey": "modrinth:SMxNOGZ6"
}
```

- UI 能展示「从哪来、受什么条款约束」；审计能回答「这个文件是谁提供的」。**[事实]** 09 政策 §2.3。
- Rust 不解释这些字段；但结果应回传 `sourceHost`/`provider`，便于 C# 记录实际来源。**[推断]**
- 条款/许可未知时 `license=null, distribution=unknown`；不要假装 `allowed`。**[推断]**

### 4.6 示例（片段）

```json
{
  "schemaVersion": 1,
  "planId": "8f2c...",
  "root": "/data/instances/01J...",
  "concurrency": 16,
  "totalBytes": 26836906,
  "files": [
    {
      "url": "https://piston-data.mojang.com/v1/objects/30c73b.../client.jar",
      "mirrors": ["https://bmclapi2.bangbang93.com/version/1.21.1/client"],
      "hash": { "algo": "sha1", "hex": "30c73b1c5da787909b2f73340419fdf13b9def88" },
      "size": 26836906,
      "dest": "versions/1.21.1/1.21.1.jar",
      "mode": "ensure",
      "priority": 0,
      "executable": false,
      "provider": "mojang",
      "source": "https://piston-meta.mojang.com/...",
      "termsUrl": "https://www.minecraft.net/eula",
      "distribution": "allowed",
      "origin": "remote"
    },
    {
      "url": "https://cdn.modrinth.com/data/AANobbMI/versions/SMxNOGZ6/sodium.jar",
      "mirrors": [],
      "hash": { "algo": "sha512", "hex": "..." },
      "size": 1574609,
      "dest": "mods/sodium.jar",
      "mode": "ensure",
      "provider": "modrinth",
      "source": "https://modrinth.com/mod/sodium",
      "termsUrl": "https://modrinth.com/legal/terms",
      "license": "LGPL-3.0-only",
      "distribution": "allowed",
      "origin": "remote"
    }
  ],
  "post": []
}
```

> Forge installer 也可以是一个 `PlanFile`（`provider=forge`, `dest=downloads/.../installer.jar`）；但它的 processor 输出**不在**同一个 plan 里，而是后续 C# repair 事务。**[推断]**

---

## 5. 失败模式

| 场景 | 典型来源 | 识别 | 重试/动作 | 用户提示 |
|---|---|---|---|---|
| **作者禁转** | CurseForge | `allowModDistribution=false`；`downloadUrl=null`；`/download-url` 无可用 URL | **不重试、不换镜像**；`DISTRIBUTION_DENIED` | 「作者未授权第三方下载，请到官网手动下载」 |
| **key 无效/未授权** | CurseForge | 401/403 + body/上下文 | 不重试；`PROVIDER_AUTH` | 「检查 CurseForge API key」 |
| **限流** | Modrinth/CF | 429 + `Retry-After`；Modrinth `Remaining` 低 | 退避/等待；`RATE_LIMITED` | 「请求过多，N 秒后重试」 |
| **CF 配额 403** | CurseForge | 403 与 key 错误难区分 | 先判 `PROVIDER_AUTH`，再判 `RATE_LIMITED`；不要盲目重试 | 「可能达到配额，稍后再试/检查 key」 |
| **签名 URL 过期** | CF/CDN/自定义镜像 | 403/410/`URL_EXPIRED` | 同 URL 不重试；C# 重新解析 URL 后重提 | 「下载链接已过期，正在重新获取」 |
| **镜像 hash 不一致** | BMCLAPI/自定义 | actual != expected | 删除 `.part`，拉黑 `(hash,host)`，换源；单源 `MIRROR_MISMATCH`，全失败 `HASH_MISMATCH` | 「镜像文件校验失败，已切换官方源」 |
| **镜像返回 HTML/错误页** | 镜像/WAF | 200 但 content-type/html、size 不符 | 视为 hash/size 失败；换源 | 同上 |
| **主源 404/410** | Modrinth/CF/Mojang | 404/410 | 不重试；`VERSION_REMOVED`；让用户选其他版本 | 「该版本已被删除/不可用」 |
| **Quilt/Fabric meta 5xx/超时** | meta | 5xx/timeout | 退避重试；缓存旧 profile 并标注 stale | 「加载器元数据暂不可用，使用缓存」 |
| **缺少依赖** | 解析器 | required 未解析/不兼容 | `DEPENDENCY_UNRESOLVED`；列出缺失项 | 「缺少前置 Mod X；是否一起安装？」 |
| **可选依赖缺失** | 解析器 | optional | 安装继续；warning | 「可选前置 X 未安装」 |
| **版本级不兼容** | 解析器 | gameVersion/loader 不匹配 | 不安装；`NO_COMPATIBLE_BUILD` | 「该文件不支持 1.21.1/NeoForge」 |
| **CF 分页超限** | CF | `index+pageSize>10000` | `PAGINATION_LIMIT`；收窄筛选 | 「结果太多，请加筛选条件」 |
| **离线且无缓存** | 全部 | 网络不可用 + cache miss | `OFFLINE_CACHE_MISS`；不阻塞已有实例启动 | 「离线，且没有缓存」 |
| **合规拒绝** | 门控 | L0/L2/同意缺失 | `DOWNLOAD_DISABLED`/`CONSENT_REQUIRED`；不生成计划 | 「当前构建/设置不允许下载」 |
| **磁盘满/权限** | 本地 | ENOSPC/EACCES | 停止写入；不清空已有文件 | 「磁盘空间不足/无权限」 |
| **Java 组件不可用** | Mojang/Adoptium | 平台/架构无该组件 | `JAVA_COMPONENT_UNAVAILABLE`；让用户选手动 JDK | 「该平台没有匹配的 Java，请手动选择」 |
| **生成物缺失** | Forge/NeoForge | `-client.jar` 等缺失 | 不是下载失败；走 09 的 `loader.repair` | 「加载器文件不完整，正在修复」 |

错误码映射：Rust 2006/2007（HTTP 4xx/429）、3001（HASH_MISMATCH）、5001（CANCELLED）等见 05 §9；C# 统一 envelope 见架构 10 §13.7。**[事实]**

---

## 6. 测试

### 6.1 源矩阵契约测试

| # | 用例 | 期望 |
|---:|---|---|
| 1 | 本地假 HTTP 服务器提供 manifest/version/client/libraries/assets | C# 解析 → Plan → Rust 下载 → hash 全部通过 |
| 2 | version JSON 的 `sha1` 与客户端计算不一致 | `HASH_MISMATCH`；不落盘；重试换源 |
| 3 | 主源 200 但字节错，镜像正确 | 自动换源成功；记录 `sourceHost=mirror` |
| 4 | 镜像全部错误 | `HASH_MISMATCH`；结果含每源实际 hash；不破坏已有 dest |
| 5 | 429 + `Retry-After` 秒/HTTP-date | 等待后重试；超过 120s 冷却换源 |
| 6 | 403 + 签名 URL 过期 | `URL_EXPIRED`；C# 重新解析；不循环重试 |
| 7 | 403 + CF key 无效 | `PROVIDER_AUTH`；不重试 |
| 8 | CF `downloadUrl=null` / 禁转 | `DISTRIBUTION_DENIED`；无第三方镜像请求 |
| 9 | Modrinth `hashes.sha512` 与下载字节不符 | 换源/失败；不降级为 SHA-1 蒙混 |
| 10 | Fabric profile JSON 无 hash | 读取 Maven `.sha1` sidecar 与 `Content-Length` 后进入计划；计划仍要求 hash |
| 11 | Java runtime `all.json` 选平台/组件 | 只取对应平台；`gamecore` 空数组不误判 |
| 12 | Java runtime 文件 `executable=true` | Unix 保留执行位；Windows 不报错 |
| 13 | 资源包 `loaders=["minecraft"]`、光影 `["iris"]` | 过滤语义正确；不拿实例 loader 硬套 |
| 14 | 项目级 `game_versions` 包含但 version 级不包含 | 不选中；`NO_COMPATIBLE_BUILD` |
| 15 | 依赖 required 缺失/optional 缺失 | 前者阻断并列出；后者 warning |
| 16 | CF fingerprint 反查未知文件 | `UnknownLocal`；回退 modId+version 低置信度 |
| 17 | 批量更新 500+ hash | 分批；状态映射正确；不自动更新 |
| 18 | 计划含 `origin:"generated"` | C# 生成阶段拒绝；Rust 也拒绝 `INVALID_PLAN` |
| 19 | 计划含空 URL/无 hash/无 size | `INVALID_PLAN`；不启动下载 |
| 20 | 路径含空格/中文；dest 含 `..`/保留名 | 前者正常；后者拒绝 |
| 21 | 同一 plan 重复提交 | 返回同一 task；不重复下载 |
| 22 | 两个 plan 写同一 dest | `DEST_BUSY` 或排队；不交叉写 |

### 6.2 真实源（可选/夜间）

- Mojang：1.20.1 / 1.21.1 清单、版本 JSON、client、少量 libraries/assets；记录 sha1。
- Forge/NeoForge：installer 下载 + `loader.install`（见 09）；不在 CI 里跑完整 6 个 processor 除非有缓存。
- Fabric/Quilt：profile JSON 解析 + 库下载；Quilt 当前网络在本次实测超时，需重试/镜像。
- Modrinth：搜索/版本/下载一个公开 mod；记录 UA/限流头；不要高频。
- CurseForge：**需要用户 key**；无 key 只跑 fixture/错误路径，不请求真实 API。
- Java runtime：`all.json` + 一个平台 manifest 的元数据；不下载完整 JRE。
- 镜像：BMCLAPI 下载一个 installer/库，校验官方 hash。

### 6.3 验收

- `DownloadPlan` 中 0 个生成物、0 个空 URL、0 个缺 hash/size 的文件。**[推断]**
- 所有镜像命中都按官方 hash 校验；失败不写 dest。**[事实]** 05 §3。
- CF key 不出现在 Dart/日志/计划/崩溃报告；`official-cn` 构建不含 L2 端点。**[事实]** 09 政策 §4.1。
- 失败码可区分 `DISTRIBUTION_DENIED`/`PROVIDER_AUTH`/`RATE_LIMITED`/`URL_EXPIRED`/`HASH_MISMATCH`。**[推断]**
- 更新检查不自动改用户文件；先展示、后确认。**[推断]**

---

## 7. 未验证 / 开放问题

1. **CurseForge 真实速率限制与 403 语义**：官方未公布；社区反馈 403 可能是配额。需要真实 key 测 401/403/429、批量端点上限、`allowModDistribution=false` 的响应。**[未验证]**
2. **CF `/download-url` 的 URL 有效期**：签名过期时间/是否可缓存；需要实测。**[未验证]**
3. **Modrinth 批量更新上限**：实测 1000 hash 可返回 200，但官方未写上限；工程按 500/批。**[未验证]**
4. **Modrinth ETag**：实测 `/search`/`/project` 无 ETag；其他端点可能有；按 TTL 为主。**[事实/未验证]**
5. **Fabric/Quilt meta 速率限制**：未见公开数值；需要压测/观察 429。**[未验证]**
6. **Quilt 当前可用性**：本次 `meta.quiltmc.org` 请求超时；需要重试/镜像策略。**[未验证]**
7. **Mojang runtime 组件映射**：`java-runtime-delta` 21.0.7；未来版本（25/26）组件名需以 `javaVersion.component` 与 `all.json` 为准。**[未验证]**
8. **Adoptium 速率/条款**：`/v3/binary/latest` 的重定向、校验和、再分发条款需核对。**[未验证]**
9. **BMCLAPI 镜像一致性**：本次样本从镜像下载；官方 Maven 超时；生产应校验官方 hash 并记录实际来源。**[未验证]**
10. **`pack_format` 与游戏版本映射**：资源包真实可用性需读 `pack.mcmeta` 后校验；Modrinth API 不提供。**[未验证]**
11. **CF key 网关**：自建网关的条款/隐私/防滥用设计未定；不能把它做成绕过平台限制的通道。**[未验证]**
12. **`DownloadPlan` 扩展字段**：`provider/source/termsUrl/license/distribution/origin/cacheKey` 需与 05/10 契约同步；新增必须可选，`origin=generated` 拒绝。**[待契约冻结]**
13. **镜像策略**：Fabric/Quilt 是否允许 BMCLAPI 镜像、如何记录实际来源；需要与 09 政策确认。**[未验证]**
14. **CF 作者禁转的替代路径**：只允许官网手动下载/本地导入；是否提供「打开网页」深链需产品确认。**[未验证]**

---

## 8. 参考

| 内容 | 链接 |
|---|---|
| Mojang 版本清单 | https://piston-meta.mojang.com/mc/game/version_manifest_v2.json |
| Mojang Java runtime 清单 | https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json |
| Forge Maven | https://maven.minecraftforge.net/ |
| NeoForge Maven | https://maven.neoforged.net/releases/ |
| Fabric Meta | https://meta.fabricmc.net/ |
| Quilt Meta | https://meta.quiltmc.org/ |
| Modrinth API | https://docs.modrinth.com/api/ |
| Modrinth 限流 | https://docs.modrinth.com/api/#ratelimits |
| CurseForge API | https://docs.curseforge.com/rest-api/ |
| CurseForge key | https://support.curseforge.com/support/solutions/articles/9000208346-about-the-curseforge-api-and-how-to-apply-for-a-key |
| Adoptium API | https://api.adoptium.net/ |
| BMCLAPI | https://bmclapi2.bangbang93.com/ |
| 本项目下载引擎 | [05-download-engine.md](05-download-engine.md) |
| 本项目整合包 | [06-modpack-install.md](06-modpack-install.md) |
| 本项目搜索/更新 | [03-mod-search.md](03-mod-search.md) |
| 本项目生成物/修复 | [09-forge-generated-artifacts.md](09-forge-generated-artifacts.md) |
| 发行/下载政策 | [`../09-distribution-and-download-policy.md`](../09-distribution-and-download-policy.md) |
