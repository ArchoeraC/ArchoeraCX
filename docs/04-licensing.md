# 04 · 许可证与合规

> 项目许可证：**AGPL-3.0-only**，全文见仓库根目录 [`LICENSE`](../LICENSE)。
> 本文说明分发义务、第三方依赖边界与资产使用规则，不构成法律意见；具体义务以 `LICENSE` 与各依赖的实际许可证为准。

---

## 1. 项目许可证

| 项 | 说明 |
|---|---|
| SPDX | `AGPL-3.0-only`（不是 `-or-later`） |
| 使用 | 允许运行、研究、修改、再分发 |
| 分发二进制 | 必须向接收者提供**完整对应源码**（AGPL §6）和许可证声明 |
| 网络服务 | 修改版若让用户远程交互，须向这些用户提供对应源码（AGPL §13） |
| 担保 | 不提供任何担保 |
| 商标 | 许可证不授予名称/Logo 使用权；fork 必须改名 |
| iOS / Mac App Store | 与 Apple 的 DRM/附加限制条款冲突，本项目不作为分发渠道 |

- 已发布版本的 AGPL 授权**不可撤回**；接收者可以继续使用和 fork。
- 当前不接受外部代码贡献；贡献政策见 [`CONTRIBUTING.md`](../CONTRIBUTING.md)。

---

## 2. 第三方依赖

### 2.1 允许进入构建

| 许可证 | 义务 |
|---|---|
| MIT / BSD-2-Clause / BSD-3-Clause / ISC / Zlib | 保留版权声明与许可证全文 |
| Apache-2.0 | 保留版权声明、许可证全文；上游带 `NOTICE` 时一并保留 |
| Unlicense / 0BSD / CC0 | 通常无署名义务；没有明确专利授权，使用时自行评估 |
| OFL-1.1（字体） | 保留字体名称与许可证；不单独售卖字体 |

### 2.2 需要审批或有条件

| 许可证 | 结论 |
|---|---|
| MPL-2.0 | 文件级 copyleft：修改过的 MPL 文件必须继续以 MPL 提供；可按 §3.3 作为 Larger Work 分发。只有文件带 Exhibit B（Secondary License）声明时，才能把 MPL 文件本身按 GPL/AGPL 授权 |
| LGPL-2.1 / LGPL-3.0 | 动态链接或独立进程较简单；静态链接需满足可重链接等条件。多语言静态打包容易踩坑，默认避免 |

### 2.3 禁止进入构建

| 许可证 | 原因 |
|---|---|
| GPL-2.0 | 与 AGPLv3 不兼容 |
| GPL-3.0 | AGPLv3 可按 GPLv3 §13 与 GPLv3 组合，但 GPL 部分仍受 GPL 约束；本项目不并入 |
| AGPL-3.0（第三方） | 不能并入本项目代码；独立进程/独立 jar 需单独评估并履行 AGPL 义务 |
| SSPL / BUSL / Commons Clause / 任何“仅非商业”许可 | 不是 OSI 开源，或限制商业使用 |
| 无 LICENSE 的仓库 | 默认保留所有权利，等同闭源，不能复制 |

### 2.4 代码来源规则

- 不复制、改写、翻译或换语言重写 GPL/AGPL 代码；翻译作品仍属衍生作品。
- 不把 GPL 代码封装成 NuGet/私有包再引用；链接/静态组合仍可能构成衍生。
- 阅读、运行、观察行为和独立实现是允许的；不要复制具体表达、目录结构或命名体系。
- 新增依赖必须评估必要性、许可证与已知漏洞，并登记到 [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md)。

---

## 3. 资产与品牌

| 类型 | 规则 |
|---|---|
| 项目图标、默认皮肤、自有插画 | 原创资产，随项目以 AGPL-3.0-only 分发 |
| Mojang / Microsoft 素材 | 不使用 Creeper 脸、草方块、官方 Logo 等；不暗示官方关联 |
| 第三方启动器资产 | 不复制其代码、布局文件、图标、插画、文案、字体或像素级设计 |
| 字体 | Noto Sans SC（OFL-1.1）、Tabler Icons（MIT）已登记；商业字体必须单独授权 |
| 加载器图标 | 来源与许可证见 `app/assets/icons/loaders/SOURCES.md`；商标归各自项目所有，不表示背书 |
| Minecraft 名称/Logo | 仅作事实性描述；必须保留非官方声明 |

必须展示的免责声明：

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.

---

## 4. 游戏文件、账户与第三方 API

### 4.1 游戏文件

