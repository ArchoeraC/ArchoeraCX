# 认证：Microsoft → Minecraft Services

本文是账户登录的当前实现与上线门槛。默认方案为 **OAuth 2.0 Authorization Code Flow + PKCE + loopback**；Device Code Flow 保留为 fallback。

## 1. 结论

```text
Microsoft Authorization Code + PKCE
  → 系统浏览器登录
  → loopback callback (127.0.0.1)
  → Microsoft access/refresh token
  → Xbox Live user.authenticate
  → XSTS authorize
  → Minecraft login_with_xbox
  → /entitlements/mcstore
  → /minecraft/profile
  → /player/certificates
```

- **Entra 应用**：公共客户端，不创建 client secret。
- **Minecraft Services**：还必须单独审核 `login_with_xbox` 使用的 client_id。
- **token 不跨 C ABI**：device code、access token、refresh token 只存在于 C#。
- **失败不落账户**：只有完整链路成功后才写账户元数据和凭据。

## 2. client_id 配置

client_id 是应用构建配置，不由用户在设置页填写，也不硬编码在源码中。
新 GitHub 仓库已把值保存为 Actions secret `ARCHOERACX_MICROSOFT_CLIENT_ID`；
Actions 当前未启用。

发布 / CI 构建注入：

```bash
ARCHOERACX_MICROSOFT_CLIENT_ID="<Application (client) ID>" ./tool/build-cs.sh
# 或
./tool/build-cs.sh -p:MicrosoftClientId="<Application (client) ID>"
```

本地调试可运行时覆盖，优先级高于构建期默认值：

```bash
ARCHOERACX_MICROSOFT_CLIENT_ID="<Application (client) ID>" \
  ./build/linux/x64/release/bundle/archoera_cx
```

构建脚本不会把明文写进生成代码：`GenerateMicrosoftBuildConfig` 目标用内联
MSBuild 任务把 client_id 变成**高熵 blob**（16 字节随机 nonce + SHA-256
keystream 异或 + 位置项，思路参考 PCL-CE 的字符串强熵处理），只把 blob 写进
`obj/.../MicrosoftBuildConfig.g.cs`；运行时由 `ClientIdObfuscation.Decode` 在
内存里还原。该文件被 `.gitignore` 忽略，不进入仓库。

覆盖范围：仓库、生成源码、`strings` / 反编译字符表里都不会出现明文 client_id
（AOT 产物已用 `strings -el` 校验为零命中）。这是**提高抓取成本**，不是密码学
保密——公共客户端的 id 运行时必须可读，能逆向二进制的人仍然可以还原它；随机
nonce 让每次构建的 blob 都不同，无法沉淀固定特征串。

### 2.1 官方构建标记（关于页）

官方构建的 client_id 具有唯一性，可用作构建来源标记：

- C# 只对编译期注入的 client_id 计算**域分隔 SHA-256**
  （`archoeracx/official-client-id/v1:` + id），通过 `system.identity`
  返回 `channel` / `official` / `clientIdSha256` / `clientIdFingerprint`；
  **原始 id 不跨 ABI、不写日志、不进仓库**。
- 仓库里只有官方 client_id 的摘要常量
  `OfficialBuildIdentity.OfficialClientIdSha256`
  （`9b8dd8ece4d8bdc8809209640b6800e4b33c40f94c8f57351e3666ab95f5ccf9`）。
  改 client_id 时必须在同一提交更新该常量。
- 摘要不可逆：client_id 是 UUIDv4（122 位随机），从摘要反推需要约 2^122 次
  预映像搜索；域分隔又让它无法在通用 UUID 哈希表里被直接匹配。官方二进制里
  仍能直接读到 client_id——公共客户端的 id 本身就是公开的，摘要只是让仓库
  不必提交原值。
- 目前 UI 不再展示构建标识（`system.identity` 保留给诊断与后续签名方案）。
  需要说明的是：client_id 是公开信息，任何人把它编进自己的构建都能得到同样的
  摘要，因此它只能说明构建配置，不构成二进制签名；需要防篡改证明时应另加离线
  私钥签名（参考 ArchoeraMusic 的 ECDSA watermark 方案）。

## 3. Authorization Code + PKCE（默认）

当前 UI 默认使用该流程：

1. C# 生成随机 `code_verifier`、`state`，计算
   `code_challenge = BASE64URL(SHA256(code_verifier))`；
2. 在 `127.0.0.1` 的固定端口（默认 53682）启动本地回调监听；
3. Flutter 打开系统浏览器，授权 URL 使用：
   `response_type=code`、`redirect_uri=http://localhost:53682/callback`、
   `code_challenge_method=S256`、`state`、`scope`；
4. 浏览器回调后校验 `state`，用 `code + code_verifier` 换取 token；
5. 回调监听立即关闭，然后进入 XBL/XSTS/Minecraft 链路。

