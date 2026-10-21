# app/core · C++ 枢纽

计划中的 `archoera_core`（C++20）源码目录：

- ABI 头文件复用 `app/contracts/abi/archoera_abi.h`
- `src/{abi,dispatcher,task,effect,event,module,config,log,crash,platform}`（当前是 `archoera_core.cpp` + `method_registry.*` + `module_registry.*` + `module_host.*`）
- `ModuleRegistry` 按名字持有 `archoera_cs` 与可选的 `archoera_dl`；`MethodRegistry` 把公开方法映射到 core 或具名模块
- 模块按 `ARCHOERACX_CS_MODULE` / `ARCHOERACX_DL_MODULE` → core 同目录 → 相对构建目录加载；缺失时对应方法返回 `E_UNSUPPORTED`
- `system.modules` 返回已加载模块；未知方法同步返回 `E_UNSUPPORTED`
- 测试：`abi_smoke`、`async_smoke`、`module_smoke`（使用 `libarchoera_fake_module.so` 作为 oracle）

职责与禁止事项见 [`docs/10-architecture-design.md`](../../docs/10-architecture-design.md) §5。
C++ 只做 ABI、生命周期、任务、事件与模块编排，**不写业务规则**。
