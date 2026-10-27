# 09 · 开源、分发与下载策略

> 前提：项目采用 **AGPL-3.0-only**（强 copyleft + 网络条款），许可证全文见仓库根目录 `LICENSE`；不接受外部代码贡献。
> 当前只有**一个发行版**：下载能力在编译期保留，运行期按网络地区与本地风险确认决定是否启用。
> 本文描述工程事实与发行约束，不构成法律意见；发行地区、平台条款与版权问题应由执业律师按实际情况复核。

---

## 0. AGPL-3.0-only 的含义

| 事实 | 含义 |
|---|---|
| OSI / SPDX `AGPL-3.0-only` | 公认的开源许可证，强 copyleft |
| 分发二进制 | 必须提供完整对应源码（§6） |
| 网络服务（§13） | 修改版让用户远程交互时，须向这些用户提供源码 |
| 整体作品 | 链接/组合的代码通常也要 AGPL；插件/独立进程边界要谨慎 |
| 专利 | 明确授权（§11） |
| 商标 | **不授权**；名字/Logo 仍归项目方（需自行注册/主张） |
| 闭源 fork | 不允许；fork 必须继续 AGPL |
| 撤回 | 已发布版本的授权不可撤回 |
| 外部贡献 | 当前**不接受**；见 `CONTRIBUTING.md` |

AGPL 不要求 fork 保留功能；fork 可以移除下载门控，但必须继续以 AGPL 公开源码。

---

## 1. 下载功能的定位：默认值，不是 DRM

下载门控的作用是：

1. **对普通用户的默认保护**（不误触、不静默出网）；
2. **知情同意的载体**（用户显式接受风险后留下本地记录）；
3. **官方构建的合规姿态**（中国大陆网络默认关闭内容下载）。

它挡不住 fork、改配置、重编译。**不要把它宣传成“技术强制”，也不要把责任推给开关。**

真正的控制点只有四个：

| 控制点 | 能做什么 |
|---|---|
| 官方二进制 | 项目控制的构建默认遵守地区策略 |
| 代码签名 / 校验和 | 发行前用于防篡改；当前 CI 尚未产出平台签名 |
| 商标 / 名称 | 阻止 fork 冒充官方（AGPL 不授权商标） |
| 官方下载页 / 更新通道 | 只分发项目认可的构建 |

---

## 2. 架构：Provider + Policy + Plan

```
┌──────────────────────────────────────────────┐
│ C# Core                                      │
│  ├─ IDownloadProvider（每源一个实现）         │
│  ├─ DownloadPolicy（地区 + 本地风险确认）      │
│  └─ PlanBuilder → DownloadPlan（不含密钥）    │
└───────────────────┬──────────────────────────┘
                    │ 校验后的 Plan
┌───────────────────▼──────────────────────────┐
│ Rust archoera_dl                             │
│  只执行：URL + hash + dest + 并发/校验/落盘    │
└──────────────────────────────────────────────┘
```

### 2.1 核心仓库里“有什么 / 没有什么”

| 有 | 没有 |
|---|---|
| Provider 接口、策略引擎、Plan 结构 | 任何游戏文件、assets、libraries |
| Mojang / Fabric / Quilt / Modrinth 官方接口实现 | 自建游戏文件镜像 |
| Forge/NeoForge installer 下载与 processors 调度 | 任何 API key / 凭据 |
| Java runtime 下载（Mojang；Adoptium 可选，尚未接入） | 打包的 Oracle JDK |
| 校验、断点、原子落盘（Rust） | 遥测端点、用户追踪 |

> 仓库与构建产物不含任何凭据；CurseForge 在 API 访问配置前不可用。

### 2.2 Provider 清单与默认约束

| Provider | 需要 key | 默认 | 约束 |
|---|---|---|---|
| Mojang（版本/资源/库/Java） | 否 | 允许 | 只连官方域名；不自建镜像；遵守 EULA |
| Fabric / Quilt Meta | 否 | 允许 | 设置可识别 UA；遵守速率 |
| Forge / NeoForge Maven | 否 | 允许 | 只取 installer/库；processors 需本地 Java |
| Modrinth | 否 | 允许（中国大陆需本地确认） | UA、速率、CDN 直链、hash 校验 |
| CurseForge | 是 | API 访问配置前不可用 | 在线与 zip 导入都需要 API key；不发起请求 |
| Java Runtime | 否 | 允许 | Mojang runtime 已接入；Adoptium 为可选源（尚未接入）；不打包 Oracle |
| authlib-injector | 否 | 未启用 | 需完成服务条款、隐私与实名复核后再接入 |
| 启动器更新检查 | 否 | 允许 | 只查询 GitHub Release，不自动下载或执行 |

### 2.3 当前策略求值（已实现）

```text
ARCHOERACX_REGION（开发覆盖）
  → Cloudflare trace 的两位国家码
  → 系统时区 / 区域 / Windows GeoID
  = region（CN / GLOBAL）

region == CN  → 默认禁用下载
                  └─ 本地 compliance.json 通过 → 启用
region != CN  → 默认启用
```

