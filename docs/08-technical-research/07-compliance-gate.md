# 07 · 合规开关：分级下载 + 开发者模式 + 知情同意

> 目标：下载能力按发行通道**分级**。默认构建 `official-cn` 为 L0（无内容下载）；`official-global` 单独构建默认 L1（官方必需内容），L2 需应用内确认。
> 本文以 `official-cn` 的高摩擦模式为主要案例；最终语义以 [`../10-architecture-design.md`](../10-architecture-design.md) §15 为准。
> 标记：**[事实]** 有源码/PR/实测；**[推断]** 基于代码与协议的判断；**[未验证]** 需本项目实测。
> HMCL 证据基线：`HMCL-dev/HMCL` commit `8827fd0`（2026-10-20，`优化地区检测逻辑 (#7020)`），本地只读副本。
> HMCL 为 GPL-3.0：只记录**行为与方法**，不复制代码、文案、布局。

---

## 0. 结论速览

| # | 结论 | 依据 |
|---|---|---|
| 1 | HMCL **没有** `hmcl.disable-download`、`--disable-download`、开发者模式或下载免责声明 | §1.1，全仓检索 0 命中 **[事实]** |
| 2 | HMCL 真正的做法是**地区判定 + 两个 UI 页面 `setDisable(true)`**，不是可配置开关 | §1.2 **[事实]** |
| 3 | 它**只拦 UI**：后端 `DownloadProvider`/`FetchTask` 不校验，改时区即可绕过 | §1.2 **[事实+推断]** |
| 4 | 可复用的只有三条方法：失败即禁用、IO 线程预热、schema 主/次/补丁兼容 | §1.3/§1.4 |
| 5 | 本设计把开关放在 **C# 计划生产层**，Rust 只认 C# 签发的能力令牌；Dart 无决定权 | §3.7/§6 |
| 6 | 应用对配置只写**降权**；升权必须走应用内三步确认，应用永不写 `true` | §3.4 |
| 7 | `official-cn`（默认构建）默认 `L0`（无内容下载；可选 `L0-Z` 零出站）；`official-global` 单独构建默认 `L1`（仅官方必需）；`L2` 需再确认 | §2.1 |
| 8 | 本地开关**不是**法律屏障，也不是 DRM；价值是「明确意图 + 摩擦 + 可审计」 | §6/§8 |

---

## 1. HMCL 实证

### 1.1 检索范围与结果

`8827fd0` 全仓（`*.java` / `*.properties` / `*.json` / `*.md`）：

| 检索项 | 命中 | 说明 |
|---|---|---|
| `disable-download` / `disableDownload` / `disable_download` | 0 | 无此配置键/属性 |
| `--disable-download` | 0 | 启动参数只有 `--apply-to`（`HMCL/.../upgrade/UpdateHandler.java:78`）与 Terracotta 的 `--hmcl` |
| `allowDownload` / `enableDownload` / `downloadEnabled` / `developerMode` / `开发者模式` | 0 | 无下载开关，无开发者模式 |
| 三份 settings schema | 45+5+3 键 | 只有 `downloadThreads`/`autoDownloadThreads`/`fileDownloadSource`/`versionListSource`/`defaultAddonSource` 等线程与源设置，无开关 |
| GitHub Issue/PR「禁用下载」 | #7017 / #7018 | 见下 |

**结论 [事实]**：HMCL 没有「隐藏下载 / 禁用下载 / 开发者模式 / 下载免责声明」这套产品机制。
`#7017`（`在中国大陆地区禁用游戏下载功能`）2026-10-20 合并（`583e004`）；`#7018` 反向 PR **已关闭、未合并**；`#7020` 是后续判定修正，即当前 HEAD。

### 1.2 真实实现：地区判定 + UI 禁用

