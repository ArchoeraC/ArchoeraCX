# 03 · 模组 / 整合包 / 资源包搜索与元数据接入

> 面向 C# 统一 API 层（`resource.*`）的可执行设计，不是 API 抄本。
> 标注：**[事实]** 官方文档或本次实测；**[推断]** 由行为/生态推导；**[未验证]** 需 key/更多样本。实测时间 2026-10-20，基址 `https://api.modrinth.com/v2`、`https://api.curseforge.com/v1`。

## 0. 结论先行

| # | 结论 | 依据 |
|---|---|---|
| 2 | UI 只认统一 DTO；provider 差异在 C# 消化，Dart 不拼 facet、不解析 CF 枚举。 | [事实] 架构 02 §3.5 |
| 3 | 兼容性**必须用 version 级 `loaders/game_versions`**，不能用项目级并集；并集是历史数据，会假阳性。 | [事实] Modrinth 文档；Iris 项目含 quilt，但并非每版本都有 |
| 4 | 更新检查：Modrinth 用 `POST /version_files/update` 批量；CF 用 Murmur2 fingerprint + `latestFilesIndexes`。两者都不保证“最新=兼容”。 | [事实] 文档+实测 |
| 5 | 搜索工程核心是**取消 + single-flight + TTL + 离线降级**；300ms 防抖只在 Dart 做一次。 | [推断] 交互/限流约束 |
| 7 | 本地搜索：实例/文件名用内存索引；Mod 描述可选 FTS5；日志不要默认建 FTS5。 | [事实] FTS5 实测见 §6 |

## 1. Modrinth API v2

### 1.1 端点

| 方法 | 路径 | 用途 |
|---|---|---|
| GET | `/search` | 参数 `query/facets/index/offset/limit` |
| GET | `/project/{id\|slug}` · `/projects?ids=["id"]` | 详情（Markdown `body`、license 对象）· 批量详情 |
| GET | `/project/{id}/version?loaders=["fabric"]&game_versions=["1.21.1"]&include_changelog=false` | 版本列表；`include_changelog=false` 显著减小响应 |
| GET | `/version/{id}` · `/versions?ids=["..."]` | 单版本 · 批量版本 |
| GET | `/version_file/{hash}?algorithm=sha1` | 按 hash 反查；未知 hash 返回 404 |
| POST | `/version_files` | body `{hashes:[],algorithm:"sha1"\|"sha512"}`，返回 `hash→版本` map |
| POST | `/version_file/{hash}/update` · `/version_files/update` | 单/批量查最新兼容版本；body `loaders/game_versions/version_types?` |
| GET | `/tag/{project_type\|loader\|game_version\|category}` | 标签枚举，构建筛选项 |
| GET | `/version_file/{hash}/download` | 302 到 CDN；一般直接用 `files[].url` |

### 1.2 `/search` 参数与 facets

| 参数 | 说明 |
|---|---|
| `query` | 自由文本；空串可做纯 facet 浏览 |
| `facets` | JSON 数组的数组；**内层 OR，外层 AND** |
| `index` | 排序：`relevance`（默认）、`downloads`、`follows`、`newest`、`updated`。参数名是 `index`，不是 `sort` |
| `offset` / `limit` | 0 基偏移；limit 默认 10、官方上限 100；实测 101→100、0→1 |

facets 语法 `{type} {op} {value}`，`:` 等价 `=`，支持 `!= >= > <= <`。常用类型：`project_type`（`mod/modpack/resourcepack/shader/plugin/datapack/minecraft_java_server`，实测 `/tag/project_type`）、`categories`（**loader 也在 categories 里**，如 `categories:fabric`）、`versions`（`versions:1.21.1`）、`environment`（`client_only/client_and_server/server_only/...`）、`client_side`/`server_side`（**已弃用但仍可用**，值 `required/optional/unsupported`）、`open_source`、`license`、`downloads`、`follows`、`all_project_types`。
```json
[["project_type:mod"],["categories:fabric","categories:quilt"],["versions:1.21.1"],["client_side:required","client_side:optional"]]
```
含义：mod **且**（fabric 或 quilt）**且** 1.21.1 **且** 客户端可用；实测能返回 Sodium/Iris。内层写成多个数组会变成 AND。实际请求必须对 facets 做 URL 编码（`Uri.EscapeDataString`），示例为可读未编码。

### 1.3 分页与响应字段

