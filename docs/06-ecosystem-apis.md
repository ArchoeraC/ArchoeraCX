# 06 · 生态接口与数据源

> 这是"开工时要查什么"的索引，不是完整协议文档。
> 所有接口都可能变化，**以官方文档与实际响应为准**；本文件只保证方向正确。

---

## 1. 版本清单与版本 JSON

### 1.1 清单

```
GET https://piston-meta.mojang.com/mc/game/version_manifest_v2.json
```

| 字段 | 用途 |
|---|---|
| `latest.release` / `latest.snapshot` | 首页"最新版本" |
| `versions[]` | `id` / `type`(release, snapshot, old_beta, old_alpha) / `url` / `sha1` / `releaseTime` / `complianceLevel` |
| `sha1` | 校验版本 JSON，缓存与增量更新依据 |

策略：本地缓存 + `If-None-Match`/`sha1` 比对；启动时异步刷新，不阻塞 UI。

### 1.2 版本 JSON 关键字段

| 字段 | 说明 |
|---|---|
| `id` / `type` / `releaseTime` | 元信息 |
| `mainClass` | 入口类 |
| `inheritsFrom` | 加载器版本继承原版（合并规则：子覆盖父） |
| `assets` / `assetIndex` | 资源索引 id、url、sha1、totalSize |
| `downloads.client` | 客户端 jar（url/sha1/size） |
| `javaVersion.majorVersion` / `.component` | 所需 Java 主版本与 Mojang 运行时组件名 |
| `libraries[]` | Maven 坐标 + `downloads.artifact` + `natives` + `rules` + `extract` |
| `arguments.game[]` / `arguments.jvm[]` | 1.13+ 结构化参数（含 `rules`） |
| `minecraftArguments` | 1.13 以前的字符串参数（老版本） |
| `logging.client` | log4j 配置（**安全相关**，见 §8） |
| `minimumLauncherVersion` | 兼容性提示 |
| `complianceLevel` | 合规级别（0/1） |

### 1.3 规则（rules）求值

- 默认 **deny**；按顺序遍历，后匹配的规则覆盖前面的结果。
- 条件：`os.name`（windows/osx/linux）、`os.arch`（x86/x86_64/arm64）、`os.version`（正则）、`features`（`has_custom_resolution`、`is_demo_user`、`has_quick_plays_support` 等）。
- **必须与官方一致**，否则会出现"少一个库"或"多一个 native"的诡异崩溃。

### 1.4 参数占位符（常见）

| 占位符 | 来源 |
|---|---|
| `${auth_player_name}` | 玩家名 |
| `${version_name}` | 版本 id |
| `${game_directory}` | 实例 gameDir |
| `${assets_root}` / `${assets_index_name}` | 资源目录 / 索引 id |
| `${auth_uuid}` / `${auth_access_token}` / `${user_type}` | 账户（`user_type` 通常 `msa`） |
| `${clientid}` / `${auth_xuid}` | 微软账户相关 |
| `${natives_directory}` | 解压后的 natives 目录 |
| `${launcher_name}` / `${launcher_version}` | 你的启动器信息 |
| `${classpath}` / `${classpath_separator}` / `${library_directory}` | 1.18+ 新版参数使用 |
| `${resolution_width}` / `${resolution_height}` | 自定义分辨率 |

> **安全**：命令行会出现在进程列表里，token 无法完全隐藏；这是官方启动器同样存在的限制。日志中必须脱敏。

### 1.5 资源文件

```
https://resources.download.minecraft.net/<hash[0:2]>/<hash>
```

- 按 sha1 落盘到 `assets/objects/xx/xxxx`，索引文件 `assets/indexes/<id>.json`。
- 1.7 以前需要 `virtual/legacy` 目录映射（老版本兼容）。

---

## 2. 微软登录链路

```
MSA OAuth (device code)
  → Xbox Live (XBL)
    → XSTS
      → Minecraft Services (login_with_xbox)
        → /minecraft/profile
```

