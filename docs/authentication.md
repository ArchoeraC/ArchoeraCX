# 认证：Microsoft → Minecraft Services

本文是账户登录的当前实现与上线门槛。默认方案为 **OAuth 2.0 Device Code Flow**，不使用 redirect URI，也不使用自定义协议。

## 1. 结论

```text
Microsoft Device Code
  → Microsoft access/refresh token
  → Xbox Live user.authenticate
  → XSTS authorize
  → Minecraft login_with_xbox
  → /minecraft/profile
  → /player/certificates
```

- **Entra 应用**：公共客户端，不创建 client secret。
- **Minecraft Services**：还必须单独审核 `login_with_xbox` 使用的 client_id。
- **token 不跨 C ABI**：device code、access token、refresh token 只存在于 C#。
- **失败不落账户**：只有完整链路成功后才写账户元数据和凭据。

## 2. client_id 配置

client_id 是应用构建配置，不由用户在设置页填写，也不硬编码在源码中。

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

构建脚本会把值生成到 `obj/.../MicrosoftBuildConfig.g.cs`；该文件被 `.gitignore` 忽略，不进入仓库。

## 3. Device Code 流程

### 3.1 申请设备码

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

### 3.2 轮询 token

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
GET https://api.minecraftservices.com/minecraft/profile
GET https://api.minecraftservices.com/player/certificates
Authorization: Bearer <Minecraft access token>
```

profile 提供 UUID、用户名、皮肤和披风；certificates 原文只写入 C# 凭据文件，不返回 UI。

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
accounts.credentials.json     refresh / Minecraft token（Unix 0600）
logs/account.log              脱敏诊断日志
```

- device code 只在内存中。
- access/refresh token 不写日志、不跨 ABI、不进入 UI 模型。
- 失败登录不会写入账户文件。
- 凭据文件是过渡方案；后续迁移 Windows DPAPI、macOS Keychain、Linux libsecret。
- 多账户切换和 refresh 单飞尚未完整实现。

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
- `microsoft_start_test.dart`：运行时 client ID 能真实发起 device code（需网络与 client ID）。

人工上线验收：

- [ ] Entra 应用可申请设备码
- [ ] 用户完成授权后可拿到 Microsoft token
- [ ] XBL/XSTS 成功
- [ ] `login_with_xbox` 成功（client_id 已审核）
- [ ] profile 返回 UUID/用户名/皮肤
- [ ] 重启应用后 refresh 可用
- [ ] 日志不出现 token/device code

## 9. 已知限制

- 只实现 Device Code；loopback + PKCE 尚未实现。
- 真实登录受 Minecraft Services 审核阻塞。
- OS 凭据库尚未接入。
- 启动游戏尚未消费 Minecraft token/certificates。
