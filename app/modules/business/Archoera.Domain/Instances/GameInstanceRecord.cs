namespace Archoera.Domain.Instances;

/// <summary>
/// Portable instance metadata. The authoritative copy lives in
/// <c>&lt;instance root&gt;/.archoera/instance.json</c>; the data-root registry is
/// only an index used to find custom locations.
/// </summary>
public sealed record GameInstanceRecord(
    string Id,
    string Name,
    string GameVersion,
    string Loader,
    string LoaderVersion,
    string RootPath,
    int ModCount,
    bool Favorite,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastPlayedAt,
    InstanceOverrides? Overrides = null)
{
    public const int CurrentSchemaVersion = 2;
}