| 步骤 | 端点 | 关键点 |
|---|---|---|
| 设备码 | `POST https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode` | `client_id`=自有应用；`scope=XboxLive.signin offline_access` |
| 轮询 | `POST .../oauth2/v2.0/token` | `grant_type=urn:ietf:params:oauth:grant-type:device_code`；处理 `authorization_pending` / `slow_down` / `expired_token` |
| XBL | `POST https://user.auth.xboxlive.com/user/authenticate` | `RpsTicket: "d=<access_token>"`；`RelyingParty: http://auth.xboxlive.com` |
| XSTS | `POST https://xsts.auth.xboxlive.com/xsts/authorize` | `RelyingParty: rp://api.minecraftservices.com/`；返回 `UHS` 与 token |
| MC 登录 | `POST https://api.minecraftservices.com/authentication/login_with_xbox` | `identityToken: "XBL3.0 x=<uhs>;<xsts>"` |
| 档案 | `GET https://api.minecraftservices.com/minecraft/profile` | Bearer；返回 uuid/name/skins/capes |
| 拥有验证 | `GET https://api.minecraftservices.com/entitlements/mcstore` | 判断是否拥有游戏 |

**必读注意**

- 桌面应用是**公共客户端**，不要内嵌 client secret。
- 必须注册**自己的** Entra 应用；不要复用 Mojang/其他启动器的 client id。
- **Entra 应用注册后还需向 Minecraft 提交 client_id 审核**（`https://aka.ms/mce-reviewappid`；旧入口 `AppRegInfo`）。未获准可能 OAuth/XBL/XSTS 全通过，但 `login_with_xbox` 返回 `403 Invalid app registration`。
- refresh token 存 OS 凭据库；`offline_access` 才拿得到。
- 常见 XSTS 错误：无 Xbox 账户、儿童账户未获家长同意、地区限制 —— 要给**人话提示**，不是抛 HTTP 码。

### 2.1 重定向方案（已定）

| 方案 | 用途 | 结论 |
|---|---|---|
| **Device Code Flow** | 默认微软登录 | **首选**；没有 redirect URI，无需本地 HTTP 监听；适合桌面/无浏览器回调场景 |
| **Loopback + auth code + PKCE** | 设备码不可用/用户要求浏览器登录时的备选 | 注册 `http://localhost/callback`；运行时 `http://localhost:<ephemeral>/callback`；PKCE S256 + state；仅绑定 `127.0.0.1`，校验 loopback Host，短超时，回调后立即关闭监听 |
| 自定义协议 `archoeracx://oauth/callback` | 深链/商店场景 | **不作为微软 OAuth 主方案**；仅在其他平台能力明确需要时作为次要入口 |
| authlib-injector | 第三方 Yggdrasil | 不是 OAuth 浏览器回调；走 `/authserver/*` + `-javaagent` |

- Entra 对 `http://localhost` 的端口匹配是宽松的；`http://127.0.0.1` 通常需改应用 manifest 才能加入。
- 不要在 URL 查询参数里长期保留 token；授权码换 token 后立即结束回调。
- 公共客户端禁止 client secret；token 只进 OS 凭据库，日志/诊断包脱敏。

---

## 3. 第三方认证（authlib-injector）

- 以 `-javaagent:authlib-injector.jar=<API 根地址>` 注入，可加 `-Dauthlibinjector.yggdrasil.prefetched=<base64>` 预取元数据。
- 需要实现/对接的 Yggdrasil 端点：`/authserver/authenticate`、`/authserver/refresh`、`/sessionserver/session/minecraft/profile/<uuid>`。
- 许可证 **AGPL-3.0**：作为独立 jar 分发时，提供其源码获取方式即可（链接官方仓库通常足够）。
- 不要修改游戏 jar；不要绕过正版验证做在线服务。

---

## 4. 加载器