Entra 应用需要在 **Mobile and desktop applications** 中**精确添加**
`http://localhost:53682/callback`。如果端口被占用，可设置
`ARCHOERACX_OAUTH_PORT` 并在 Entra 中注册对应 URI。本地回调只绑定
`127.0.0.1`，不要暴露到局域网。

Device Code 仍可通过
`account.login.microsoft.start` 的 `{"flow":"device_code"}` 使用，作为无法绑定
loopback 时的 fallback。

## 3A. Device Code 流程（fallback）

### 3A.1 申请设备码

```http
POST https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode
Content-Type: application/x-www-form-urlencoded

client_id=<Application (client) ID>
scope=XboxLive.signin offline_access
```

响应包含：

```text
device_code, user_code, verification_uri, expires_in, interval
```

UI 只展示 `user_code` 和 `verification_uri`；`device_code` 保留在 C# 内存中。

### 3A.2 轮询 token

```http
POST https://login.microsoftonline.com/consumers/oauth2/v2.0/token
Content-Type: application/x-www-form-urlencoded

grant_type=urn:ietf:params:oauth:grant-type:device_code
client_id=<Application (client) ID>
device_code=<device_code>
```

轮询必须遵守 `interval`：

| 返回 | 处理 |
|---|---|
| `authorization_pending` | 继续等待 |
| `slow_down` | 增加间隔 |
| `expired_token` | 结束，提示重新开始 |
| `authorization_declined` / `access_denied` | 结束，提示用户拒绝 |
| 成功 | 得到 `access_token` + `refresh_token` |

轮询使用可取消的 `Task.Delay`，不使用后台定时器。UI 取消后停止轮询并清除 device code。

## 4. Xbox Live / XSTS / Minecraft

### 4.1 Xbox Live

```http
POST https://user.auth.xboxlive.com/user/authenticate
x-xbl-contract-version: 1
```

```json
{
  "Properties": {
    "AuthMethod": "RPS",
    "SiteName": "user.auth.xboxlive.com",
    "RpsTicket": "d=<Microsoft access token>"
  },
  "RelyingParty": "http://auth.xboxlive.com",
  "TokenType": "JWT"
}
```

### 4.2 XSTS

```http
POST https://xsts.auth.xboxlive.com/xsts/authorize
x-xbl-contract-version: 1
```

```json
{
  "Properties": {
    "SandboxId": "RETAIL",
    "UserTokens": ["<XBL token>"]
  },
  "RelyingParty": "rp://api.minecraftservices.com/",
  "TokenType": "JWT"
}
```

常见 XSTS 错误：

| XErr | 代码 | 含义 |
|---|---|---|
| `2148916233` | `XSTS_NO_XBOX_ACCOUNT` | 没有 Xbox 档案 |
| `2148916235` | `XSTS_REGION_UNAVAILABLE` | 地区不可用 |
| `2148916236/7` | `XSTS_ADULT_VERIFICATION` | 需要成人验证 |
| `2148916238` | `XSTS_CHILD_ACCOUNT` | 儿童账户需要家庭组 |
| `2148916227` | `XSTS_ACCOUNT_BANNED` | 账户被封禁 |

### 4.3 Minecraft

```http
POST https://api.minecraftservices.com/authentication/login_with_xbox
Content-Type: application/json

{"identityToken":"XBL3.0 x=<uhs>;<XSTS token>"}
```

拿到 Minecraft access token 后：

```http
GET https://api.minecraftservices.com/entitlements/mcstore
GET https://api.minecraftservices.com/minecraft/profile
GET https://api.minecraftservices.com/player/certificates
Authorization: Bearer <Minecraft access token>
```

`/entitlements/mcstore` 只读检查 `product_minecraft` / `game_minecraft` 权益；
没有权益时登录失败，不写入账户。profile 提供 UUID、用户名、皮肤和披风；
certificates 原文只写入 C# 凭据文件，不返回 UI。不调用 `sessionserver.mojang.com`。

## 5. Minecraft Services 应用审核（关键门槛）

**Entra 配置正确、OAuth/XBL/XSTS 全部成功，也不代表 Minecraft 登录一定成功。**

`login_with_xbox` 使用的 client_id 有独立的 Minecraft Services allowlist。未审核时常见结果：

```text
403 Invalid app registration
MINECRAFT_APP_NOT_APPROVED
```

2026-10-21 实测：

```text
MICROSOFT_TOKEN_OK
MINECRAFT_APP_NOT_APPROVED  Minecraft Services has not approved this application client id.
```

此时账户和 token 都不会写入。

### 上线步骤

1. 在 Entra 注册应用，类型为公共客户端，启用 public client flows。
2. 不创建 client secret。
3. 将 **Application (client) ID** 提交到：
   `https://aka.ms/mce-reviewappid`
4. 准备应用名称、client ID、使用场景、隐私政策/主页和可接收审核结果的联系方式。
5. 审核通过后使用**同一个 client ID**重新登录，不需要改代码。
6. 不要复用其他启动器的 client ID；这违反服务条款，也会让应用身份不可控。