- `limit<=100`；`offset` 官方未写上限，实测 `offset=100000` 仍 200 但 `hits=[]`。深分页不可当数据库，UI 建议上限 1000。**[事实/推断]**
- 响应 `{hits:[],offset,limit,total_hits}`；`total_hits` 不保证翻页期间稳定。

| 字段 | 含义 |
|---|---|
| `project_id` / `slug` | 稳定 id / 可变友好名；长期存储用 id |
| `project_type` / `all_project_types` | 主类型 / 所有版本涉及类型 |
| `title` / `description` / `author` | 名称 / 摘要（非正文）/ 作者 |
| `categories` / `display_categories` | 分类+loader 混合；展示用 display |
| `versions` / `loaders` / `game_versions` | **项目级并集**，只用于展示/筛选，不代表当前版本兼容 |
| `downloads` / `follows` | 项目总下载 / 关注数 |
| `date_created` / `date_modified` / `latest_version` | 创建/修改时间（不等于最新版本发布）/ 最新版本 id（不保证兼容） |
| `license` / `client_side` / `server_side` / `environment` | license（命中为 SPDX 字符串，详情为对象）/ 侧别；新代码优先 environment |

项目详情额外 `body`（Markdown）、`followers`、`published`、`updated`、`versions`（版本 id 数组）、`source_url/issues_url`。版本对象：`version_number`、`version_type`（release/beta/alpha）、`game_versions[]`、`loaders[]`、`date_published`、`downloads`、`files[]`（`hashes.sha1/sha512`、`url`、`filename`、`primary`、`size`）、`dependencies[]`。资源包版本 `loaders=["minecraft"]`；光影版本 `loaders=["iris","optifine"]`（实测 BSL Shaders）——loader 过滤要换语义。

### 1.4 UA、限流、缓存

- **User-Agent 必填且必须唯一识别应用**；只写 `okhttp/...` 可能被封。推荐 `ArchoeraCX/0.1.0 (contact@example.com)`；同一 UA 也发给 CF。**[事实]**
- **300 请求/分钟/IP**，带 token 也一样；响应头 `X-Ratelimit-Limit/Remaining/Reset`（Reset 为剩余秒）。**[事实] 文档+实测**
- 建议 token bucket：稳态 4 req/s、突发 20；`Remaining<20` 主动降速；429 读 `Retry-After`，无则 1/2/4s 退避。每 provider 独立桶。
- **不要假设有 ETag**：实测 `/v2/search` 与 `/v2/project` 均无 `ETag`，只有 Cloudflare `last-modified/age`，文档也未承诺条件请求。设计为“有 ETag 就用，没有就 TTL”。TTL：搜索 60s、详情 10min、版本 5min、标签 24h、更新检查 6h。**[实测/推断]**

## 2. CurseForge API v1

### 2.1 鉴权与固定 id

- `gameId=432` 是 Minecraft；启动时可用 `GET /v1/games` 校验。**[事实/通行值]**
- `classId` 官方没有枚举表，下表是社区通行值；**必须用 `GET /v1/mods/{id}` 的 `classId` 或 `/v1/categories?gameId=432&classesOnly=true` 运行时校验**：