| 加载器 | 元数据 / 安装 | 备注 |
|---|---|---|
| **Fabric** | `https://meta.fabricmc.net/v2/versions/loader/{game}/{loader}/profile/json` | 直接返回可合并的版本 JSON；最省事 |
| **Quilt** | `https://meta.quiltmc.org/v3/versions/loader/{game}/{loader}/profile/json` | 与 Fabric 类似，注意包名差异 |
| **Forge** | Maven metadata → installer jar → `install_profile.json` + processors | 需运行 Java 处理器；产物 `forge-<ver>-client.jar` |
| **NeoForge** | `https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml` | 流程与 Forge 类似，版本号规则不同（`21.1.x`）；该 Maven 在部分网络会连接超时：境内网络版本列表直接走 BMCLAPI 镜像（官方兜底），Forge 的版本列表则用 BMCLAPI 的 per-game JSON（其镜像的 maven-metadata 已过期），与 HMCL/SJMCL 的做法一致 |
| **OptiFine** | 无公开 API | 站点条款限制自动化，建议引导手动放置 |

### Forge/NeoForge 安装要点

1. 下载 installer jar（含 `install_profile.json`、`version.json`、`data/`）。
2. 解析 `install_profile.json`：`spec`（0/1）、`data`（含 `client`/`server` 映射）、`processors[]`。
3. 按顺序执行 processors（`java -cp <processor classpath> <mainClass> --keys...`），替换 `{MINECRAFT_JAR}`、`{SIDE}`、`{INSTALLER}`、`{LIBRARY_DIR}`、`{ROOT}` 等占位符。
4. 产出 client jar 与版本 JSON，合并进实例。
5. **产物/中间文件要可回滚**；失败留下半成品是最常见的用户投诉。

### 版本兼容

- 加载器版本与游戏版本强绑定，必须做矩阵校验。
- 1.20.5+ 的 Forge/NeoForge 对 Java 21 有要求。
- Fabric API 是 Mod，不是加载器本身，别混在安装流程里。

---

## 5. 资源平台

### 5.1 Modrinth（API v2）

| 端点 | 用途 |
|---|---|
| `GET /v2/search?query=&facets=&limit=&offset=` | 搜索（facet 支持 project_type、versions、categories、loader） |
| `GET /v2/project/{id\|slug}` | 项目详情 |
| `GET /v2/project/{id}/version` | 版本列表（含 game_versions、loaders） |
| `GET /v2/version/{id}` | 单版本（files[].hashes、url） |
| `GET /v2/version_file/{hash}?algorithm=sha1` | 按文件 hash 反查（更新检查的关键） |
| `GET /v2/tag/...` | 分类/加载器/游戏版本标签 |

- 必须设置可识别的 `User-Agent`，遵守速率限制。
- **按 hash 反查**是"本地 Mod 更新检查"的核心能力，务必内置。

### 5.2 `.mrpack` 格式

```
modrinth.index.json
overrides/            # 服务端与客户端共用
client-overrides/     # 仅客户端
server-overrides/     # 仅服务端
```

`modrinth.index.json` 关键字段：

```json
{
  "formatVersion": 1,
  "game": "minecraft",
  "versionId": "1.0.0",
  "name": "Pack",
  "dependencies": { "minecraft": "1.20.1", "fabric-loader": "0.15.0" },
  "files": [
    { "path": "mods/foo.jar", "hashes": { "sha1": "...", "sha512": "..." },
      "env": { "client": "required", "server": "optional" },
      "downloads": ["https://cdn.modrinth.com/..."], "fileSize": 12345 }
  ]
}
```

- 安装：校验 hash → 下载 → 落到 `path`；`overrides` 覆盖到实例目录。
- 安全：`path` 必须规范化，禁止 `..` 与绝对路径。

### 5.3 CurseForge