**判定**（`HMCL/src/main/java/org/jackhuang/hmcl/util/RegionUtils.java`）：`:42-44` 在 IO 线程预热，避免首帧阻塞；`:46-66` 时区 `Asia/Shanghai` 命中，或 UTC+8 且国家 `CN`，或 Windows `GetUserGeoID(GEOCLASS_NATION)==45`；`:73-91` 结果缓存在 `volatile Boolean`，**进程内不刷新**，并硬编码排除港澳台/新加坡等时区；`:102-106` 否则请求 `https://ipwho.is`，**异常一律返回 `true`（fail-closed）**。**执行（只有两处）**：`ui/download/VersionsPage.java:456-469` 与 `ui/instances/DownloadListPage.java:631-645`，命中时顶部插 `HintPane(WARNING)` 并 `setDisable(true)`。二者覆盖 `DownloadPage` 的 6 个 Tab（`DownloadPage.java:84-115`）与加载器安装（`AbstractInstallersPage` 内嵌 `VersionsPage`）。文案为 `I18N_zh_Hans.properties:385` 的 `download.china`，链接指向 `mc.163.com`；仅三份语言有值，其余回退英文 **[事实]**。

**性质 [事实]**：纯 UI 禁用，`DownloadProvider`/`FetchTask`/`FileDownloadTask` 均无权限校验；未覆盖启动器自更新、Java 运行时下载、皮肤上传、日志上传；无同意记录、无版本化声明、无撤回。**[推断]** 改时区或 hosts 即可绕过；`init()` 与首次 UI 判定存在竞态，最坏阻塞 FX 线程约 10s（`NetworkUtils.TIMEOUT = 10s`）。

### 1.3 同仓库里三个更接近「开关」的先例

| 先例 | 位置 | 机制 | 取舍 |
|---|---|---|---|
| 用户协议 | `ui/Controllers.java:322-340`；`UserState.java:116-117` | `agreementVersion` 整数；弹窗只有同意/拒绝，拒绝即退出；声明是外链 | **不可**照搬：无滚动、勾选、哈希、渠道 |
| 离线账户限制 | `ui/account/AccountListPage.java:56-70` | `-Dhmcl.offline.auth.restricted=auto/true/false` 或用户设置 | **可借鉴**双入口；**不可**把系统属性当边界 |
| 配置 schema 兼容 | `JsonSchema.java:44-56`；`SettingsManager.java:1172-1206` | 主版本不支持→拒读；次版本更新→只读；补丁差异→保留 `$schema` 与未知字段 | **直接采纳**（§3.5） |

反面证据：`IntegrityChecker.java:48` 用 `-Dhmcl.self_integrity_check.disable=true` 关闭自校验。写进文档的调试开关会变成最短路径；我们的旁路必须**编译期剔除**。

### 1.4 取舍与未验证项

**值得学**：判定失败即禁用（fail-closed）；判定预热放 IO 线程；schema 兼容分层。
**不可学**：用地区/时区做唯一依据（隐私、误伤、易绕过）；只在 UI 层拦；无同意与撤回。**不复制**：文案、`RegionUtils` 实现、`ipwho.is` 依赖、`mc.163.com` 导流。

- **[未找到]** 下载开关配置键、开发者模式、下载免责声明、`--disable-download`。
- **[未验证]** 其他分支、国内分发版、社区 fork 是否有私有补丁（本次只审 `8827fd0` 与公开 PR）。
- **[未验证]** `#7017` 在真实大陆网络下的命中率与误伤率（时区/GeoID/IP 不一致场景）。

### 1.5 对我们的含义（关键判断）

HMCL 的事实是：**自动地区禁用，没有用户开关**。所以「改配置文件 + 开发者模式」不是 HMCL 的方案，而是我们更宽松、也更可控的变体。先想清楚目标：

| 形态 | 强制性 | 用户体验 | 适用 |
|---|---|---|---|
| A. 地区自动禁用（HMCL） | 中（UI 层，可绕） | 无感 | 面向特定地区分发 |
| B. **构建通道分离** | 高（代码路径不存在） | 消费者无感 | 合规优先、单一产品线 |
| C. **运行期开发者模式** | 低（改文件/补丁即可开） | 需手改 + 阅读 + 确认 | 单构建、自用、开发调试 |

**建议：B + C 组合。**

- `consumer` flavor：下载 provider 在编译期被裁掉（或策略模块恒返回 `LOCKED`），UI 不出现任何开关；这是唯一接近"硬约束"的形态。
- `dev` flavor：内部/自用构建，允许 §3 的三步开启；产物不进发布渠道，CI 断言。
- 若坚持单构建（你当前的要求）：只能走 C，必须接受"它挡的是误触与随手脚本，不是逆向"。

