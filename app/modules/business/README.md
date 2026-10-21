# app/modules/business · C# 业务核心

计划中的 `archoera_cs`（.NET 10 / NativeAOT）：

```
Archoera.Domain/          # 纯模型/规则，无 I/O
Archoera.Application/     # 用例/接口/编排
Archoera.Infrastructure/  # HTTP、SQLite、文件、注册表、进程、凭据库
Archoera.Exports/         # NativeAOT 导出层
Archoera.DevHost/         # JIT 调试/CLI
```

业务规则、API、SQLite 唯一写入方都在这里；导出层保持极薄。
参考 [`docs/10-architecture-design.md`](../../../docs/10-architecture-design.md) §8。

## 方法注册表

`Archoera.Application/Modules/ModuleMethodRegistry.cs` 负责“方法名 → handler”的确定性注册；`Archoera.Infrastructure/Modules/BusinessModuleRegistry.cs` 是唯一组合根。新增业务域时实现 `IModuleMethodHandler` 并在这里注册，`Archoera.Exports/ModuleExports.cs` 不再增长 if 链。重复方法名会在启动时直接失败。

## 账户

- `MicrosoftAuthClient`：Device Code → Microsoft token → XBL → XSTS → Minecraft login → profile/certificates。
- `AccountModuleService`：账户列表/切换/刷新/移除、Microsoft 登录、离线账户。
- `JsonAccountStore`：账户元数据与敏感凭据分离；凭据文件在 Unix 上写为 0600。token 不跨 C ABI，也不写日志。

## Forge / NeoForge 生成物

`Archoera.Domain/Loaders` 与 `Archoera.Infrastructure/Loaders` 提供第一片加载器安装逻辑：

- `ForgeArtifactPlanner`：只读 installer zip，生成**可迁移**的 artifact manifest（无绝对路径），
  解析 `install_profile.json` spec 1、`data` 客户端变量、maven 坐标、`BINPATCH`/`client.lzma`、
  内嵌 `maven/` 与 `version.json` 空 URL 库；不启动 Java、不联网。
- `LoaderArtifactVerifier`：root/libraries 路径封闭、存在性、size、SHA-1。
- `ForgeProcessorRunner`：argv 数组、无 shell、清理 JVM 环境变量、超时杀进程树。
- `ForgeRepairService`：按 LoaderId 加锁，先补 installer 解压物，跳过有效输出，
  从最早缺失的 processor 开始执行，每个输出立即校验；不下载任何 `-client.jar`。

离线自测（不需要 Java/网络）：

```bash
dotnet run --project modules/business/Archoera.DevHost -c Release -- --self-test-loader
dotnet test modules/business/Archoera.Tests/Archoera.Tests.csproj -c Release
```

DevHost 还支持 `--verify-loader <installer.jar>` 与 `--repair-loader`；不带参数运行时会打印完整 usage。