| 项 | 说明 |
|---|---|
| API | `https://api.curseforge.com/v1`（需要 API key；provider 未注册时不发请求） |
| 当前状态 | **Provider 未注册**：在线接口与 zip 导入都依赖 API key；`provider=curseforge` 在发起 HTTP 前返回 `PROVIDER_UNAVAILABLE` |
| 关键端点 | `/mods/search`、`/mods/{id}`、`/mods/{id}/files`、`/mods/{id}/files/{fileId}/download-url` |
| 整合包 | zip 内 `manifest.json`：`minecraft.version`、`modLoaders[{id, primary}]`、`files[{projectID, fileID, required}]`、`overrides` |
| 条款 | 内容版权归作者与平台；接入实现必须持有 API key，且不得绕过作者分发限制 |

### 5.4 本地 Mod 元数据

| 加载器 | 文件 | 用途 |
|---|---|---|
| Forge/NeoForge | `META-INF/mods.toml` / `META-INF/neoforge.mods.toml` | modId、版本、依赖 |
| Fabric | `fabric.mod.json` | id、version、depends |
| Quilt | `quilt.mod.json` | 同上 |

用于：启停、依赖提示、重复 modId 检测、更新检查。

### 5.5 搜索与浏览的工程约定

> 归属：**C# 统一实现**。Dart 只做输入防抖、取消与渲染。

**统一 DTO**（Modrinth / CurseForge 都要映射到同一结构，UI 不感知来源）：

```json
{
  "provider": "modrinth",
  "total": 1240,
  "page": 1,
  "pageSize": 20,
  "hits": [{
    "id": "AABBCC",
    "slug": "sodium",
    "name": "Sodium",
    "summary": "Modern rendering engine...",
    "author": "jellysquid3",
    "iconUrl": "https://cdn.modrinth.com/...",
    "downloads": 12345678,
    "follows": 3456,
    "updatedAt": "2026-09-01T12:00:00Z",
    "categories": ["optimization"],
    "loaders": ["fabric", "quilt"],
    "gameVersions": ["1.21.4", "1.21.3"]
  }]
}
```

**接口形状**

```
method: resource.search
params: {
  provider: "modrinth" | "curseforge",
  query: "sodium",
  projectType: "mod" | "modpack" | "resourcepack" | "shader",
  loaders: ["fabric"],
  gameVersions: ["1.21.4"],
  categories: ["optimization"],
  sort: "relevance" | "downloads" | "updated" | "newest",
  page: 0, pageSize: 20,
  instanceId: "..."        // 可选：按实例上下文过滤
}
→ result: 统一 DTO；可取消（taskId）
```

**缓存与限流**

| 项 | 建议 |
|---|---|
| 搜索缓存 | 60s TTL，键 = (provider, query, facets, sort, page) |
| 详情缓存 | 10min TTL；用 ETag / `If-None-Match` |
| 标签缓存 | 24h（加载器、游戏版本、分类） |
| 限流 | 每 provider 一个 token bucket；CurseForge 更保守 |
| 合并 | 同一 key 的并发请求合并为一次（single-flight） |
| 离线 | 最近 N 条搜索结果落 SQLite，断网时可展示并标注"缓存" |

**交互约定**

- 输入防抖 **300ms**；新请求发出前取消旧 task（不要等旧请求返回）。
- 滚动加载下一页时保留旧结果，避免闪烁。
- 搜索失败给"重试 + 查看离线缓存"，不要只弹 toast。
- 图标：优先本地缓存（C# 后台下载），否则 Flutter `Image.network`；必须有占位图。

**安全与合规**

- `User-Agent` 统一格式，例如 `ArchoeraCX/0.0.1+1 (contact@example.com)`。
- 不在客户端做全量抓取/镜像；遵守各平台速率与条款。

**更新检查**

- 读取本地 Mod 文件的 sha1 → `GET /v2/version_file/{sha1}?algorithm=sha1` → 取最新兼容版本。
- 这一步也归 C#，因为它需要扫描实例目录并写回元数据。

---

## 6. Java 运行时