三条底线：

1. **不要把运行期开关宣传成合规保证**；它是"知情同意 + 责任留痕"。
2. **不要抄 HMCL 的 IP/时区判定**：隐私、误伤、VPN 都会出问题；地区应由分发渠道决定，而不是探测用户。
3. **不要做"一键解锁"**：开启必须跨过"改文件 → 阅读 → 勾选 → 输入短语 → 二次确认"，且应用永不写 `true`。

> 如果产品要面向中国大陆公开分发，优先 B；C 只作为开发/自用通道。具体法律义务请咨询律师，本文只给工程机制。

---

## 2. 功能范围界定

### 2.1 什么算「下载」与分层

判定标准不是「有没有用 HTTP」，而是**是否由启动器把远端字节落到本地并可能被执行/加载**；只读元数据（版本清单、搜索）风险不同，单独列出。

| 能力 | L0 无内容下载 | L1 仅官方必需 | L2 全部 |
|---|---|---|---|
| 版本清单 / 搜索 / 详情（只读元数据） | ✓ | ✓ | ✓ |
| 游戏 jar / assets / libraries | ✗ | ✓ | ✓ |
| 加载器元数据、installer、processors 产物 | ✗ | ✓ | ✓ |
| Java 运行时（Mojang / Adoptium 官方源） | ✗ | ✓ | ✓ |
| 启动器自更新（下载并执行） | ✗（只提示 + 浏览器打开官网） | ✓（自家签名通道） | ✓ |
| Mod / 整合包 / 资源包 / 光影 / 世界 | ✗ | ✗ | ✓ |
| 远程图片（图标、头图、皮肤渲染） | ✗（本地占位） | ✓（仅元数据页） | ✓ |
| 镜像测速 / 主动探测 | ✗ | ✓ | ✓ |
| 皮肤上传、日志/崩溃上传 | ✗ | ✗ | 独立开关，默认 ✗ |

**默认值 [建议]**：默认发行通道 `official-cn` 出厂与升级后默认 `L0`；`official-global` 单独构建出厂与升级后默认 `L1`。`official-cn` 从 L0 升到 L1 需开发者模式 + 声明 + 同意；任何消费构建的 `L1→L2` 都需应用内第二次确认（记 `levelEscalatedAt`）。升级不得自动升到 L2；把「装原版」与「装任意第三方内容」分成两个风险档。

> `L0` = 不下载任何内容，但允许只读元数据（版本清单/搜索/详情）。
> `L0-Z` = 可选严格构建：**进程不发起任何出站连接**（回环除外），连元数据也不请求；用于高合规场景。这比「按钮不可点」强，能防止图片、遥测、更新检查偷偷出网。

**不受门控**：启动已有实例、离线账户、本地 Mod 扫描/启停、日志查看、崩溃本地解析、导入本地整合包、设置与主题。**门控不能把启动器变成砖头**，否则用户会去找破解版。

---

## 3. 开关机制

### 3.1 文件与所有权

| 文件 | 所有者 | 内容 |
|---|---|---|
| `<config>/compliance.json` | **用户手写** | `schemaVersion`、`downloadLevel`、`developerMode` |
| `<config>/state/consent.json` | 应用写 | 同意记录、开发者确认、撤回时间、last-known-good |
| `<config>/audit/compliance-YYYYMM.jsonl` | 应用追加 | 审计链（§4.3） |
| `<install>/policy/compliance.policy.json` | 管理员（可选） | 机器级上限，只能更低 |

`<config>` 取 `docs/02` §8。**核心不变量**：应用对 `compliance.json` **只有降权写权限**，没有升权写接口——即使 UI 出 bug 或被脚本驱动，也无法把自己打开。

### 3.2 `compliance.json`

```json
{ "$schema": "https://schemas.archoeracx.example/compliance/1.0.0",
  "schemaVersion": 1, "downloadLevel": "disabled", "developerMode": false }
```

| 字段 | 类型 | 默认 | 校验 |
|---|---|---|---|
| `schemaVersion` | int | 无（缺失即 `CONFIG_INVALID`） | 必须等于当前主版本 |
| `downloadLevel` | string enum | 通道默认（cn `disabled` / global `official` / dev `all`） | 只接受 `disabled`/`official`/`all`；大小写敏感 |
| `developerMode` | bool | `false` | 必须是 JSON `true`；`"true"`、`1`、`"yes"` 一律无效；仅 `official-cn` 需要 |