- 地区判定只读取粗粒度国家码，不保存 IP；请求失败回退系统时区/区域/Windows GeoID。
- `download.policy` 返回当前状态；资源、版本、加载器、整合包等 C# 方法在入口统一检查，未启用返回 `DOWNLOAD_DISABLED`。
- 中国大陆网络默认隐藏 Download 导航与首页下载快捷入口；直接访问 `/download` 仍显示配置说明。
- 登录、实例管理、本地启动与本地 Mod 管理不受下载门控影响。
- 该判定不是 VPN/代理安全边界，也不能替代账号服务端的真实校验。

### 2.4 本地风险确认（中国大陆网络）

当前实现只使用**一个用户手写的文件**：`<data>/compliance.json`。应用**永不创建、永不修改**该文件，也不会把它当作“合规认证”。

| 字段 | 要求 |
|---|---|
| `schemaVersion` | 必须为 `1` |
| `developerMode` | 必须为 JSON `true` |
| `acceptRisk` | 必须为 JSON `true`，表示用户已知悉并接受下载/使用第三方内容的风险 |
| `downloadLevel` | 当前只接受 `"L2"` |
| `acknowledgement` | 必须精确为 `ENABLE-DOWNLOADS 1` |

- 字段缺失或类型不符时返回 `CONSENT_REQUIRED` / `CONSENT_INVALID`；应用内 `/download` 页会列出缺失字段。
- 修改后重启应用，或在 `/download` 页点击“重新检查配置”。
- 非中国大陆网络不需要该文件。
- 当前**没有**独立的 `consent.json`、审计链或双文件 `DEVELOPER_ACK.txt`；如后续加入，它们只增加摩擦与留痕，不改变“不是 DRM”的性质。

> 这是运行期地区判断和风险门槛，不是法律合规认证；VPN/代理、IP 数据库误差都可能影响结果。
> 解锁后用户仍需自行承担第三方内容、平台条款和版权风险。

### 2.5 构建与发布渠道

- **单一发行版**：不存在 `official-cn` / `official-global` 两套二进制；`app/tool/build-cn.sh` 仅为旧命令兼容保留，产物与普通构建相同。
- 唯一发布渠道：GitHub Releases；不提交应用商店，不做第三方二进制分发。
- 发布由 `.github/workflows/build-all.yml` 在 Linux/macOS/Windows 三个 runner 原生构建；推送 `v*` tag 后才汇总三端产物发版。
- Microsoft Entra client ID 仅通过 GitHub Actions secret 在构建时注入 NativeAOT 模块；本地维护者构建可使用环境变量，但不提交到仓库。
- CurseForge 在 API 访问配置前不可用：在线接口与其整合包 zip 导入都依赖 API key。`resource.*`/`modpack.install` 对 `provider=curseforge` 在发起 HTTP 前返回 `PROVIDER_UNAVAILABLE`，不发起任何 CF 请求。
- Release 附件应包含源码归档（tag）、SHA-256、`THIRD-PARTY-NOTICES.md`、字体/图标许可正文和 AGPL 源码获取方式。
- Windows 产物形态：便携 `.zip`（解压即用，`data/` 在 exe 旁）与 Inno Setup 安装器两种。不提供单文件 exe——Flutter Windows runner 必须旁加载 `flutter_windows.dll`、引擎数据、AOT 快照与原生模块侧车文件，单文件只能靠自解压引导器实现，暂不引入；便携 zip 即本项目的 Windows 便携形态。
- 不发布带有未审核 Microsoft client ID 的公共构建；若审核未完成，发布说明必须明确在线登录/在线启动的不可用状态。

### 2.6 当前 GitHub-only 开发版声明

当前不通过中国大陆应用商店或商业渠道分发，只在 GitHub 发布开发版。发布页与 README 必须同时保留以下声明；应用内不占用常驻警告条，只在「关于」页保留开发版标记：

- 开发版，功能不完整，可能损坏数据；
- 请勿用于正式环境或重要实例；
- 非官方 Minecraft 产品，不提供游戏本体；
- 不承诺稳定性、兼容性或数据安全；
- 不接受外部代码贡献。

---

## 3. 能力与默认行为

下表描述**同一发行版**在不同地区与配置下的默认行为，不代表不同的二进制：

| 能力 | 中国大陆（未配置 `compliance.json`） | 非中国大陆 | 本地启动 |
|---|---|---|---|
| 版本清单 / 搜索 / 详情（只读元数据） | ✓ | ✓ | ✓ |
| 原版游戏文件 | ✗ | ✓ | — |
| 加载器 / processors | ✗ | ✓ | — |
| Mod / 整合包 / 资源包 / 光影 | ✗ | ✓ | — |
| Java 运行时 | ✗ | ✓ | — |
| 启动器更新检查 | 仅提示，不自动下载 | 仅提示，不自动下载 | — |
| 遥测 / 日志上传 | ✗ | ✗ | — |
| 登录 / 实例管理 / 本地启动 / 本地 Mod | ✓ | ✓ | ✓ |

