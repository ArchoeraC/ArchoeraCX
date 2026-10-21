using System.Text.Json;
using System.Text.Json.Nodes;
using Archoera.Domain.Mojang;

namespace Archoera.Application.Mojang;

/// <summary>
/// Default chain resolver. It only reads <c>inheritsFrom</c> from each layer;
/// it does not inspect libraries, arguments or loader-specific fields.
/// </summary>
public sealed class VersionChainResolver : IVersionChainResolver
{
    /// <summary>Maximum number of layers in a chain (docs/08 01 §4.3).</summary>
    public const int MaxDepth = 8;

    /// <inheritdoc />
    public async Task<IReadOnlyList<VersionLayer>> ResolveAsync(
        string rootId,
        Func<string, CancellationToken, Task<VersionLayer?>> lookup,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootId))
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidRequest,
                "root version id is required");
        }

        ArgumentNullException.ThrowIfNull(lookup);

        var layers = new List<VersionLayer>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var id = rootId.Trim();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (visited.Contains(id))
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.ChainCycle,
                    $"version chain revisits '{id}'");
            }

            if (visited.Count >= MaxDepth)
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.ChainTooDeep,
                    $"version chain exceeds {MaxDepth} layers at '{id}'");
            }

            VersionLayer? layer = await lookup(id, cancellationToken).ConfigureAwait(false);

            if (layer is null)
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.ChainMissing,
                    $"version '{id}' could not be resolved");
            }

            if (!string.IsNullOrWhiteSpace(layer.Id)
                && !string.Equals(layer.Id, id, StringComparison.Ordinal))
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.ChainIdMismatch,
                    $"lookup for '{id}' returned layer '{layer.Id}'");
            }

            if (string.IsNullOrWhiteSpace(layer.Json))
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.InvalidLayerJson,
                    $"version '{id}' has empty JSON");
            }

            var normalized = string.IsNullOrWhiteSpace(layer.Id) ? layer with { Id = id } : layer;
            visited.Add(id);
            layers.Add(normalized);

            var parent = ReadInheritsFrom(normalized);
            if (parent is null)
            {
                break;
            }

            if (string.Equals(parent, id, StringComparison.Ordinal))
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.ChainSelfReference,
                    $"version '{id}' inherits itself");
            }

            if (visited.Contains(parent))
            {
                throw new VersionMergeException(
                    VersionMergeErrorCode.ChainCycle,
                    $"version '{id}' inherits '{parent}', which is already in the chain");
            }

            id = parent;
        }

        // The resolver walked leaf -> root; the merge pass expects root -> leaf.
        layers.Reverse();
        return layers;
    }

    private static string? ReadInheritsFrom(VersionLayer layer)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(layer.Json);
        }
        catch (JsonException exception)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{layer.Id}' is not valid JSON: {exception.Message}",
                exception);
        }

        if (node is not JsonObject root)
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{layer.Id}' must be a JSON object");
        }

        if (!root.TryGetPropertyValue("inheritsFrom", out var parent) || parent is null)
        {
            return null;
        }

        if (parent is not JsonValue value || !value.TryGetValue<string>(out var parentId))
        {
            throw new VersionMergeException(
                VersionMergeErrorCode.InvalidLayerJson,
                $"version '{layer.Id}' has a non-string inheritsFrom");
        }

        return string.IsNullOrWhiteSpace(parentId) ? null : parentId.Trim();
    }
}
