# services/core · FFI 客户端

当前已实现：

- `core_client.dart`：加载 `archoera_core`、`archoera_init/shutdown`、`system.ping`、两段式 `archoera_result`
- `CoreUnavailableException` / `CoreException` / `PingResult`
- 首页 `CoreStatusCard` 通过它探测原生核心

下一步：

- `EventPump`：wake 驱动、`poll` 批量 drain、进度合并；**无定时器/轮询**
- `TaskHandle`：taskId / 状态 / 取消 / 结果生命周期
- 异步方法（安装、下载、启动）全部改为“投递任务 + 事件回调”

原则：

- Dart **不直连业务 API**；所有业务经 C++ 枢纽。
- UI 线程不阻塞；长任务不得同步等待。
- 图片加载是唯一例外。

参考 [`docs/10-architecture-design.md`](../../../../docs/10-architecture-design.md) §6、§10.2。
