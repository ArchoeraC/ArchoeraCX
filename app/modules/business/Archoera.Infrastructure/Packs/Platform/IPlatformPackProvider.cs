using Archoera.Application.Packs;

namespace Archoera.Infrastructure.Packs.Platform;

/// <summary>One registered pack catalogue backend.</summary>
internal interface IPlatformPackProvider
{
    string Id { get; }
    string DisplayName { get; }

    Task<(IReadOnlyList<PlatformPackSummary> Items, int Total)> SearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformPackVersion>> GetVersionsAsync(
        string packId,
        CancellationToken cancellationToken);

    Task<ModpackIndex> BuildIndexAsync(
        string packId,
        string versionId,
        CancellationToken cancellationToken);
}