键名带语义（`downloadLevel` 而非 `enabled`），降低「全局替换 `true`」误伤；枚举拼错必然落到通道默认。未知字段保留并告警（同 HMCL patch 策略）；字段说明放 `compliance.schema.json`，文件内不写注释。

#### 3.2.1 `official-cn` 双文件模式

在 `compliance.json` 之外，`official-cn` 再要求一个**不同目录**的 `DEVELOPER_ACK.txt`：

```
ENABLE-DOWNLOADS <noticeVersion> <noticeSha256 前 16 位>
```

- 应用**永不创建**该文件，也永不写 `developerMode: true`；
- 两处都通过，才显示「开启开发者模式」入口；
- 逐项诊断（A_MISSING / A_SCHEMA / B_MISSING / B_HASH / B_VERSION），不要只报"配置无效"；
- 目的只是提高误触成本 + 留下意图证据，**不是防破解**；
- `official-global` 默认 L1，不需要该文件；升 L2 走三步同意。

完整规则与反模式见 `../09-distribution-and-download-policy.md` §2.4。

### 3.3 生效算法（C# `ComplianceGate.Evaluate`）

```text
Evaluate(channel, compliance.json, machinePolicy, remotePolicy, consent):
  defaultLevel = { cn: disabled, global: official, dev: all }[channel]
  maxLevel     = { cn: official, global: all,      dev: all }[channel]
  requested    = compliance.json.downloadLevel ?? defaultLevel

  ├─ 文件解析失败/类型错      → LOCKED(CONFIG_INVALID)
  ├─ 主版本不支持             → LOCKED(SCHEMA_UNSUPPORTED)   // 只读，不写回
  ├─ requested == disabled    → LOCKED(LEVEL_DISABLED)
  ├─ requested > maxLevel     → LOCKED(LEVEL_NOT_ALLOWED)
  ├─ channel == cn 且 requested > disabled:
  │    ├─ developerMode != true → LOCKED(NO_DEVELOPER)
  │    ├─ ACK 缺失/短语/哈希不符 → LOCKED(NO_ACK / ACK_MISMATCH)
  │    └─ consent 缺失/哈希不符  → CONSENT_REQUIRED / CONSENT_STALE
  ├─ requested == all 且 channel != dev:
  │    └─ consent 缺失/哈希不符  → CONSENT_REQUIRED / CONSENT_STALE
  ├─ revokedAt > acceptedAt   → LOCKED(REVOKED)
  └─ effective = min(requested, maxLevel, machinePolicy, remotePolicy)
```

序：`disabled < official < all`。`official-cn` 的 `maxLevel=official`（不提供第三方内容）；`official-global` 的 `defaultLevel=official`、`maxLevel=all`；`dev` 默认/最大均为 `all`。运行期策略只能降低。

### 3.4 防误触：五条硬规则

1. **升权只能发生在应用内**：文件从 `disabled` 改成 `all` 只让界面出现「继续启用」，不直接生效。
2. **运行期文件变更只降不升**：`all→disabled` 在 300ms 防抖后立即生效并取消在途任务；`disabled→all` 不生效，必须重新确认。
3. **应用永不写 `true`**：`ComplianceConfigStore` 只有 `LowerTo(level)`，没有 `Enable()`。
4. **类型严格**：JSON Schema + 手写校验双保险；类型/枚举不符按**整份无效**处理，不做「尽量解析」。
5. **原子写 + 备份**：`*.tmp` → `fsync` → `rename`；覆盖前留 `.bak`；忽略 `.tmp` 与 300ms 内连续事件，避免半写误判。

### 3.5 版本与迁移

- `$schema` 用 `major.minor.patch` URL，`schemaVersion` 对应主版本。
- 主版本更高 → 拒读、只读、不覆盖（用户降级不丢配置）；更低 → 迁移器，迁移前备份。
- 次版本更高 → 可读、只读（保留未知字段），不写回；补丁差异 → 可读写，保留原 `$schema` 与未知字段。迁移是纯函数 + 快照测试，失败则保持禁用并提示文件路径。