| 游戏版本 | Java |
|---|---|
| ≤ 1.16.5 | 8 |
| 1.17 | 16 |
| 1.18 – 1.20.4 | 17 |
| 1.20.5 – 1.21.x | 21 |
| 26.x | 25（以 `javaVersion.majorVersion` 为准） |
| 更新版本 | 以 `javaVersion.majorVersion` 为准；上表只覆盖到 26.x |

**来源**

- Mojang 官方运行时清单（`java-runtime` manifest）：与版本 JSON 的 `javaVersion.component` 对应，最稳。
- Adoptium API v3：`https://api.adoptium.net/v3/binary/latest/{major}/ga/{os}/{arch}/jre/hotspot/normal/eclipse`
- Azul Zulu Metadata API：适合 Windows/macOS 的 JRE 分发（核对再分发条款）。

**不要**打包 Oracle JDK。Temurin 为 GPLv2 + Classpath Exception，可再分发。

---

## 7. 镜像与下载

| 镜像 | 说明 |
|---|---|
| BMCLAPI | `https://bmclapi2.bangbang93.com/`，社区维护，覆盖版本清单/库/资源；遵守其使用说明 |
| 官方 | `piston-meta` / `libraries.minecraft.net` / `resources.download.minecraft.net` |
| 自定义 | 允许用户填镜像根地址，但需白名单 + 哈希校验 |

**下载器设计**

- 文件分级：小文件（<1MB）批量合并请求；大文件分片 + Range。
- 并发：全局 16–32，按域名分别限流，避免把镜像打挂。
- 校验：sha1（官方）/ sha512（Modrinth）落盘前校验，失败重试换源。
- 原子写：`.part` → 校验 → rename，避免半文件。
- 去重：以 hash 为键索引，跨实例共享。
- 代理：尊重系统代理 + 手动配置。

---

## 8. 安全要点（容易踩）

| 项 | 说明 |
|---|---|
| Log4Shell | 1.7–1.18 的版本必须使用 Mojang 提供的 `logging.client` 配置（`-Dlog4j.configurationFile=...`），不要自己拼 |
| Zip Slip | 所有解压（整合包、资源包、加载器）都要规范化路径并校验前缀 |
| 路径长度 | Windows 260 限制；长路径需 `\\?\` 前缀或缩短实例名 |
| 符号链接 | 整合包内可能含 symlink，解压时拒绝或严格限制 |
| 文件锁 | Windows 下 jar 被占用会导致更新失败；先停游戏再更新 |
| Token 泄漏 | 不进日志、不进崩溃报告、不进 UI 进程内存 |
| 更新劫持 | 校验签名，不只校验服务端给的 hash |
| 第三方 jar | 提示风险，不在启动器内自动执行未知代码 |

---

## 9. 条款速查

| 对象 | 要做 | 不要做 |
|---|---|---|
| Mojang | 从官方源下载游戏文件；展示免责声明 | 分发游戏本体/assets；使用官方 Logo 做图标 |
| Microsoft | 自有 Entra 应用；device code；安全存储 token | 复用他人 client id；内嵌 client secret |
| Modrinth | 设置 UA，遵守限速，标注来源 | 把 CDN 当自己的存储对外分发 |
| authlib-injector | 保留 AGPL 声明与源码链接 | 修改后闭源分发 |
| Java | 用 Temurin/Zulu 并遵守其条款 | 打包 Oracle JDK |
| 字体/图标 | 使用 OFL/MIT/ISC 资源并署名 | 使用商业字体（如 DINPro）或未授权素材 |

---

## 10. 实现顺序建议

1. 版本清单 + 版本 JSON + rules 求值（一切的基础）
2. 下载器 + assets/libraries（可独立测试）
3. 启动参数拼装 + 进程管理
4. 微软认证
5. Fabric（最简单）→ Quilt → Forge → NeoForge
6. Modrinth（API 最友好）→ `.mrpack` → CurseForge
7. 更新检查（按 hash 反查）
8. 崩溃诊断与日志脱敏
