using System.Text.Json;
using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Produces the single effective version for an <c>inheritsFrom</c> chain.
/// This is the only merge producer in the C# business module; callers supply
/// layers through an async lookup and never see partially merged JSON.
/// </summary>
public sealed class VersionMergeService
{
    private readonly IVersionChainResolver _resolver;

    public VersionMergeService(IVersionChainResolver? resolver = null)
    {
        _resolver = resolver ?? new VersionChainResolver();
    }

    /// <summary>
    /// Resolves the chain then merges it. The returned
    /// <see cref="EffectiveVersion.Version"/> has been parsed by
    /// <see cref="MojangJson.ParseVersion"/> and always has a null
    /// <see cref="MojangVersion.InheritsFrom"/>.
    /// </summary>
    public async Task<EffectiveVersion> MergeAsync(
        string rootId,
        Func<string, CancellationToken, Task<VersionLayer?>> lookup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        var chain = await _resolver
            .ResolveAsync(rootId, lookup, cancellationToken)
            .ConfigureAwait(false);

        return Merge(chain);
    }

    /// <summary>Convenience wrapper returning only the compact effective JSON.</summary>
    public async Task<byte[]> MergeJsonAsync(
        string rootId,
        Func<string, CancellationToken, Task<VersionLayer?>> lookup,
        CancellationToken cancellationToken = default)
    {
        var effective = await MergeAsync(rootId, lookup, cancellationToken)
            .ConfigureAwait(false);
        return effective.Json;
    }

    /// <summary>
    /// Merges an already resolved root-to-leaf chain. Parsing the result here
    /// catches a structurally broken merge before it reaches the plan builder.
    /// </summary>
    public EffectiveVersion Merge(IReadOnlyList<VersionLayer> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var result = VersionMergeJson.Build(chain);

        MojangVersion version;
        try
        {
            using var document = JsonDocument.Parse(result.Json);
            version = MojangJson.ParseVersion(document.RootElement);
        }
        catch (Exception exception) when (
            exception is JsonException or MojangException or ArgumentException)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidMergedVersion,
                "merged version JSON is invalid: " + exception.Message,
                exception);
        }

        if (version.InheritsFrom is not null)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidMergedVersion,
                $"merged version '{result.Id}' still carries inheritsFrom");
        }

        return new EffectiveVersion(
            result.Id,
            result.Chain,
            result.Json,
            result.Warnings,
            version,
            result.MergeSchema);
    }
}
