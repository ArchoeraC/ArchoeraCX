using Archoera.Domain.Resources;

namespace Archoera.Application.Resources;

/// <summary>
/// Version-level compatibility rules (docs/08 §3.2). Project-level
/// <c>loaders</c>/<c>game_versions</c> are historical unions and are never
/// consulted here.
/// </summary>
public static class ResourceCompatibility
{
    /// <summary>
    /// True when <paramref name="version"/> may be installed for
    /// <paramref name="target"/> and <paramref name="kind"/>.
    /// </summary>
    public static bool IsCompatible(
        ResourceVersion? version,
        ResourceKind kind,
        ResourceTarget? target)
    {
        if (version is null || target is null || !target.IsCompleteFor(kind))
        {
            return false;
        }

        if (!target.IncludeSnapshots && IsSnapshot(target.GameVersion))
        {
            return false;
        }

        var gameVersions = version.GameVersions ?? [];
        if (!gameVersions.Any(candidate =>
                string.Equals(candidate, target.GameVersion, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var loaders = version.Loaders ?? [];

        return kind switch
        {
            ResourceKind.Mod => MatchesLoader(loaders, target.NormalizedLoader),
            ResourceKind.Shader => MatchesLoader(loaders, target.NormalizedShaderLoader),
            ResourceKind.ResourcePack => MatchesLoader(loaders, "minecraft"),
            _ => false,
        };
    }

    /// <summary>
    /// True when the version advertises <paramref name="loader"/> at version
    /// level. Quilt does not implicitly accept Fabric mods here.
    /// </summary>
    public static bool MatchesLoader(IReadOnlyList<string>? loaders, string? loader)
    {
        if (loaders is null || string.IsNullOrWhiteSpace(loader))
        {
            return false;
        }

        var wanted = loader.Trim().ToLowerInvariant();
        foreach (var candidate in loaders)
        {
            if (string.Equals(candidate?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Snapshot detection used for the default filter: weekly snapshots
    /// (<c>24w14a</c>), pre-releases/release candidates (<c>1.21.5-pre1</c>,
    /// <c>1.21.5-rc1</c>), old alpha/beta builds and <c>inf-*</c> snapshots.
    /// </summary>
    public static bool IsSnapshot(string? gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return false;
        }

        var value = gameVersion.Trim().ToLowerInvariant();

        if (value.StartsWith("inf-", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.Contains("-pre", StringComparison.Ordinal)
            || value.Contains("-rc", StringComparison.Ordinal))
        {
            return true;
        }

        if (value.Length == 6
            && value[0] is >= '0' and <= '9'
            && value[1] is >= '0' and <= '9'
            && value[2] == 'w'
            && value[3] is >= '0' and <= '9'
            && value[4] is >= '0' and <= '9'
            && value[5] is >= 'a' and <= 'z')
        {
            return true;
        }

        // Legacy alpha/beta identifiers: a1.2.6, b1.7.3, ...
        return value.Length > 3
            && value[0] is 'a' or 'b'
            && value[1] is >= '0' and <= '9'
            && value[2] == '.';
    }
}
