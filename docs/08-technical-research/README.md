# 技术预研

这些文档是设计输入，不是实现规范。最终架构、ABI 和数据流以
[`../10-architecture-design.md`](../10-architecture-design.md) 为准；
Microsoft / Minecraft 认证已单独整理到 [`../authentication.md`](../authentication.md)。

| 文档 | 主题 |
|---|---|
| [01-version-merge.md](01-version-merge.md) | 版本 JSON 合并 / `inheritsFrom` / 库去重 |
| [02-loader-install.md](02-loader-install.md) | Fabric/Quilt/Forge/NeoForge 安装与 processors |
| [03-mod-search.md](03-mod-search.md) | Modrinth 搜索、兼容性、分页（CurseForge 待同意） |
| [04-crash-analysis.md](04-crash-analysis.md) | 崩溃日志、crash-report、规则引擎 |
| [05-download-engine.md](05-download-engine.md) | 下载并发、Range、原子落盘、去重 |
| [06-modpack-install.md](06-modpack-install.md) | 整合包格式、overrides、Zip 安全 |
| [07-compliance-gate.md](07-compliance-gate.md) | 发行通道、合规门控、同意与审计 |
| [08-java-scan.md](08-java-scan.md) | Java 候选发现、版本探测、手动回退 |
| [09-forge-generated-artifacts.md](09-forge-generated-artifacts.md) | Forge/NeoForge 生成物清单、校验、修复 |
| [10-download-sources.md](10-download-sources.md) | 下载源矩阵、镜像、`DownloadPlan` 边界 |
| [11-loader-install-strategy.md](11-loader-install-strategy.md) | 加载器无 Java 边界与安装策略 |
| [12-download-scheduling-principles.md](12-download-scheduling-principles.md) | FDM/aria2 的分段与调度原理（只读） |
| [13-transport-protocol-fallback.md](13-transport-protocol-fallback.md) | 地址族/TLS/HTTP 版本/代理的多协议回退 |
| [14-security-audit-2026-10.md](14-security-audit-2026-10.md) | 依赖 CVE 与代码安全审查（2026-10） |
| [15-launch-parameter-parity.md](15-launch-parameter-parity.md) | 启动参数与 Prism 的差距（只读） |

## 使用约束

- 预研结论只有在真实样本或官方文档支持时才能进入实现。
- 第三方项目只读；不复制代码、文案、图标、字体。
- 标注「未验证」的内容必须在对应测试或集成验证后删除标记。
- 下载源/发行策略以 [`../09-distribution-and-download-policy.md`](../09-distribution-and-download-policy.md) 为准。