如果审核入口不可用或被拒绝，应联系 Minecraft 官方支持 / Xbox Developer 渠道确认当前流程，不能在启动器侧绕过。

## 6. 存储与安全

当前实现：

```text
accounts.json                 账户元数据
archoera-vault                凭证保险库（crypto/file 免密模式）
logs/account.log              脱敏诊断日志
```

- device code 只在内存中。
- access/refresh token 不写日志、不跨 ABI、不进入 UI 模型。
- 失败登录不会写入账户文件。
- 旧 `accounts.credentials.json` 在首次使用时迁移进保险库并删除；生产构建需包含 `archoera-vault`（CI 已构建并检查）。
- 多账户切换和 refresh 单飞尚未完整实现。
- 发布版账户策略：账户元数据里必须有一条带 UUID 的 Microsoft 账户，并且该账户在凭据保险库中必须有可用凭据；token 过期时会先刷新并重新校验 entitlement，才允许添加/选择离线账户或开始离线启动。仅伪造 `accounts.json` 元数据不会解锁。
- 离线启动也有 C# 硬门槛，不依赖 Dart 传入的 `requirePremium`，也没有公开的跳过开关。

### 申请前检查

- [ ] 使用自己注册的 Entra 公共客户端，启用 public client flows，不创建 secret。
- [ ] 固定同一个 Application (client) ID 用于审核、开发和 CI；构建时注入 `-p:MicrosoftClientId=<GUID>`，不要只依赖运行时覆盖。
- [ ] 准备应用名称、使用场景、主页/隐私政策、联系地址；Minecraft Services 审核针对 client ID，不针对某个二进制。
- [ ] 审核前 `login_with_xbox` 返回 `MINECRAFT_APP_NOT_APPROVED` 属预期；通过后无需改代码。
- [ ] 发布前确认 `launch.start` 的在线账户启动仍未接线；不要把“登录成功”当作“可在线启动”的评审演示。

## 7. 实现位置

| 文件 | 作用 |
|---|---|
| `app/modules/business/Archoera.Infrastructure/Accounts/MicrosoftAuthClient.cs` | Device Code、refresh、XBL/XSTS/Minecraft、profile/certificates |
| `.../AccountModuleService.cs` | `account.*` 方法、待处理 flow、账户写入、脱敏日志 |
| `.../JsonAccountStore.cs` | 元数据/凭据分离存储 |
| `app/lib/pages/accounts_page.dart` | 账户页、设备码弹窗、离线账户 |
| `app/lib/services/core/core_client.dart` | `account.*` FFI 封装 |

## 8. 测试

自动化：

- `AccountAuthTests`：完整 mock 链路、pending、XSTS 错误、refresh、离线账户、日志脱敏。
- `account_integration_test.dart`：离线账户通过 C++/C#/SQLite 持久化。
- `microsoft_start_test.dart`：运行时 client ID 能真实发起默认的 authorization code 挑战（校验 `authorizationUrl` 含 `client_id` / `code_challenge` / `code_challenge_method=S256` 与 loopback `redirectUri`），并验证 `{"flow":"device_code"}` 仍能拿到 `userCode`（需网络与 client ID）。
- `LaunchOnlinePlanTests` / `LaunchModuleOnlineTests`：在线启动参数、准备状态和错误映射。

人工上线验收：

- [ ] Entra 应用可申请设备码
- [ ] 用户完成授权后可拿到 Microsoft token
- [ ] XBL/XSTS 成功
- [ ] `login_with_xbox` 成功（client_id 已审核）
- [ ] `/entitlements/mcstore` 返回 Minecraft: Java Edition 权益
- [ ] profile 返回 UUID/用户名/皮肤
- [ ] 重启应用后 refresh 可用
- [ ] 日志不出现 token/device code

## 9. 在线启动

`launch.prepare` / `launch.start` 的 `mode=online` 已接通：

- 只允许活动账户为 Microsoft 且具有 Minecraft profile；
- 从凭据保险库读取 Minecraft access token；
- 会话在 2 分钟内到期或已过期时，用 refresh token 重新走
  Microsoft → XBL/XSTS → `login_with_xbox` → entitlement/profile；
- 刷新成功后更新账户元数据和保险库；
- 启动参数使用真实的 `auth_access_token`、`clientid`、`auth_xuid`、
  `user_type=msa` 和 profile UUID；
- 不会把在线账户静默降级为离线启动；离线模式仍只接受离线账户。

已知限制：

- 只实现 Device Code；loopback + PKCE 尚未实现。
- 审核提交文本见 [`docs/legal/minecraft-app-review.md`](legal/minecraft-app-review.md)；不要提交与实现不一致的 authorization-code/sessionserver 描述。
- 真实登录/在线启动仍受 Minecraft Services client ID 审核阻塞。
- 证书保存在保险库，但当前没有注入 JVM 参数；如果后续版本需要安全聊天/证书消费，需单独接线。
- 在线启动会把 access token 放入 JVM 参数；这与官方启动器一样会出现在进程列表中，日志不记录 token。
