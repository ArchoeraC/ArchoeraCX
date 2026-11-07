namespace Archoera.Application.Packs;

/// <summary>One modpack row returned by a non-Modrinth platform provider.</summary>
public sealed record PlatformPackSummary(
    string Provider,
    string Id,
    string Name,
    string Summary,
    string? IconUrl = null,
    string? Author = null,
    long Downloads = 0,
    DateTimeOffset? UpdatedAt = null,
    string? WebsiteUrl = null);

/// <summary>One concrete pack version from a platform provider.</summary>
/// <param name="DirectArchive">
/// True when the provider publishes one direct zip and no per-file manifest
/// hash. The UI warns before installing it.
/// </param>
public sealed record PlatformPackVersion(
    string Provider,
    string PackId,
    string VersionId,
    string Name,
    string GameVersion,
    string Loader,
    string LoaderVersion,
    string VersionType = "release",
    DateTimeOffset? PublishedAt = null,
    bool DirectArchive = false);
