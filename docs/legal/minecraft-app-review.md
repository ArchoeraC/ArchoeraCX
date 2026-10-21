# Minecraft Services application review

Status: submission draft for the **current Device Code implementation**. Do not
submit the older authorization-code/sessionserver wording unless those features
are implemented first. The client ID itself is stored as the GitHub Actions
secret `ARCHOERACX_MICROSOFT_CLIENT_ID` and must not be committed here.

## Submission text (English)

ArchoeraCX is a third-party Minecraft: Java Edition launcher. We are requesting
review of our Azure Application (Client) ID so the launcher can call the
Minecraft Services authentication and profile endpoints.

**Authentication.** Users sign in through Microsoft OAuth 2.0 Device Code Flow
using a native public client. The launcher requests a device code from
`login.microsoftonline.com/consumers`, shows the one-time code and verification
URL, and polls the token endpoint until the user completes sign-in. We never ask
for or store Microsoft passwords, and the public client uses no client secret.
The device code remains in memory inside the native module. After Microsoft
authorization, the launcher authenticates with Xbox Live and XSTS, then calls
`https://api.minecraftservices.com/authentication/login_with_xbox` to obtain a
Minecraft access token.

**Profile, ownership and skin data.** With that token, the launcher calls
`https://api.minecraftservices.com/entitlements/mcstore` to check for a
Minecraft: Java Edition entitlement, and
`https://api.minecraftservices.com/minecraft/profile` to retrieve the UUID,
username, skin/cape URLs and XUID. It may also call
`https://api.minecraftservices.com/player/certificates` to obtain certificates
for the current session. We do not call `sessionserver.mojang.com`. The UUID and
username are stored locally as account metadata and used to show the account in
the launcher UI; they are not uploaded. Skin textures are fetched from
Mojang's CDN and may be held in memory for display; they are not uploaded,
shared, or used for any other purpose. Profile and skin data can be removed
with the launcher's local data.

**Storage and compliance.** Microsoft refresh tokens, Minecraft access tokens
and certificates are stored in an encrypted local credential vault on the
user's device (OS secure storage where available, with a user-only local key
fallback otherwise). They are not sent to ArchoeraCX servers, are not written
to logs, and are not exposed to the UI model. The launcher does not bypass or
disable authentication, ownership/license checks, or other required checks, and
does not use another application's Client ID. The current GitHub-only alpha
does not enable online launch; when online launch is enabled it will use the
same reviewed Client ID and the official Microsoft, Xbox and Minecraft services.
We can provide more details or a test build if needed.

## Why this differs from the earlier draft

| Earlier draft | Current implementation |
|---|---|
| Authorization code flow with a native public-client redirect URI | OAuth 2.0 Device Code Flow; no redirect URI or local callback listener |
| `sessionserver.mojang.com` profile lookup | `api.minecraftservices.com/minecraft/profile`; sessionserver is not called |
| Ownership implied by profile/login | `/entitlements/mcstore` is checked before accepting the account |
| “Before enabling launch” | Ownership is checked at sign-in; online launch remains disabled in the current alpha |

If Minecraft review specifically requires the authorization-code flow, implement
loopback redirect + PKCE and update this document before submitting. Do not
describe an implementation that does not exist.