### 3.6 环境变量 / 命令行 / CI

| 通道 | 允许 | 约束 |
|---|---|---|
| `ARCHOERACX_DEV_DOWNLOAD=1` | 仅 Debug | `#if DEBUG`；Release 二进制中不存在该符号（CI 断言） |
| `--allow-download` | **不提供** | 任何公开 CLI 都会被写成「一键解锁教程」 |
| 编译期 `ARCHOERACX_CI_ALLOW_DOWNLOAD` | 仅 `ci` flavor | 产物不入发布渠道，发布流水线断言关闭 |
| 测试注入 `FakeGate` | 是 | 单测默认假实现，不打真实网络 |

**必须承认**：本地运行期开关永远可被调试器/补丁绕过。上表防的是「误触」和「脚本顺手打开」，不是防逆向。

### 3.7 执行点（真正的边界）

```
Dart ──compliance.state / download.plan──▶ C++ ──▶ C# ComplianceGate
                                                    │ 通过后签发 capability
                                                    ▼
                                      Rust archoera_dl_submit(plan, capability)
```

- 唯一计划生产者在 C#（`docs/02` §3）；`download.plan` 生成前求值，失败返回 `DOWNLOAD_DISABLED` + `reason`。
- C# HTTP 层按**用途**（`metadata`/`content`/`telemetry`）放行，默认 `content`——不按 URL 白名单，避免镜像换域名即失效。
- capability = `base64url(payload || HMAC-SHA256(进程随机 key, payload))`，payload 含 `planId|level|exp|nonce`；Rust 验签后才落盘。Dart 绕过 C# 直接调 Rust 会失败。
- 撤回：`gateEpoch++`，拒绝新 capability，取消在途任务（保留 `.part`，见 `05-download-engine.md`）。

### 3.8 远程 kill switch

| 维度 | 做法 |
|---|---|
| 能力 | 只能调低 `maxLevel`，**永不能调高**，不能下发 URL/代码/正则 |
| 载体/校验 | 独立签名 JSON（Ed25519），公钥编译进二进制；签名失败、字段越界、过期 → 整份丢弃并记审计 |
| 缓存/离线 | 缓存至 `expiresAt + 30d`，到期回到本地策略；不因「拿不到」阻断已授权功能 |
| 透明/隐私 | 「关于」页展示最近策略与签发时间、官网公开历史；拉取不带设备/账号/安装 ID |

**利**：出现投诉可快速止血，不必发版。**弊**：服务器失陷可远程关功能、用户怀疑后门、离线语义复杂。**结论 [建议]**：做，但严格「只能收紧 + 签名 + 可审计 + 可离线」，且**不把它当法律依据**——依据是本地默认分级（cn L0 / global L1）与审计。

---

## 4. 免责声明与知情同意

### 4.1 三步流程（不可跳步）

1. **阅读页**：全屏路由，不可 Esc/点遮罩关闭；正文可滚动，顶部固定显示 `声明版本 / 生效日期 / SHA-256 前 12 位 / 渠道 / 应用版本`。
2. **确认页**：滚到底后「我已阅读」复选框才可用（默认未勾选）；勾选后出现短语输入框。
3. **二次确认**：按钮明写「启用下载（将从互联网获取游戏内容）」；点击写 `state/consent.json` + 审计。拒绝路径「保持禁用」与主按钮**同等视觉权重**，任意步骤可用，不反复弹窗。

### 4.2 反暗模式清单

- 复选框默认不勾选；不得预填短语；不得默认同意；默认焦点不得落在「启用」上（Enter 不应直接同意）。
- 拒绝按钮不得隐藏、缩小、置灰或需滚动才可见。
- 不得倒计时、抖动、羞耻文案、反复弹窗。
- 必须可撤回：设置页「关闭开发者模式」一键完成，不必改文件。

### 4.3 记录与审计

```json
{ "schemaVersion": 1, "noticeId": "compliance.download",
  "noticeVersion": "2026-10-01", "noticeSha256": "9f2c…",
  "acceptedAt": "2026-10-20T12:34:56Z", "method": "typed_phrase",
  "phraseId": "zh-Hans#1", "locale": "zh-Hans", "channel": "stable",
  "appVersion": "0.1.0+build.123", "configSha256": "3a71…", "revokedAt": null }
```

