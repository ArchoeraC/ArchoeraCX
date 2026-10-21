# Security Policy

## 报告漏洞

请使用 GitHub 仓库的 **Security → Report a vulnerability**（GitHub Security Advisories）私下报告。

不要在公开 Issue 中粘贴：

- Microsoft access token / refresh token / device code
- Minecraft access token / certificates
- 未脱敏的崩溃转储、日志、账户数据库或本地路径

## 范围

当前只维护 `main` 分支的最新代码。支持范围内的问题包括：

- ABI / FFI 边界的越界、释放后使用、崩溃
- 路径逃逸、Zip Slip、任意文件写入
- token 泄漏到日志、事件、UI 或错误消息
- 下载 hash 校验绕过、镜像降级导致的完整性破坏
- SQLite 并发写、凭据文件权限问题

## 设计边界

- 启动器不分发游戏本体或资源文件。
- 不提供绕过正版验证的在线服务。
- 不允许复用其他启动器的 Microsoft client_id。
- 账户 token 只由 C# 持有；Flutter 不直接读取 token 或 SQLite。

## 处理

维护者会先确认影响范围，再决定修复与披露时间。修复发布前请不要公开细节。