| 内容 | classId | 置信度 |
|---|---:|---|
| Mod / Modpack | 6 / 4471 | 高 |
| Resource Pack / Shader | 12 / 6552 | 高 |
| 其他（World/Plugin/DataPack） | 17/5/6945 | 中/中/**未验证** |

### 2.2 `/mods/search`

| 参数 | 说明 |
|---|---|
| `gameId` / `classId` | 必填 432 / 内容类型 |
| `searchFilter` | 自由文本，匹配名称与作者 |
| `gameVersion` | 单个游戏版本字符串 |
| `modLoaderType` | `0=Any,1=Forge,2=Cauldron,3=LiteLoader,4=Fabric,5=Quilt,6=NeoForge`；**官方要求与 gameVersion 同用** |
| `modLoaderTypes` / `categoryIds` | 多 loader 最多 5 个 / 多分类最多 10 个；会覆盖单值参数 |
| `sortField` | `1=Featured,2=Popularity,3=LastUpdated,4=Name,5=Author,6=TotalDownloads,7=Category,8=GameVersion,9=EarlyAccess,10=FeaturedReleased,11=ReleasedDate,12=Rating` |
| `sortOrder` / `index` / `pageSize` | `asc\|desc` / 0 基偏移 / 默认与最大 50 |

- **分页硬限制 `index+pageSize<=10,000`**，超过即错误；UI 到 200 页（50/页）就到顶，必须提示加筛选。**[事实]**
- 响应 `{data:[Mod],pagination:{index,pageSize,resultCount,totalCount}}`。
- `Mod`：`id/name/slug/summary/downloadCount/dateModified/classId/authors[]/logo/categories[]/links/allowModDistribution/latestFilesIndexes[]/latestFiles[]/thumbsUpCount/rating`；**无 follows/license**，`thumbsUpCount` 是点赞不是关注，统一 DTO 中为 `null`，不要硬映射。**[事实]**
- `latestFilesIndexes[]` 是更新检查关键：`{gameVersion,fileId,filename,releaseType,gameVersionTypeId,modLoader}`，可直接构造“某版本+某 loader 的最新文件”候选。

### 2.3 文件、下载与整合包

| 端点 | 说明 |
|---|---|
| `GET /mods/{id}/files?gameVersion=&modLoaderType=&index=&pageSize=` | `pageSize<=50`，`index+pageSize<=10000`；**返回顺序未承诺，必须按 fileDate 排序** |
| `GET /mods/{id}/files/{fileId}` · `POST /mods/files` | 单文件 · body `{fileIds:[]}` 批量；未文档化上限，保守 100/批 |
| `GET /mods/{id}/files/{fileId}/download-url` · `POST /mods` | 返回 `{data:"https://..."}`（`downloadUrl` 为空时二次获取）· body `{modIds:[]}` 批量项目，保守 100/批 |

文件字段：`id/fileName/displayName/releaseType(1/2/3)/fileDate/fileLength/hashes[](algo=1 Sha1,2 Md5)/downloadUrl/gameVersions[]/sortableGameVersions[]/dependencies[]/fileFingerprint/isAvailable`。`gameVersions` **把游戏版本和 loader 名字混在一起**（如 `["1.21.1","Forge"]`），判断 loader 用 `sortableGameVersions[].gameVersionTypeId` 或已知 loader 名单。

- **`downloadUrl` 可能为 null**：作者关闭第三方分发（`allowModDistribution=false`）时 `/download-url` 通常也拿不到可用 URL。返回专用 `DISTRIBUTION_DENIED`，引导官网手动下载，禁止重试。**[事实/生态行为]**

整合包 `manifest.json`（事实标准，非 CF 官方 schema 文档）：
```json
{"minecraft":{"version":"1.21.1","modLoaders":[{"id":"fabric-0.16.9","primary":true}]},
 "manifestType":"minecraftModpack","manifestVersion":1,"name":"Pack","version":"1.0.0",
 "author":"someone","overrides":"overrides",
 "files":[{"projectID":238222,"fileID":5234567,"required":true,"isLocked":false}]}
```
安装规则：`minecraft.version` + `modLoaders[].id`（`<loader>-<version>`，取 `primary:true`）决定实例；`files[]` 必须按 `(projectID,fileID)` **精确**解析，禁止“找不到就装最新”；`required:false` 可跳过并提示；`overrides`（缺省 `overrides`）解压前做 Zip Slip 校验（拒绝绝对路径、`..`、symlink）；`manifestVersion` 只接受 1。`isLocked` 语义未文档化，原样保留、不据此做安全判断。**[事实/推断]**

### 2.4 key 的约束与泄露风险


## 3. 统一 DTO 设计

### 3.1 枚举与身份

`Provider: modrinth|curseforge`；`Kind: mod|modpack|resourcepack|shader|datapack|world|plugin|unknown`；`Channel: release|beta|alpha`；`Side: client|server|both`。
统一 `Id` 用 **string**（Modrinth base62 / CF int32），不要用 int 承载两家；稳定键 `(Provider,ProjectId)`，slug 只用于跳转；文件身份 `(Provider,FileId)+sha1`，跨 provider 去重用 sha1。

### 3.2 字段映射

| 统一字段 | Modrinth | CurseForge | 缺失处理 |
|---|---|---|---|
| `id` | `project_id` | `id`（转字符串） | 必填，缺失判 schema |
| `slug` / `name` / `summary` | `slug` / `title` / `description` | `slug` / `name` / `summary` | 空串/null |
| `author` | `author` | `authors[0].name` | null；CF 保留 authors 列表 |
| `iconUrl` | `icon_url` | `logo.thumbnailUrl`/`logo.url` | null→占位图 |
| `downloads` / `follows` | `downloads` / `follows` | `downloadCount` / **无** | 0 / null，不拿点赞冒充 |
| `updatedAt` | `date_modified` | `dateModified` | null |
| `categories` | `categories` 去 loader | `categories[].slug` | 空数组 |
| `loaders` / `gameVersions` | 项目级仅展示，版本级为准 | `latestFilesIndexes[].modLoader/gameVersion` | 空数组 |
| `kind` / `license` / 侧别 | `project_type` / 字符串或对象 / `client_side` | `classId` 映射 / **无** / 无 | unknown / null |
| `latestVersionId` | `latest_version` | `latestFilesIndexes[].fileId`（按文件详情 fileDate 排序） | null |

版本映射：Modrinth `version.id/version_number/version_type/game_versions/loaders/date_published/downloads/files/dependencies`；CF `file.id/displayName/releaseType/fileDate/fileLength/downloadUrl/hashes/gameVersions/dependencies/fileFingerprint`。CF 无 `version_number`，用 `displayName`，解析文件名只作 fallback。

### 3.3 兼容性过滤

输入 `InstanceTarget{gameVersion,loader,loaderVersion?,side=client,shaderLoader?}`；loader 规范化 `fabric|quilt|forge|neoforge`，CF 数字映射 `1/4/5/6`。
```text
accept(project, target):
  if project.kind != requestedKind: false
  if target.gameVersion not in version.game_versions: false   // 精确匹配，不用前缀
  if kind == resourcepack: version.loaders contains "minecraft"
  if kind == shader:       version.loaders contains target.shaderLoader (iris|optifine)
  else:                    version.loaders contains target.loader
  if side == client and project.clientSide == "unsupported": false
```
- 项目级 `loaders/game_versions` 只用于 facets/展示；安装候选必须逐个版本判断。
- `1.21` 与 `1.21.1` 不互相兼容；快照默认排除，用户显式开启才查。
- Quilt 对 Fabric mod 的兼容是“常见但非保证”，不要默认放行；可作为高级设置。**[推断]**
- 资源包 `loaders=["minecraft"]`，真实可用性还受 `pack_format` 影响；该字段不在 Modrinth API，安装时读 `pack.mcmeta` 再校验。**[未验证/需补表]**
- 类型判断不要靠文件名（`.jar` 可能是 mod/resourcepack/datapack），靠 `project_type/classId`；仍不确定则 `unknown` 并禁止一键安装。

## 4. 依赖关系与更新检查

### 4.1 依赖语义

| 来源 | 类型 | 处理 |
|---|---|---|
| Modrinth | `required` / `optional` / `incompatible` / `embedded` | 递归解析；可选默认不装；冲突阻断；已内置不重复装 |
| CF | `3 RequiredDependency` / `2 OptionalDependency` / `1 EmbeddedLibrary` | 递归解析；CF 只给 modId，需再查兼容文件；可选/已内置按语义处理 |
| CF | `5 Incompatible` / `4 Tool` / `6 Include` | 冲突阻断 / 工具，不单独装 / 打包内容 |

依赖解析深度上限 8、按 projectId 去环、让用户一次确认批量清单；CF 只给 modId 不给 fileId，解析结果会随作者更新漂移，必须记录“解析时选择的 fileId”并锁定。**[事实/推断]**

### 4.2 本地文件 → 远端项目

- Modrinth：流式算 sha1 → `POST /version_files` 得当前版本；未知 hash 不在 map 里。
- CF：算 **MurmurHash2 32-bit，seed=1，过滤 0x09/0x0A/0x0D/0x20** → `POST /v1/fingerprints/432` body `{fingerprints:[...]}` → `exactMatches[{id,file}]`。**[事实/社区逆向]**
- 改过内容的 jar（如内置配置）hash/fingerprint 会变，反查失败；回退到 `META-INF/mods.toml`/`fabric.mod.json` 的 modId+version 做低置信度匹配并标注。
- 缓存 `(path,size,mtime)→sha1/fingerprint`，mtime 变才重算。

### 4.3 批量更新检查

**Modrinth（首选）**：`POST /version_files/update` body `{hashes,algorithm:"sha1",loaders,game_versions,version_types:["release"]}` → `hash→latest version`。比较 latest 主文件 sha1 与本地：相同=最新，不同=有更新，不在返回=无兼容构建/未知。一次覆盖整个实例；实测 1000 hash 返回 200，官方未写上限，工程按 **500/批**。需要 UI diff 的当前版本号时再调 `POST /version_files` 或读本地元数据。**[实测/未验证上限]**

**CurseForge（补充）**：fingerprint 反查 → `POST /v1/mods` 批量拿 `latestFilesIndexes`，按 `(gameVersion,modLoader,releaseType)` 过滤，按 fileDate 选最新候选 → `POST /v1/mods/files` 拿详情比较 `fileId/hash`。CF 没有批量“查最新兼容”端点，只能 N 个项目查询；限流、合并、缓存 6h，不与搜索共享突发额度。

**状态机**：`UpToDate|UpdateAvailable|NoCompatibleBuild|UnknownLocal|DistributionDenied|Error`。默认不自动更新、不降级、不跨大版本；beta/alpha 需显式开启；用户 pin 的 mod 跳过；更新前展示 changelog 与目标 loader/gameVersion。

## 5. 搜索 UX 与工程

| 机制 | 设计 |
|---|---|
| 防抖 | Dart 300ms；C# 不再防抖，避免双重延迟 |
| 取消 | 每次输入生成 `taskId`；发新请求前 `archoera_cancel(old)`；C# `CancellationToken` 传到 `SendAsync/ReadAsStreamAsync` |
| single-flight | key=provider+规范化 query+排序 facets+sort+page+target；并发合并为一个 Task；订阅者全取消才取消底层 |
| 缓存 | SQLite `resource_cache(key,provider,etag,last_modified,fetched_at,expires_at,payload,stale)`；搜索 60s、详情 10min、版本 5min、标签 24h |
| 条件请求 | 有 ETag 才发 `If-None-Match`；304 只更新 fetched_at。Modrinth 实测无 ETag，TTL 为主 |
| 离线 | 最近 50 个搜索 key×首页 + 500 个详情，LRU 20MB；返回 `stale=true/cachedAt`，UI 标注“缓存” |
| 分页保持 | `SearchSession{queryKey,pages[],nextOffset,totalHits,scrollOffset}`；追加去重 `(provider,id)`；筛选变化即新 session；刷新保留旧列表直到新页到达 |
| 错误 UX | 内联错误面板+重试+“查看离线缓存”；禁止只弹 toast；429 显示倒计时 |
| 图标 | Flutter 直接加载 CDN；C# 仅后台预热；失败占位图，不阻塞列表 |

错误码（C ABI envelope `{ok,data,error}`）：

| code | 触发 | 可重试 | UI |
|---|---|---|---|
| `INVALID_ARGUMENT` | 400/参数校验 | 否 | 修正筛选 |
| `NOT_FOUND` | 404/未知 hash | 否 | 空态 |
| `RATE_LIMITED` | 429/CF 403 配额 | 是 | 倒计时+降级 |
| `PROVIDER_UNAVAILABLE` | 5xx/DNS/TLS/超时 | 是 | 重试 |
| `PAGINATION_LIMIT` | CF >10000 | 否 | 收窄筛选 |
| `DISTRIBUTION_DENIED` | CF 作者禁转 | 否 | 去官网 |
| `SCHEMA_UNSUPPORTED` | 未知枚举/缺必填 | 否 | 报错+日志 |
| `CANCELLED` / `OFFLINE_CACHE_MISS` / `INTERNAL` | 取消 / 离线无缓存 / 其他 | 否/是/视情况 | 静默/重试/诊断 |

## 6. 本地搜索是否值得做 / FTS5

| 场景 | 建议 | 理由 |
|---|---|---|
| 实例列表、Mod 文件名、已装 mod 名 | **内存索引**，不建 FTS | 典型 <10k 条，启动构建几十 ms；前缀/子串足够 |
| 资源库缓存的 mod 名称/描述 | 可选 FTS5 | 文本量大、需相关性排序；但必须先有稳定语料 |
| 日志 | **流式扫描**，默认不建 FTS5 | GB 级、追加写；FTS 写放大与维护成本高 |
| 中文搜索 | 若用 FTS5，用 `tokenize='trigram'` | `unicode61` 不切中文；trigram 支持子串，但 **MATCH 至少 3 字符** |

实测（SQLite 3.54）：trigram 表插入“钠优化整合包”，`MATCH '优化'` 返回 0，`MATCH '优化整'` 返回 1；`LIKE '%优化%'` 可命中但扫描。**2 字中文必须回退 LIKE/内存；3 字以上才用 trigram。** .NET 用 `Microsoft.Data.Sqlite`（SQLitePCLRaw bundle），启动执行 `PRAGMA compile_options` 确认 `ENABLE_FTS5`，不要假设。**[实测/需目标包验证]**
```sql
CREATE TABLE resource_project(provider TEXT,id TEXT,slug TEXT,kind TEXT,name TEXT,summary TEXT,icon_url TEXT,
  downloads INTEGER,follows INTEGER,updated_at TEXT,raw_json BLOB,fetched_at INTEGER,PRIMARY KEY(provider,id));
CREATE VIRTUAL TABLE resource_fts USING fts5(name,summary,content='resource_project',content_rowid='rowid',tokenize='trigram');
-- 只索引摘要；日志不建表；触发器或 C# 事务内同步
```

## 7. C# 接口与 DTO
```csharp
public enum ResourceProvider { Modrinth, CurseForge }
public enum ResourceKind { Mod, Modpack, ResourcePack, Shader, Datapack, World, Plugin, Unknown }
public enum SortBy { Relevance, Downloads, Follows, Updated, Newest }
public enum ReleaseChannel { Release, Beta, Alpha }
public enum GameSide { Client, Server, Both }
public sealed record InstanceTarget(string GameVersion, string Loader, string? LoaderVersion = null, GameSide Side = GameSide.Client, string? ShaderLoader = null);
public sealed record ResourceRef(ResourceProvider Provider, string ProjectId, string? Slug = null, ResourceKind Kind = ResourceKind.Unknown);
public sealed record ResourceSearchRequest(ResourceProvider Provider, string Query = "", ResourceKind Kind = ResourceKind.Mod, InstanceTarget? Target = null, IReadOnlyList<string>? Loaders = null, IReadOnlyList<string>? GameVersions = null, IReadOnlyList<string>? Categories = null, SortBy Sort = SortBy.Relevance, int Page = 0, int PageSize = 20, bool IncludeSnapshots = false);
public sealed record ResourceSearchResult(string Provider, int TotalHits, int Page, int PageSize, IReadOnlyList<ResourceHit> Hits, bool Stale, DateTimeOffset? CachedAt, string? RequestId);
public sealed record ResourceHit(string Id, string Slug, string Name, string Summary, string? Author, string? IconUrl, long Downloads, long? Follows, DateTimeOffset? UpdatedAt, ResourceKind Kind, IReadOnlyList<string> Categories, IReadOnlyList<string> Loaders, IReadOnlyList<string> GameVersions, string? LatestVersionId, string? License);
public sealed record ResourceProject(ResourceRef Ref, string Name, string Summary, string Body, string BodyFormat, string? Author, string? IconUrl, long Downloads, long? Follows, DateTimeOffset? PublishedAt, DateTimeOffset? UpdatedAt, ResourceKind Kind, IReadOnlyList<string> Categories, IReadOnlyList<string> Loaders, IReadOnlyList<string> GameVersions, string? License, bool? ClientSide, bool? ServerSide);
public sealed record ResourceVersion(string Id, string ProjectId, string Name, string VersionNumber, ReleaseChannel Channel, IReadOnlyList<string> GameVersions, IReadOnlyList<string> Loaders, DateTimeOffset PublishedAt, long Downloads, IReadOnlyList<ResourceFile> Files, IReadOnlyList<ResourceDependency> Dependencies, string? Changelog);
public sealed record ResourceFile(string FileId, string FileName, string Url, long Size, string? Sha1, string? Sha512, bool Primary, bool DistributionAllowed, long? Fingerprint);
public sealed record ResourceDependency(string ProjectId, string? VersionId, string? FileName, DependencyKind Kind);
public enum DependencyKind { Required, Optional, Incompatible, Embedded, Tool, Include }
public sealed record LocalFileRef(string Path, string Sha1, long Size, string? ModId, string? Version);
public sealed record UpdateCheckRequest(ResourceProvider Provider, InstanceTarget Target, IReadOnlyList<LocalFileRef> Files, bool IncludeBeta = false, bool IncludeAlpha = false);
public enum UpdateState { UpToDate, UpdateAvailable, NoCompatibleBuild, UnknownLocal, DistributionDenied, Error }
public sealed record UpdateItem(string Path, UpdateState State, ResourceVersion? Latest, string? Reason);
public sealed record UpdateCheckResult(IReadOnlyList<UpdateItem> Items, DateTimeOffset CheckedAt);
public interface IResourceService
{
    Task<ResourceSearchResult> SearchAsync(ResourceSearchRequest request, CancellationToken ct);
    Task<ResourceProject> GetDetailAsync(ResourceRef request, CancellationToken ct);
    Task<IReadOnlyList<ResourceVersion>> GetVersionsAsync(ResourceRef project, InstanceTarget? target, int page, int pageSize, CancellationToken ct);
    Task<UpdateCheckResult> CheckUpdatesAsync(UpdateCheckRequest request, CancellationToken ct);
}
```
- AOT：DTO 全部 `JsonSerializerContext` 源生成，禁止运行时反射；C ABI 层只做 `params_json→DTO→service→result_json`。
- `archoera_call("resource.search",json)` 立即返回 `task_id`；`archoera_cancel` 映射 `CancellationTokenSource`；结果经事件/`archoera_result`，不阻塞 FFI 线程。
- `HttpClient` 每 provider 一个；`SocketsHttpHandler` 开 `AutomaticDecompression`、`ConnectTimeout=10s`、`PooledConnectionLifetime=5min`；总超时 30s（linked CTS）。
- 列表页不取 `body/changelog`；详情与版本分开缓存，避免一次搜索拉 MB 级 Markdown。

## 8. 五个典型请求（JSON）

> envelope：`{"ok":true,"data":...}`；错误 `{"ok":false,"error":{"code":"RATE_LIMITED","retryable":true,"retryAfterMs":8000}}`。示例为实测截断或结构示意。

**8.1 `resource.search`（Modrinth，按下载排序）**
```text
→ {"method":"resource.search","params":{"provider":"modrinth","query":"sodium","kind":"mod","target":{"gameVersion":"1.21.1","loader":"fabric"},"sort":"downloads","page":0,"pageSize":20}}
  GET /v2/search?query=sodium&facets=[["project_type:mod"],["categories:fabric"],["versions:1.21.1"]]&index=downloads&offset=0&limit=20
← {"ok":true,"data":{"provider":"modrinth","totalHits":47,"page":0,"pageSize":20,"stale":false,"hits":[{"id":"AANobbMI","slug":"sodium","name":"Sodium","summary":"A high-performance rendering engine replacement for Minecraft.","author":"jellysquid3","iconUrl":"https://cdn.modrinth.com/data/AANobbMI/295862f4724dc3f78df3447ad6072b2dcd3ef0c9_96.webp","downloads":249863192,"follows":41963,"updatedAt":"2026-10-15T17:51:36Z","kind":"mod","categories":["fabric","neoforge","optimization","quilt"],"loaders":["fabric","neoforge","quilt"],"gameVersions":["1.21.1","1.21.2","1.21.3"],"latestVersionId":"SMxNOGZ6","license":"LicenseRef-Polyform-Shield-1.0.0"}]}}
```
**8.2 `resource.search`（CurseForge，同一个 UI 请求）**
```text
→ {"method":"resource.search","params":{"provider":"curseforge","query":"jei","kind":"mod","target":{"gameVersion":"1.21.1","loader":"forge"},"sort":"downloads","page":0,"pageSize":20}}
  GET /v1/mods/search?gameId=432&classId=6&gameVersion=1.21.1&modLoaderType=1&searchFilter=jei&sortField=6&sortOrder=desc&index=0&pageSize=20
← {"ok":true,"data":{"provider":"curseforge","totalHits":1,"page":0,"pageSize":20,"stale":false,"hits":[{"id":"238222","slug":"jei","name":"Just Enough Items (JEI)","summary":"View Items and Recipes","author":"mezz","iconUrl":"https://media.forgecdn.net/...","downloads":480000000,"follows":null,"updatedAt":"2026-09-01T00:00:00Z","kind":"mod","categories":["armor"],"loaders":["forge"],"gameVersions":["1.21.1"],"latestVersionId":"5234567","license":null}]}}
```
**8.3 `resource.detail`（Modrinth，按 slug）**
```text
→ {"method":"resource.detail","params":{"provider":"modrinth","projectId":"AANobbMI","slug":"sodium","kind":"mod"}}
← {"ok":true,"data":{"ref":{"provider":"modrinth","projectId":"AANobbMI","slug":"sodium","kind":"mod"},"name":"Sodium","summary":"A high-performance rendering engine replacement...","body":"# Sodium\n...","bodyFormat":"markdown","author":"jellysquid3","iconUrl":"https://cdn.modrinth.com/...","downloads":250138078,"follows":41974,"publishedAt":"2021-01-03T00:53:34Z","updatedAt":"2026-10-15T17:51:36Z","kind":"mod","categories":["fabric","neoforge","optimization","quilt"],"loaders":["fabric","neoforge","quilt"],"gameVersions":["1.21.1","1.21.2","1.21.3"],"license":{"id":"LicenseRef-Polyform-Shield-1.0.0","name":"","url":"https://github.com/CaffeineMC/sodium/blob/dev/LICENSE.md"},"clientSide":true,"serverSide":false}}
```
**8.4 `resource.versions`（CurseForge，按实例过滤）**
```text
→ {"method":"resource.versions","params":{"provider":"curseforge","projectId":"238222","kind":"mod","target":{"gameVersion":"1.21.1","loader":"forge"},"page":0,"pageSize":20}}
  GET /v1/mods/238222/files?gameVersion=1.21.1&modLoaderType=1&index=0&pageSize=20
← {"ok":true,"data":[{"id":"5234567","projectId":"238222","name":"jei-1.21.1-19.0.0.1.jar","versionNumber":"19.0.0.1","channel":"release","gameVersions":["1.21.1"],"loaders":["forge"],"publishedAt":"2026-08-01T10:00:00Z","downloads":123456,"files":[{"fileId":"5234567","fileName":"jei-1.21.1-19.0.0.1.jar","url":"https://edge.forgecdn.net/files/...","size":1234567,"sha1":"0123456789abcdef0123456789abcdef01234567","sha512":null,"primary":true,"distributionAllowed":true,"fingerprint":123456789}],"dependencies":[{"projectId":"238222","versionId":null,"fileName":null,"kind":"required"}],"changelog":null}]}
```
**8.5 `resource.checkUpdates`（Modrinth 批量）**
```text
→ {"method":"resource.checkUpdates","params":{"provider":"modrinth","target":{"gameVersion":"1.21.1","loader":"fabric"},"files":[{"path":"mods/sodium.jar","sha1":"003c114c85ca88ef3362e018deb6aca0c682d6a1","size":1574609,"modId":"sodium","version":"0.8.13"},{"path":"mods/old.jar","sha1":"0000000000000000000000000000000000000000","size":1000,"modId":null,"version":null}],"includeBeta":false}}
  POST /v2/version_files/update {"hashes":["003c...","0000..."],"algorithm":"sha1","loaders":["fabric"],"game_versions":["1.21.1"],"version_types":["release"]}
← {"ok":true,"data":{"checkedAt":"2026-10-20T11:30:00Z","items":[{"path":"mods/sodium.jar","state":"up_to_date","latest":{"id":"SMxNOGZ6","versionNumber":"mc1.21.1-0.8.13-fabric"},"reason":null},{"path":"mods/old.jar","state":"unknown_local","latest":null,"reason":"hash not found"}]}}
```

## 9. 测试用例（必须覆盖）

- facets：内层 OR/外层 AND；`categories:fabric`+`versions:1.21.1`；loader 当 category。
- 分页：Modrinth `limit=101→100`、`limit=0→1`、`offset=100000` 空；CF `index+pageSize=10000` 边界/超限。
- 取消/single-flight/缓存：连续输入 10 次仅最后一次存活，旧 task 不写缓存；同 key 并发 5 次只发 1 个 HTTP，全取消才取消底层；TTL 过期刷新、304 只更新 fetched_at、ETag 缺失正常回退、离线有/无缓存两条路径。
- 兼容性：项目级并集但版本不兼容；`1.21` vs `1.21.1`；shader iris/optifine；resourcepack `minecraft`。
- 更新/整合包：未知 hash、改过的 jar、无兼容构建、CF `downloadUrl=null`、beta 关闭不选 beta；文件被删、`required:false`、overrides 含 `../`/绝对路径/symlink、manifestVersion 未知。

## 10. 来源与待验证

- Modrinth 搜索/facets/分页：https://docs.modrinth.com/api/operations/searchprojects/ ；批量 hash：https://docs.modrinth.com/api/operations/versionsfromhashes/ ；单文件更新：https://docs.modrinth.com/api/operations/getlatestversionfromhash/ ；限流/UA：https://docs.modrinth.com/api/#ratelimits
- CF manifest 实例：https://github.com/Fabulously-Optimized/fabulously-optimized/blob/main/CurseForge/manifest.json ；fingerprint（社区）：https://docs.rs/furse/latest/furse/fn.cf_fingerprint.html
- SQLite FTS5/trigram：https://www.sqlite.org/fts5.html#the_trigram_tokenizer
- **待验证**：CF classId 6945/17/5；CF 真实速率限制与 403 语义；CF `downloadUrl=null` 服务端行为；.NET 目标包 FTS5 编译选项；Modrinth 批量 hash 官方上限；`pack_format` 与游戏版本映射。