- `noticeSha256` 运行时由**打包进二进制的声明资源**计算，不读磁盘、不信配置。
- 主稿 `zh-Hans` 算哈希；翻译变更不改主哈希但记 `locale`；实质差异必须升 `noticeVersion`。
- 审计 JSONL：`seq`、`ts`(UTC)、`event`、`noticeVersion`、`noticeSha256`、`channel`、`appVersion`、`configSha256`、`prevHash`、`hash`。哈希链只能**发现删改**，不能防伪造；不要宣称不可抵赖。
- 保留 12 个月按月轮转；「导出审计」附链头哈希。

### 4.4 声明更新与撤回

- `noticeVersion` 变化 → `CONSENT_STALE`，下次进入下载入口（或启动且开发者模式仍开）要求重读，**不自动接受**。
- 应用降级导致声明回退 → 哈希不匹配 → 同样重确认。
- 撤回：写 `revokedAt`、`gateEpoch++`；已下载文件不删（属用户数据），在途任务取消并提示。
- 撤回后再启用 = 完整三步重来，不能「撤销撤回」。

---

## 5. 状态机与错误处理

### 5.1 状态机

```
official-global:
  ENABLED(official) ──请求 L2──▶ CONSENT_REQUIRED ──三步完成──▶ ENABLED(all)
        ▲                                                        │
        └──────────── 撤回 / 文件降权 / kill switch ◀────────────┘

official-cn:
  LOCKED(reason) ──developerMode=true + ACK──▶ CONSENT_REQUIRED
        ▲                                            │ 三步完成
        │ 撤回 / 文件降权 / kill switch              ▼
        └────────────────────────────────── ENABLED(official)
```

所有迁移记审计；`ENABLED` 每次生成计划都重新求值，不缓存「已授权」布尔值。`official-cn` 不提供 L2。

### 5.2 未开启时点「下载」

> 以下以 `official-cn` 为例；`official-global` 的 L2 入口相同，但不需要 `developerMode`/ACK。

- 按钮**保持可点**（不要 `enabled:false` 后零解释），带锁图标与 tooltip。
- 点击弹底部说明：为什么禁用、本地功能不受影响、`了解原因`（文档）、`高级：手动启用`（打开配置目录 + 复制路径，**无「一键开启」**）。
- 若 `developerMode=true` 但未确认，直接进入声明流程；配置无效则显示 JSON Pointer（如 `/downloadLevel`）与「打开日志」。

### 5.3 错误码（C# → C ABI）

| 码 | 场景 | UI |
|---|---|---|
| `DOWNLOAD_DISABLED` | 门控未通过 | 说明面板 |
| `CONSENT_REQUIRED` / `CONSENT_STALE` | 缺同意记录 / 声明已更新 | 进入声明流程 / 重读提示 |
| `CONFIG_INVALID` | 解析/类型错误 | pointer + 路径，不改文件 |
| `SCHEMA_UNSUPPORTED` | 主版本更高 | 提示升级应用 |
| `LEVEL_NOT_ALLOWED` | 请求等级超过通道上限（如 cn 请求 L2） | 说明通道限制 |
| `POLICY_RESTRICTED` | 机器/远程策略更低 | 显示原因码 |
| `GATE_REVOKED` | 已撤回 | 说明 + 重开入口 |
| `CAPABILITY_INVALID` | Rust 验签失败 | 审计 + 内部错误上报 |

### 5.4 离线 / CI / 自动化

- `L0` / `L0-Z` 与离线是常态：启动、启动实例、看日志全部可用，不因判定失败崩溃。
- 单测注入 `FakeGate`（`enabled/level/reason` 可编排），不碰真实文件与网络；集成测试用 `ARCHOERACX_CONFIG_DIR=<tmp>`，门控照常生效——测试环境不是旁路理由。
- 提供 `archoera-core selftest --gate`：跑完 §7 判定矩阵，输出 JSON + 退出码。
- `L0-Z` 零出站用 network namespace/防火墙断言，不信日志。

### 5.5 多语言与无障碍

