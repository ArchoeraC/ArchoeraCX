using Archoera.Domain.Instances;

namespace Archoera.Application.Instances;

/// <summary>Instance metadata/index owner contract.</summary>
public interface IInstanceRepository
{
    Task<IReadOnlyList<GameInstanceRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<GameInstanceRecord> CreateAsync(
        string name,
        string gameVersion,
        string loader,
        string loaderVersion,
        string? rootPath,
        CancellationToken cancellationToken = default);
    Task<GameInstanceRecord?> UpdateAsync(
        string id,
        string? name,
        bool? favorite,
        DateTimeOffset? lastPlayedAt,
        InstanceOverrides? overrides = null,
        bool setOverrides = false,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task<InstanceSyncReport> SyncAsync(
        IReadOnlyList<string>? roots,
        bool dryRun,
        CancellationToken cancellationToken = default);
}