- 不分发游戏本体、assets、libraries 或 natives；只从官方或白名单源下载。
- 下载内容必须校验官方 hash；镜像可在中国大陆自动模式下优先，但不得改写内容。
- 不把第三方整合包中的游戏文件重新打包分发。

### 4.2 账户

- 使用项目自己的 Microsoft Entra 应用；公共客户端，不内嵌 client secret。
- 支持 Authorization Code + PKCE（loopback），Device Code 作为 fallback。
- 不提供绕过正版验证、账号租售、共享或代练功能；离线账户仅用于本地/单人场景，发布版需先通过正版账户验证。
- 账户 token 只保存在本地加密凭据库，不写日志、不跨模块 ABI。

### 4.3 第三方认证

- `authlib-injector` 为 **AGPL-3.0**，通过独立 jar / `-javaagent` 使用。
- 分发其 jar 时，必须提供对应源码或有效的书面要约；仅给上游链接不一定是充分的合规方式。
- 当前未接入第三方 authlib 登录。

### 4.4 第三方 API 条款

| 服务 | 要点 |
|---|---|
| Mojang launchermeta / resources | 事实标准接口；缓存与限速；不把官方文件镜像到自有 CDN 再分发 |
| Microsoft / Xbox Live | 遵守 Microsoft 服务协议；token 安全存储；公共客户端不得内嵌 secret |
| Modrinth | 设置可识别 `User-Agent`；遵守速率限制；`.mrpack` 规范开放 |
| CurseForge | 需要 API key；未配置前不发起请求；遵守作者分发限制 |
| OptiFine | 无公开 API，站点条款限制自动化下载，谨慎处理 |
| Java 运行时 | Mojang runtime 已接入；Adoptium 为可选源（尚未接入）；不打包 Oracle JDK |
| BMCLAPI 等镜像 | 遵守镜像方说明；中国大陆自动模式下镜像优先、官方 fallback；始终校验官方 hash |

---

## 5. 仓库与发布物

### 5.1 仓库基线

```
LICENSE                 # AGPL-3.0 全文
THIRD-PARTY-NOTICES.md  # 第三方组件、许可证与来源
PRIVACY.md              # 数据处理说明
CONTRIBUTING.md         # 贡献政策（当前不接受外部代码）
SECURITY.md             # 漏洞报告渠道与范围
```

- 建议为新源文件添加 SPDX 头：`SPDX-License-Identifier: AGPL-3.0-only`。
- 资产来源写入对应 `SOURCES.md`；许可证正文放在 `app/assets/licenses/`。
- 依赖变更时同步更新 `THIRD-PARTY-NOTICES.md`。

### 5.2 发布前检查

| 检查 | 方式 |
|---|---|
| 依赖许可证白名单 | `dotnet list package --include-transitive`、`flutter pub deps`、`cargo deny` |
| SBOM | CycloneDX / syft |
| 白名单外依赖 | 目标：构建脚本显式断言；当前未接入 |
| 资产来源 | `SOURCES.md` + `app/assets/licenses/` |

> 当前 CI 只执行构建、测试与打包；许可证白名单与 SBOM 尚未接入自动化，发布前需手工执行或后续接入。

### 5.3 发布物

- 提供源码获取方式（tag / 源码归档），并附 `THIRD-PARTY-NOTICES.md` 与字体/图标许可证正文。
- 不包含游戏本体、assets、libraries、API key、账号凭据或第三方源码。
- 关于页/下载页展示非官方声明与源码获取方式。
- 若分发 `authlib-injector`，附 AGPL 声明并提供对应源码或有效书面要约。

---

## 6. 常见误解

| 误解 | 事实 |
|---|---|
| “GitHub 公开就能随便用” | 公开不等于授权；无 LICENSE 即保留所有权利 |
| “AGPL 只影响服务端” | 桌面程序分发同样触发源码义务；网络条款是额外义务 |
| “改一改/换个语言就不算抄” | 改写、翻译、换语言仍是衍生作品 |
| “只抄思路不算抄” | 思路通常不受版权保护，但具体表达、结构编排和资产受保护 |
| “开源项目没有专利风险” | Apache-2.0 和 GPLv3 有明确专利授权；MIT 没有 |
| “用了 MIT 库就可以不署名” | 必须保留版权与许可证声明 |
| “先把代码抄进来，以后再说” | 一旦发布就构成侵权，无法用后续修改“洗白”历史 |

---

## 7. 问题与报告

- 许可证/合规问题：在仓库 Issue 中提出，避免包含 token、账号数据或未脱敏日志。
- 安全漏洞：按 [`SECURITY.md`](../SECURITY.md) 私下报告。