- 声明与提示走 ARB，不拼句子；日期用 locale，审计用 UTC ISO-8601。缺翻译 → `zh-Hans` + `en` 双语并列，不机器翻译法律文本。
- 伪语言 `en_XA` 下按钮不截断；德语长词、阿语 RTL、泰语断行单独验收。
- `phraseId` 随语言固定（`zh-Hans#1` =「我已知晓」）；比较前 NFKC + trim + casefold，规避 IME 全半角。版本号/哈希在 RTL 下用 LTR 隔离。
- 焦点陷阱在对话框内；读屏播报标题、滚动进度、勾选状态；键盘可完成全流程，`End` 可到底；读屏等价路径「朗读全文」完成 = 滚到底，审计记 `method: a11y_confirm`。
- 200% 字号、4.5:1 对比度、减少动效；锁图标不能是唯一信息载体。

---

## 6. 安全与滥用

> **AGPL-3.0-only 的影响**：代码公开后，任何人 fork 都能删掉整个 gate；fork 必须继续以 AGPL 公开源码，但**功能可以被移除**。因此下表的"应对"只能按「提高误触成本 + 留痕」理解；硬约束只能靠发行通道与官方构建（见 `../09-distribution-and-download-policy.md`）。
> capability/HMAC 也只是"防止 Dart 绕过 C# 策略"的进程内边界，不是防逆向。

| 风险 | 应对 | 残余风险 |
|---|---|---|
| 把本地开关当法律屏障 | 文档与 UI 明说「意图记录 + 摩擦，不是技术保护措施」 | 无法消除；不要对外宣称「已合规」 |
| 一键破解包 | 无公开 CLI；应用不写 `true`；需手改文件 + 三步确认 + 哈希 | 补丁二进制仍可绕过 |
| 脚本静默跳过声明 | 哈希来自打包资源；consent 与 notice 比对；capability 由 C# 签发 | 调试器可改内存 |
| 绕过 UI 直连下载器 | Rust 验 capability；C# 是唯一计划生产者 | 同进程调试可提取 key |
| 审计被删改 | 哈希链 + 序号 + 链头导出 | 只能发现，不能阻止 |
| 日志泄露隐私 | 统一脱敏（Bearer/token/邮箱/UUID/路径/IP）；审计只存哈希 | 低熵内容可被字典攻击 |
| 远程策略被滥用 | 只降不升、签名、字段白名单、公开历史 | 服务器失陷可造成拒绝服务 |

- **不要把声明做成空壳**：正文必须与哈希源一致，禁止运行时从网络拉正文（否则服务端可换文本而记录不变）。
- **配置哈希用于发现「悄悄改配置」**：与 `state` 里 last-known-good 比对，变化记审计（不改行为）。
- 「读到底」不能只信滚动位置：同时要求至少一次真实滚动事件，防预滚动/程序化 `jumpTo`。

---

## 7. 测试用例

| # | 用例 | 期望 |
|---|---|---|
| 1 | `official-cn` 全新安装，无 `compliance.json` | `LEVEL_DISABLED`；点下载出说明；L0 只允许只读元数据，`L0-Z` 抓包零出站 |
| 2 | `official-cn` 只写 `level:"official"`，`developerMode` 缺省 | `NO_DEVELOPER`；不出现声明 |
| 3 | `official-global` 全新安装，无 `compliance.json` | `ENABLED(official)`；可下载官方内容 |
| 4 | `official-global` 请求 `level:"all"`、无 consent | `CONSENT_REQUIRED`；三步可走完 |
| 5 | 声明页未滚到底 / 滚到底未勾选 | 复选框/短语/确认依次禁用；强制点击无效 |
| 6 | 勾选 + 错误短语 | 行内错误；不写 consent；不记「成功」 |
| 7 | `official-cn` 全流程完成 | `ENABLED(official)`；consent 完整；审计链有效 |
| 8 | `"developerMode": "true"`（字符串）或枚举拼错 | `CONFIG_INVALID`；保持通道默认；**不覆盖用户文件** |
| 9 | 运行中手改 `all→disabled` / `disabled→all` | 降权 ≤300ms 生效并取消在途任务；升权不直接生效，仅出现「继续启用」 |
| 10 | `schemaVersion: 2`（应用只支持 1） | `SCHEMA_UNSUPPORTED`；只读；文件字节不变 |
| 11 | 保存瞬间断电/半写 | `CONFIG_INVALID`；保留 `.bak`；恢复后自动重求值 |
| 12 | 应用内撤回 | `LOCKED(REVOKED)`；`gateEpoch++`；不删已下载文件 |
| 13 | 升级后 `noticeVersion` 变化 / 应用降级 | `CONSENT_STALE`；必须重读 |
| 14 | 远程策略 `maxLevel=disabled`（签名有效/无效） | 有效则覆盖本地 `all` 并记审计；无效整份丢弃 |
| 15 | `L0-Z` 下触发图片、更新检查 | 本地占位/跳过；零出站 |
| 16 | `official-cn` `developerMode:true` + ACK + `level:"all"` | `LEVEL_NOT_ALLOWED`（cn 上限 official） |
| 15 | Dart 直接调 `archoera_dl_submit`（无 capability） | `CAPABILITY_INVALID`；不创建文件 |
| 16 | CI flavor 编译 / Release 下设 `ARCHOERACX_DEV_DOWNLOAD=1` | CI 可下载；Release 忽略环境变量且符号检查确认旁路不存在 |
| 17 | `de`/`ar`/`ja`/伪语言 1.5× 字号 | 无截断重叠；RTL 正确；滚动焦点正常 |
| 18 | 读屏 + 纯键盘走完流程 | 可达；记录 `a11y_confirm` |
| 19 | 手工删除审计中间行 | 导出报链断裂并给 `seq`；门控不受影响 |

