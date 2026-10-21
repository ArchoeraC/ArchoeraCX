using Archoera.Domain.Accounts;

namespace Archoera.Application.Accounts;

public interface IAccountRepository
{
    Task<IReadOnlyList<AccountRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<AccountRecord?> GetAsync(string id, CancellationToken cancellationToken = default);
    Task<AccountRecord?> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<AccountRecord> SaveAsync(AccountRecord account, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<AccountRecord?> SetActiveAsync(string id, CancellationToken cancellationToken = default);
}

public interface IAccountCredentialStore
{
    Task<AccountCredential?> GetCredentialAsync(
        string accountId,
        CancellationToken cancellationToken = default);
    Task SaveCredentialAsync(
        AccountCredential credential,
        CancellationToken cancellationToken = default);
    Task DeleteCredentialAsync(
        string accountId,
        CancellationToken cancellationToken = default);
}
