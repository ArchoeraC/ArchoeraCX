using Archoera.Application.Accounts;
using Archoera.Domain.Accounts;

namespace Archoera.Infrastructure.Accounts;

/// <summary>
/// Authoritative premium gate. Metadata alone is not enough: a Microsoft
/// account must also have a stored credential that is still usable. When the
/// Minecraft token is expired, the gate refreshes it through the official
/// Microsoft/Xbox/Minecraft chain and rechecks entitlement before accepting it.
/// </summary>
public sealed class MicrosoftAccountGate
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);

    private readonly IAccountRepository _accounts;
    private readonly IAccountCredentialStore _credentials;
    private readonly MicrosoftAuthClient _auth;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MicrosoftAccountGate(
        IAccountRepository accounts,
        IAccountCredentialStore credentials,
        MicrosoftAuthClient auth)
    {
        _accounts = accounts;
        _credentials = credentials;
        _auth = auth;
    }

    /// <summary>Returns true when at least one Microsoft account is verified.</summary>
    public async Task<bool> HasValidAccountAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var accounts = await _accounts.ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var account in accounts)
            {
                if (account.Kind != AccountKind.Microsoft ||
                    string.IsNullOrWhiteSpace(account.Uuid))
                {
                    continue;
                }

                try
                {
                    if (await ValidateAsync(account, cancellationToken).ConfigureAwait(false))
                    {
                        return true;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (AccountAuthException)
                {
                    // Try the next Microsoft account. Revoked/expired accounts
                    // must not unlock offline or third-party authentication.
                }
                catch (CredentialVaultException)
                {
                    // A broken credential is not a valid premium account.
                }
                catch (Exception)
                {
                    // Network/protocol failure while refreshing is not a valid
                    // verification; try another account and fail closed.
                }
            }

            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ValidateAsync(
        AccountRecord account,
        CancellationToken cancellationToken)
    {
        var credential = await _credentials
            .GetCredentialAsync(account.Id, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return false;
        }

        var expiresAt = credential.MinecraftAccessTokenExpiresAt;
        if (!string.IsNullOrWhiteSpace(credential.MinecraftAccessToken) &&
            expiresAt is not null &&
            expiresAt > DateTimeOffset.UtcNow.Add(RefreshWindow))
        {
            return true;
        }

        if (!credential.HasRefreshToken)
        {
            return false;
        }

        var microsoftToken = await _auth
            .RefreshMicrosoftAsync(credential.MicrosoftRefreshToken!, cancellationToken)
            .ConfigureAwait(false);
        var session = await _auth
            .CompleteMinecraftLoginAsync(microsoftToken, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(microsoftToken.RefreshToken))
        {
            return false;
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
        await _accounts.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        await _credentials.SaveCredentialAsync(
                new AccountCredential(
                    account.Id,
                    microsoftToken.RefreshToken,
                    session.AccessToken,
                    session.ExpiresAt,
                    session.CertificatesJson,
                    now),
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