---

## 8. 合规表述（非法律意见）

**本文只讨论产品机制，不构成法律意见，也不对任何具体行为的合法性下结论。** 上线前请咨询执业律师，并按实际发行地区、渠道与商业模式复核。

可能需要自行核实的方向（**均需自行核实**，不代表已适用）：**网络出版 / 游戏版号**（启动器是否被认定为网络出版服务或游戏运营的一环、工具与下载入口的界限）；**未成年人保护**（实名/时段/消费限制是否适用于工具类软件、`official-cn` 默认禁网能否作为缓解）；**著作权**（游戏本体与资源的分发边界、Mod/整合包/资源包授权链、镜像站再分发许可）；**个人信息保护**（IP 地理判定、日志/崩溃上传、审计日志的告知与最小化、跨境传输）；**消费者权益 / 商店规则**（`official-cn` 默认关闭是否影响「商品完整性」描述、商店对「需手改配置」的接受度）。

工程建议：

1. 用户协议、隐私政策、下载声明三份文本互相引用且版本独立，任一份变更可追溯。
2. 对外文案避免「根据相关法律法规，本软件……」这类笼统断言——HMCL 的文案是其自身选择，不应照抄，也不应替律师下结论；同时保留「默认分级」的证据链（默认配置、构建产物、发布说明、审计格式），并把「开启后能做什么」（L1/L2 差异）写清楚。

---

## 9. 参考与来源

| 内容 | 位置 |
|---|---|
| HMCL 地区判定 | `HMCL/src/main/java/org/jackhuang/hmcl/util/RegionUtils.java:42-124` |
| HMCL UI 禁用（两处）与 Tab 结构 | `HMCL/.../ui/download/VersionsPage.java:456-469` · `.../ui/instances/DownloadListPage.java:631-645` · `.../ui/download/DownloadPage.java:84-115` |
| HMCL 用户协议弹窗 | `HMCL/.../ui/Controllers.java:322-340`；`setting/UserState.java:116-117` |
| HMCL schema 兼容 | `HMCLCore/.../util/gson/JsonSchema.java:44-56`；`setting/SettingsManager.java:1172-1206` |
| HMCL 离线账户限制 | `HMCL/.../ui/account/AccountListPage.java:56-70` |
| HMCL PR #7017 / #7018 / #7020 | https://github.com/HMCL-dev/HMCL/pull/7017 · /7018 · /7020 |
| 本仓库数据目录 | `docs/10-architecture-design.md` §11 |
| 下载器契约 | `docs/08-technical-research/05-download-engine.md` §1 |
| JSON Schema 2020-12 | https://json-schema.org/draft/2020-12/schema |

> 许可证边界：HMCL 为 GPL-3.0。本文仅总结行为与取舍，未复制代码、注释、i18n 文案或 UI 资源；实现须独立编写。
