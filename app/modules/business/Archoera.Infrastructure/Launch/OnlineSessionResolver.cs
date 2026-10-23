using Archoera.Application.Accounts;
using Archoera.Domain.Accounts;
using Archoera.Domain.Instances;
using Archoera.Infrastructure.Accounts;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Resolves the launchable Microsoft session. A still-valid Minecraft token
/// is reused; otherwise the Microsoft refresh token is exchanged for a new
/// Minecraft session and both stores are updated. Tokens are never logged.
/// </summary>
internal sealed class OnlineSessionResolver(
    MicrosoftAuthClient auth,
    IAccountCredentialStore credentials,
    JsonAccountStore accounts)
{
    /// <summary>
    /// Read-only readiness probe. A credential is usable when it still has a
    /// Minecraft access token and either is not near expiry or can be refreshed.
    /// </summary>
    internal async Task<bool> HasUsableCredentialAsync(
        string accountId,
        CancellationToken cancellationToken)
    {
        AccountCredential? credential;
        try
        {
            credential = await credentials
                .GetCredentialAsync(accountId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CredentialVaultException)
        {
            // Prepare is advisory; launch.start reports the vault error.
            return false;
        }

        if (credential is null || string.IsNullOrWhiteSpace(credential.MinecraftAccessToken))
        {
            return false;
        }

        var expiresAt = credential.MinecraftAccessTokenExpiresAt;
        return credential.HasRefreshToken ||
               (expiresAt is not null &&
                expiresAt > DateTimeOffset.UtcNow.AddMinutes(2));
    }

    internal async Task<OnlineSession> ResolveAsync(
        AccountRecord account,
        CancellationToken cancellationToken)
    {
        if (account.Kind != AccountKind.Microsoft ||
            string.IsNullOrWhiteSpace(account.Uuid))
        {
            throw new LaunchException(
                "MICROSOFT_ACCOUNT_REQUIRED",
                "Online launch requires an active Microsoft account with a Minecraft profile.");
        }

        var clientId = auth.Options.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new LaunchException(
                "MICROSOFT_CLIENT_ID_MISSING",
                "No Microsoft client ID is configured, so online launch is unavailable.");
        }

        var credential = await credentials
            .GetCredentialAsync(account.Id, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null ||
            string.IsNullOrWhiteSpace(credential.MinecraftAccessToken))
        {
            throw new LaunchException(
                "AUTH_REQUIRED",
                "No stored Minecraft session for this account. Sign in again.");
        }

        var expiresAt = credential.MinecraftAccessTokenExpiresAt;
        var needsRefresh = expiresAt is null ||
                           expiresAt <= DateTimeOffset.UtcNow.AddMinutes(2);
        if (!needsRefresh)
        {
            return new OnlineSession(account, credential.MinecraftAccessToken);
        }

        if (!credential.HasRefreshToken)
        {
            throw new LaunchException(
                "AUTH_REQUIRED",
                "The Minecraft session expired. Sign in again.");
        }

        MicrosoftTokenSet microsoftTokens;
        MinecraftAuthenticatedSession session;
        try
        {
            microsoftTokens = await auth
                .RefreshMicrosoftAsync(credential.MicrosoftRefreshToken!, cancellationToken)
                .ConfigureAwait(false);
            session = await auth
                .CompleteMinecraftLoginAsync(microsoftTokens, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AccountAuthException exception)
        {
            throw new LaunchException(exception.Code, exception.Message);
        }

        var refreshToken = microsoftTokens.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new LaunchException(
                "AUTH_REQUIRED",
                "Microsoft returned no reusable refresh token. Sign in again.");
        }

        var now = DateTimeOffset.UtcNow;
        var updated = new AccountRecord(
            account.Id,
            AccountKind.Microsoft,
            session.Name,
            session.Uuid,
            session.Xuid,
            session.SkinUrl,
            session.CapeUrl,
            account.Active,
            account.CreatedAt,
            now,
            now);
        await accounts.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        await credentials.SaveCredentialAsync(
                new AccountCredential(
                    account.Id,
                    refreshToken,
                    session.AccessToken,
                    session.ExpiresAt,
                    session.CertificatesJson,
                    now),
                cancellationToken)
            .ConfigureAwait(false);

        return new OnlineSession(updated, session.AccessToken);
    }
}

internal sealed record OnlineSession(AccountRecord Account, string AccessToken);
