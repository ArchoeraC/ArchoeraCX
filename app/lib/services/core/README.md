# services/core · FFI 客户端

当前已实现：

- `core_client.dart`：对外唯一入口。`CoreClient` 由引擎(`core_transport.dart`)与各域 mixin 组合而成，公开 API 与拆分前一致(`openDefault`、`pingAsync`、生命周期成员等)
- `core_transport.dart`：FFI 绑定、库查找 candidates、`runAsync` 任务引擎、信封解码、`CoreException` / `CoreUnavailableException`、`CoreStatusCodes`、`noChange`、`CoreDetailedProgress`
- `event_pump.dart`：wake 驱动、`poll` 批量 drain；**无定时器/轮询**
- `core_event.dart`：事件载荷类型
- 领域文件夹(每个域一个 mixin 文件 + 归属此域的类型，方法混合进 `CoreClient`、信封解析保持文件私有)：
  - `accounts/` – 账户与凭据域
  - `downloads/` – DownloadPlan 执行、区域/合规策略、`.part` 维护、任务快照与更新检查
  - `instances/` – 实例域与维护任务
  - `java/` – Java 扫描/运行时规划/路径存储
  - `launch/` – 启动、日志、内存、预检、动作
  - `loaders/` – 加载器安装/规划/版本
  - `packs/` – 整合包安装与结果
  - `resources/` – Modrinth 资源域与模型
  - `settings/` – 应用设置
  - `system/` – 系统、外观、内存、状态、ping、构建指纹
  - `versions/` – 版本清单、原版安装计划
  - `wiki/` – wiki 源
- `core_client_integration_test.dart` 通过 `system.pingAsync` 探测原生核心；首页不再暴露调试卡片

原则：

- Dart **不直连业务 API**；所有业务经 C++ 枢纽。
- UI 线程不阻塞；长任务不得同步等待。
- 图片加载是唯一例外。
- `core_transport.dart` 中标注 *internal transport API* 的成员只供 `services/core` 内部使用；调用方一律走 `CoreClient`。

参考 [`docs/10-architecture-design.md`](../../../../docs/10-architecture-design.md) §6、§10.2。
