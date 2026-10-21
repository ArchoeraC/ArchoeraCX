namespace Archoera.Application.Accounts;

/// <summary>Device-code challenge. DeviceCode is a secret and stays in C#.</summary>
public sealed record MicrosoftDeviceCodeChallenge(
    string FlowId,
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval,
    DateTimeOffset CreatedAt)
{
    public DateTimeOffset ExpiresAt => CreatedAt.AddSeconds(ExpiresIn);
}

/// <summary>
/// Authorization Code + PKCE challenge. CodeVerifier and State are secrets and
/// stay in C#; the UI only receives the authorization URL and flow id.
/// </summary>
public sealed record MicrosoftAuthorizationCodeChallenge(
    string FlowId,
    string AuthorizationUrl,
    string RedirectUri,
    string CodeVerifier,
    string State,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Raw loopback callback payload. Code and State are secrets and are never
/// logged; the caller validates State against the active challenge.
/// </summary>
public sealed record LoopbackOAuthCallback(
    string? Code,
    string? State,
    string? Error,
    string? ErrorDescription);

/// <summary>Microsoft identity token (not a Minecraft token).</summary>
public sealed record MicrosoftTokenSet(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string Scope,
    string TokenType);

public sealed record XboxLiveToken(string Token, string UserHash);

public sealed record XstsToken(string Token, string UserHash);

public sealed record MinecraftAuthenticatedSession(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string Uuid,
    string Name,
    string? Xuid,
    string? SkinUrl,
    string? CapeUrl,
    string? CertificatesJson,
    DateTimeOffset? CertificatesExpireAt);
