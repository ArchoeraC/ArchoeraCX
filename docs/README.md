# 文档索引

ArchoeraCX 的公开文档。实现以代码和契约为准；文档与代码冲突时，先修代码和契约，再同步文档。

## 先读

| 文档 | 内容 |
|---|---|
| [根 README](../README.md) | 项目定位、当前能力、架构红线与快速开始 |
| [构建、运行与测试](12-build-and-test.md) | 环境依赖、逐模块构建、client ID 注入、测试矩阵、数据目录 |
| [认证与 Minecraft 审核](authentication.md) | Microsoft Device Code、XBL/XSTS、Minecraft client_id 白名单 |
| [详细架构](10-architecture-design.md) | ABI、模块协议、任务/事件、存储、流程、安全、测试 |
| [UI 设计系统](11-ui-design-system.md) | 设计令牌、AppShell、页面结构、动效与资产边界 |
| [登录与隐私法律底稿](legal/login-and-privacy.md) | 中国大陆发行假设下的登录、未成年人、隐私与上线决策（非法律意见） |

## 产品与工程

| 文档 | 内容 |
|---|---|
| [路线图](05-roadmap.md) | 阶段目标、当前进度、验收标准 |
| [许可证与资产边界](04-licensing.md) | AGPL-3.0-only、依赖组合、字体/图标/品牌红线 |
| [发行与下载策略](09-distribution-and-download-policy.md) | 发行通道、Provider/Policy/DownloadPlan、密钥与镜像 |
| [生态 API](06-ecosystem-apis.md) | Mojang、Microsoft、加载器、Modrinth/CurseForge、Java |

## 技术预研

见 [docs/08-technical-research](08-technical-research/README.md)。其中已落地或直接影响当前实现的条目：

- Java 扫描
- Forge/NeoForge 生成物与修复
- 下载源与 `DownloadPlan` 边界
- Microsoft OAuth 已迁移到 [authentication.md](authentication.md)

## 归档

已废弃或被取代的设计稿见 [docs/archive](archive/README.md)：仅作溯源与历史对照，冲突时以现行文档为准。

## 代码内文档

- [Flutter / UI](../app/README.md)
- [C++ 核心](../app/core/README.md)
- [C# 业务模块](../app/modules/business/README.md)
- [Rust 下载器](../app/modules/downloader/README.md)
- [ABI / 契约](../app/contracts/README.md)

## 维护规则

- 不提交 token、client secret、账号数据、反编译产物或第三方源码。
- 新方法必须同时登记到 C++ `method_registry` 与 C# `BusinessModuleRegistry`。
- 新增文档先判断是否已有 SSOT；重复摘要优先删除，而不是继续叠加。
- 被取代的设计稿移入 `docs/archive/` 并在[归档索引](archive/README.md)登记原位置、理由与现行替代。
- 本地临时草稿、反编译产物与样本放 `Reference/local/`，不进入公开仓库。
