# Reference / MANIFEST

只读参考仓库记录。`upstream/` **不参与构建、不进发布物**，并已加入 `.gitignore`。

| 仓库 | Commit | 许可证 | LICENSE SHA-256 | 克隆日期 | 用途 |
|---|---|---|---|---|---|
| [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) | `ffa2e908a9aa545c13175ec7b700472220e6d280` | MIT | `d67622c23e5c980df485d1b1667a52c49f28fc73130c11d02f44a10be6ce9d7d` | 2026-10-20 | Java 路径/版本探测、安装/启动流程参考 |
| [ProjBobcat](https://github.com/Corona-Studio/ProjBobcat) | `57532e29f73e38e64d809893a81518eb9f10d1a3` | MIT | `f084dfac9705b35ea9e77cba11e381f6e90f5e4d4fdc468d1929459b2af86200` | 2026-10-20 | Java 深度搜索、注册表/常见目录、资源安装器 |
| [MinecraftLaunch](https://github.com/Lunova-Studio/MinecraftLaunch) | `b84e36eb68949ae8d95aa89711bfbf60d1cc4ff7` | MIT | `28d90c631d23caff62273a1e343223bdee809e3f634246a210aa188fd948dbb7` | 2026-10-20 | Java 探测/安装、启动参数模型 |

## 使用边界

- 这三个项目是 MIT：可以依赖、参考实现；复制片段时必须保留版权与许可声明。
- 本项目的 Java 探测实现位于 `app/modules/business/Archoera.Infrastructure/Java/`，按“候选枚举 + `java -XshowSettings:properties -version` 校验”的行为重写，未复制上游代码。
- 每次重克隆/升级参考仓库时更新本表 commit 与 LICENSE 哈希。