---

## 4. 密钥、镜像与供应链

### 4.1 密钥

- CurseForge 在 API 访问配置前不可用；接入实现必须持有 API key，仓库与发布物不保存该凭据。
- Microsoft client ID 只在构建期注入；账户 token 的存储见 `authentication.md` 与凭据保险库设计。

### 4.2 镜像

- 只允许白名单镜像，且必须有官方 hash 校验。
- 不自建 Minecraft 游戏文件镜像（版权 + 带宽 + 责任）。
- BMCLAPI 等社区镜像：遵守其说明、标注来源、失败回退官方源。
- 镜像不得改写内容；hash 不匹配直接丢弃并告警。

### 4.3 供应链

- 下载内容只落到实例目录，**不自动执行**除游戏/加载器/Java 之外的二进制。
- 加载器 installer/processors 视为受信任但高风险：来源限定官方 Maven，校验 hash。
- 更新检查只读 GitHub Release 元数据；不自动下载或执行。
- 发布时应提供 SBOM 与第三方许可清单；当前 CI 尚未自动生成。

---

## 5. 分发物清单

| 项 | 要求 |
|---|---|
| 安装包 | 发布要求：代码签名（Windows Authenticode / macOS arm64 notarization）与 SHA-256；当前 CI 尚未产出平台签名 |
| 版本说明 | 明确默认地区策略与下载门控状态 |
| 许可 | 根目录 `LICENSE`（AGPL-3.0 全文）+ `THIRD-PARTY-NOTICES.md`；MIT/Apache 代码仍保留原许可 |
| 免责声明 | “非官方 Minecraft 产品”；不提供游戏本体；不保证可用性 |
| 隐私 | 单独政策；不默认上传日志或遥测 |
| 商标 | 名称/Logo 使用政策；fork 必须改名（AGPL 不授权商标） |
| 支持 | 只支持官方构建；fork 问题不受理 |

**关于第三方代码**：AGPL-3.0 项目可以包含 MIT/BSD/Apache-2.0 代码，但那些文件仍受原许可约束（保留版权与 NOTICE）。GPLv3 代码可按 GPLv3 §13 与 AGPLv3 组合，但相关部分仍受 GPL 约束；本项目不并入。分发 `authlib-injector` 时需提供对应源码或有效的书面要约，不能只给上游链接。

---

## 6. Fork 与“别人拿去乱来”

AGPL 允许 fork 关闭门控、甚至捆绑恶意软件；fork 必须继续以 AGPL 公开源码，但**功能可以被移除**。能做的：

1. **商标**：注册名称/Logo；fork 必须改名（在 README/关于页写明）。
2. **签名**：发行前为官方构建提供平台签名；当前尚未落地，用户可先核对 SHA-256。
3. **官方渠道**：只从 GitHub Release 下载；公布校验和。
4. **声明**：README 写明“fork 非官方，不代表本项目立场，不提供支持”。
5. **不要**在代码里放“只有官方能开”的假安全机制（密钥会泄漏，反而误导用户）。

> 法律上，fork 的行为由 fork 作者负责；如果把名字借给它，风险会回到项目方。商标与签名是最低成本的隔离。

---

### 6.5 许可证变更边界

已发布的 AGPL 版本不可撤回；接收者可以继续使用和 fork。未来版本若包含第三方 AGPL/GPL 代码，仍须遵守其许可。

---

## 7. 发行模型

- 单一发行版，不维护 `official-cn` / `official-global` 两套二进制。
- 中国大陆网络默认关闭内容下载，由本地 `compliance.json` 显式启用；非中国大陆默认启用。
- 地区判定只读取粗粒度国家码，不保存 IP；请求失败回退系统时区/区域/Windows GeoID。
- 门控只用于降低误触与默认风险，不宣称“合规认证”或“防破解”。
- 发行控制点在官方构建、校验和、商标与发布渠道，不在源码里的开关。

---

## 8. 上线前检查表

- [ ] 确认使用 AGPL-3.0-only，并接受“分发必须开源、功能仍可被 fork 移除”
- [ ] `LICENSE` = AGPL-3.0-only 全文；`THIRD-PARTY-NOTICES.md` 覆盖 MIT/Apache 依赖
- [ ] 核心仓库不含游戏文件、镜像、API key、遥测端点
- [ ] Provider 接口 + 策略引擎 + Plan 契约落地
- [ ] 单一发行版的地区默认行为与 `download.policy` 一致
- [ ] `compliance.json` 只读、应用不自动创建或修改
- [ ] 官方构建签名 + 校验和 + GitHub Release 说明
- [ ] 商标/名称政策 + fork 声明
- [ ] 发布前律师复核（发行地区、未成年人、隐私、平台条款）

---

## 9. 摘要

- 下载能力由 C# 的 Provider/Policy 与 Rust 的 `DownloadPlan` 执行器组成。
- 中国大陆默认关闭内容下载，非中国大陆默认启用；本地 `compliance.json` 只做风险确认。
- 发行控制点是官方构建、校验和、商标与发布渠道。
