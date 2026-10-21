# ArchoeraCX Privacy Policy

**Last updated:** 2026-10-21  
**Status:** draft for the current development/review build. The contact and
operator details marked `[TODO]` must be completed before public distribution.

ArchoeraCX is a third-party desktop launcher for Minecraft: Java Edition. It is
not an official Minecraft product and is not affiliated with Mojang Studios or
Microsoft. This policy describes how the launcher handles data on the user's
device.

## 1. Controller and contact

- Project: ArchoeraCX
- Source/homepage: https://github.com/BetaStudio2/ArchoeraCX
- Privacy contact: `[TODO: privacy contact email]`
- Operator/legal entity: `[TODO: legal entity, if any]`

## 2. Scope

This policy applies to the ArchoeraCX desktop application and its local
account, profile, instance and download features. It does not apply to
Microsoft, Xbox, Minecraft Services, Modrinth, BMCLAPI or any other third-party
service. Those services have their own privacy policies and terms.

## 3. Data processed

### 3.1 Microsoft/Minecraft account data

When the user signs in with a Microsoft account, the launcher processes:

- the Minecraft profile UUID and username;
- the Xbox XUID, if returned by Xbox services;
- the current skin and cape URLs;
- the Microsoft refresh token, Minecraft access token and their expiry time;
- player certificates returned by Minecraft Services, when available.

The launcher does **not** ask for or store the user's Microsoft password. The
Microsoft device code exists only in memory during sign-in and is not written to
disk.

### 3.2 Local/offline account data

For a local account, the launcher stores the display name and a locally derived
offline UUID. Local accounts are not Microsoft accounts and are not used to
pretend to be a Microsoft/Minecraft account.

### 3.3 Application data

The launcher stores settings, instance metadata, installed content records,
download plans, launch logs and local paths. Account metadata is stored locally;
credentials are stored in the encrypted local credential vault described below.

### 3.4 Network data

When the launcher contacts Microsoft, Xbox, Minecraft Services, Modrinth or a
download mirror, those providers receive the network information necessary to
serve the request, such as the user's IP address and request headers. ArchoeraCX
does not operate a server that receives or stores account tokens.

### 3.5 Images and caches

Skin and project images may be loaded from Mojang/Modrinth CDN endpoints and
cached locally or in memory to avoid repeated downloads. Image caches are used
only to render the launcher UI.

## 4. Purposes and legal basis

We process the data above to:

- authenticate the user and display the current account/profile;
- display skins and capes in the account UI;
- construct launch arguments for the current session;
- download and verify game files and content selected by the user;
- maintain local settings, instances and diagnostic logs;
- protect the launcher and comply with applicable legal obligations.

Where required, processing is based on performance of the requested service,
the user's consent, or compliance with legal obligations. Users can withdraw
consent for optional processing by removing the relevant account or local data.

## 5. Storage and security

- Account metadata is stored in the launcher's local data directory.
- Microsoft refresh tokens and Minecraft session tokens are stored in an
  encrypted local credential vault. The vault uses OS secure storage where
  available and a user-only local key file as a fallback.
- Tokens are not uploaded to ArchoeraCX servers, are not written to logs, are
  not exposed to the UI model and do not cross the native module ABI.
- Diagnostic logs are local and may contain error codes, file paths and account
  display names. They are not intended to contain tokens or passwords.
- The launcher does not include advertising, third-party analytics or telemetry
  SDKs.

No method of storage is perfectly secure. Users should protect their device and
operating-system account.

## 6. Sharing and third parties

We do not sell personal data. Data is exchanged only with services needed for
the feature the user requested, including:

- Microsoft/Xbox for OAuth and Xbox authentication;
- Minecraft Services for profile, ownership-related responses and certificates;
- Mojang CDN for game files and skin textures;
- Modrinth and selected download mirrors for content selected by the user;
- the operating system's credential store, when available.

These third parties process data under their own terms and privacy policies.
ArchoeraCX does not control their retention or cross-border processing.

## 7. Cross-border processing

Microsoft, Xbox and Minecraft Services are operated outside the user's country
and may process data in other jurisdictions. When the user chooses Microsoft
sign-in or downloads from an international provider, the request is sent
directly to that provider. ArchoeraCX does not add a separate server-side copy
of the account tokens.

Users in jurisdictions with cross-border data transfer requirements should
review the relevant provider policies and obtain any required consent or
approval before using those features.

## 8. Retention and deletion

Local data remains on the device until the user deletes it. Removing an account
deletes its local metadata and vault entry. Removing the launcher's local data
or uninstalling the application removes the corresponding local files, subject
to operating-system backups. Download records and logs may remain until the
user clears them.

## 9. Children and minors

ArchoeraCX is not directed to children under 14. If a user under 14 uses the
launcher, a parent or guardian must consent to the processing of the child's
personal information and supervise use, where required by applicable law.

The launcher does not itself operate a Minecraft server, sell in-game items, or
provide in-game purchases. If ArchoeraCX later provides an online game service
in a jurisdiction that requires real-name verification, anti-addiction measures
or additional guardian controls, those requirements must be implemented before
that service is enabled.

## 10. User rights

Subject to applicable law, users may request access to, correction of, deletion
of, or a copy of their personal data, and may withdraw consent for optional
processing. Because account credentials are stored locally, users can also
remove them directly through the launcher's account management or by deleting
the local data directory.

Contact: `[TODO: privacy contact email]`.

## 11. Changes

We may update this policy as the launcher changes. Material changes will be
announced in the repository and, where required, in the application before the
change takes effect. The date at the top of this document identifies the current
version.

## 12. Relationship to third-party terms

This policy does not replace the Minecraft EULA, Microsoft terms, Modrinth terms
or any other third-party agreement. Users are responsible for complying with
those terms. ArchoeraCX does not bypass authentication, ownership checks,
license checks or other required checks.

---

## 中文摘要

ArchoeraCX 是第三方 Minecraft: Java Edition 启动器，不是 Mojang/Microsoft
官方产品。启动器仅在本地处理账户元数据、设置、实例、日志和下载记录；
Microsoft refresh token 与 Minecraft token 存入本地加密凭据保险库，不上传
到 ArchoeraCX 自有服务器，不写入日志，不进入 UI 模型，也不跨本地模块 ABI。
登录时会直接访问 Microsoft、Xbox 和 Minecraft Services；下载时会访问
Mojang、Modrinth 或用户选择的镜像。皮肤和项目图片可能缓存在本地用于渲染。

发布版必须先登录已验证的 Microsoft/Minecraft 账户，之后才允许添加或选择
离线账户；离线账户仅用于本地启动，不代表正版身份。未满 14 周岁的用户应在
监护人同意和指导下使用。若未来在中国大陆提供在线游戏服务，应在启用前评估
并落实实名、防沉迷、未成年人保护和个人信息保护要求。具体法律义务请以适用
法律、监管要求和专业法律意见为准。
