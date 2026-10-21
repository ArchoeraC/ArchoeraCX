# Minecraft Services application review

Status: submission draft for the **current Authorization Code + PKCE + loopback
implementation**. Online launch is wired to the stored Minecraft session. The
older sessionserver wording must not be submitted because that endpoint is not
called. The Client ID itself is stored as the GitHub Actions
secret `ARCHOERACX_MICROSOFT_CLIENT_ID` and must not be committed here.

## Submission text (English)

ArchoeraCX is a third-party Minecraft: Java Edition launcher. We are requesting
review of our Azure Application (Client) ID so the launcher can use the
Minecraft Services authentication and profile endpoints.

**Authentication.** Users sign in through Microsoft OAuth 2.0 Authorization
Code Flow with PKCE and a native public-client loopback redirect. The launcher
opens the system browser to the Microsoft authorization page; the redirect URI
is `http://localhost:<ephemeral-port>/callback`, bound only to the local
loopback interface. After the user signs in, the launcher validates the state,
exchanges the authorization code plus PKCE verifier for tokens, and closes the
local listener. We never ask for or store Microsoft passwords, and the public
client uses no client secret. After Microsoft authorization, the launcher
authenticates with Xbox Live and XSTS, then calls
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
the launcher UI; they are not uploaded. Skin textures are fetched from Mojang's
CDN and may be kept in the image cache on the device for rendering. They are
not uploaded, shared, or used for any other purpose, and can be removed with
the launcher's local data.

**Storage and compliance.** Microsoft refresh tokens, Minecraft access tokens
and certificates are stored in an encrypted local credential vault on the
user's device (OS secure storage where available, with a user-only local key
fallback otherwise). They are not sent to ArchoeraCX servers, are not written
to logs, and are not exposed to the UI model. The launcher checks the official
Minecraft entitlement during sign-in and does not bypass or disable
authentication, ownership checks, license checks, or other required checks. It
does not use another application's Client ID. The same reviewed Client ID is
used for online launch; the launcher resolves the stored Minecraft session and
refreshes it through the official Microsoft, Xbox and Minecraft services before
starting the game. The project is still a GitHub-only alpha and we can provide
more details or a test build if needed.

## Why this differs from the earlier draft

| Earlier draft | Current implementation |
|---|---|
| Authorization code flow with a native public-client redirect URI | Implemented: Authorization Code + PKCE, loopback redirect, short-lived local listener |
| `sessionserver.mojang.com` profile lookup | `api.minecraftservices.com/minecraft/profile`; sessionserver is not called |
| Ownership implied by profile/login | `/entitlements/mcstore` is checked during sign-in |
| “Before enabling launch” | Ownership is checked during sign-in; online launch now consumes the stored Minecraft session |

Device Code Flow remains available as a fallback for environments where a
loopback listener cannot be started. The submission describes the primary
Authorization Code + PKCE flow. Do not claim `sessionserver.mojang.com` is used.
