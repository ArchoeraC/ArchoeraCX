<p align="center">
  <img src="app/assets/branding/archoera_icon_512.png" width="128" height="128" alt="ArchoeraCX">
</p>

# ArchoeraCX

> 开源、多端、面向 Minecraft: Java Edition 的混合架构启动器

> [!WARNING]
> **开发版**：仅通过 GitHub 发布，功能不完整，可能损坏实例或配置数据；请勿用于正式环境或重要实例。

---

ArchoeraCX 是一个开源的**跨平台 Minecraft: Java Edition 启动器**，桌面为主（Linux / Windows / macOS）。UI 用 Flutter，C++ 作为唯一枢纽，业务 / API / SQLite 由 C# NativeAOT 持有，下载交给 Rust；全程进程内 FFI，无 Node 进程、无本地监听端口。

- 实例管理（SQLite 索引 + `instance.json` 权威元数据）与原版安装 / 启动、Java 自动扫描
- 加载器 Fabric / Quilt / Forge / NeoForge 安装与修复；Modrinth 与本地 `.mrpack` 整合包
- Microsoft 登录（Authorization Code + PKCE，loopback）与离线启动
- Rust `DownloadPlan` 执行器：hash 校验、原子落盘、分片续传、进度与取消
- AppShell + 明暗主题、系统主题色、中英文 i18n，安装与下载统一走后台任务


> 代码以 **AGPL-3.0-only** 开源，不提供闭源商业授权；细则见 [docs/04-licensing.md](docs/04-licensing.md)。

## 快速开始

完整依赖、逐模块构建、测试矩阵与数据目录见 **[docs/12-build-and-test.md](docs/12-build-and-test.md)**。

```bash
git clone https://github.com/BetaStudio2/ArchoeraCX.git
cd ArchoeraCX/app

./tool/build-core.sh    # C++ 核心
./tool/build-cs.sh      # C# NativeAOT 模块
./tool/build-dl.sh      # Rust 下载执行器

flutter pub get && flutter gen-l10n
flutter run -d linux    # 或 windows / macos
```

Microsoft 登录需在构建 C# 模块时注入 client ID（`export ARCHOERACX_MICROSOFT_CLIENT_ID=...` 后再跑 `./tool/build-cs.sh`），否则返回 `MICROSOFT_CLIENT_ID_MISSING`。

## 文档

- [文档索引](docs/README.md) —— 全部公开文档入口
- [构建、运行与测试](docs/12-build-and-test.md) —— 环境 / 逐模块构建 / 测试 / 数据目录
- [架构设计](docs/10-architecture-design.md) —— ABI、模块协议、任务与事件、存储、安全
- [Microsoft / Minecraft 认证](docs/authentication.md) —— OAuth、XBL / XSTS、client_id 审核
- [发行与下载策略](docs/09-distribution-and-download-policy.md) —— 通道、Provider、`DownloadPlan`、镜像
- [路线图](docs/05-roadmap.md) —— 阶段目标与验收标准

## 许可证

代码以 **AGPL-3.0-only** 发布（正文见 [LICENSE](LICENSE)）：遵守 AGPL 义务（分发时开放源码、保留声明并标注修改）即可自由使用、学习、修改与再分发。
第三方依赖与内嵌运行库见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)；图标字体为上游 MIT 子集，随应用分发于 `app/assets/licenses/Tabler-Icons-MIT.txt`。

## 致谢

Tabler Icons（MIT）、Noto Sans SC（OFL-1.1）、Flutter / .NET / Rust、Modrinth、BMCLAPI。逐项来源见 `app/assets/icons/*/SOURCES.md` 与 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

## 联系与贡献

- 仓库：<https://github.com/BetaStudio2/ArchoeraCX>
- **不接受外部代码贡献**（PR / 补丁 / 代码片段），原因见 [CONTRIBUTING.md](CONTRIBUTING.md)；欢迎脱敏后的 Issue、崩溃日志、建议与文档纠错。
- 隐私与漏洞报告见 [PRIVACY.md](PRIVACY.md)、[SECURITY.md](SECURITY.md)。

## 免责声明

本项目与 Mojang Studios、Microsoft 无关联，不分发游戏本体或资源文件，也不提供绕过正版验证的服务。

> NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
