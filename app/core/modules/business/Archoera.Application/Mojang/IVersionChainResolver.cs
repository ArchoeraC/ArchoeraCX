using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Resolves an <c>inheritsFrom</c> chain through a caller-provided lookup. The
/// merge slice never fetches metadata itself, which keeps it independent of the
/// network, the version cache and the loader providers.
/// </summary>
public interface IVersionChainResolver
{
    /// <summary>
    /// Resolves <paramref name="rootId"/> (the leaf/loader profile) and its
    /// ancestors. The result is ordered root (vanilla) to leaf.
    /// </summary>
    /// <param name="rootId">Id of the version to flatten.</param>
    /// <param name="lookup">Resolves one id; <see langword="null"/> means "not found".</param>
    /// <param name="cancellationToken">Cancels lookups between layers.</param>
    Task<IReadOnlyList<VersionLayer>> ResolveAsync(
        string rootId,
        Func<string, CancellationToken, Task<VersionLayer?>> lookup,
        CancellationToken cancellationToken = default);
}
