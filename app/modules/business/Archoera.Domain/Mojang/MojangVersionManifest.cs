namespace Archoera.Domain.Mojang;

/// <summary>
/// <c>version_manifest_v2.json</c>. <c>latest</c> is informational only; a
/// caller must still choose an exact id and let the client validate the URL.
/// </summary>
/// <param name="LatestRelease">Newest release id from <c>latest.release</c>.</param>
/// <param name="LatestSnapshot">Newest snapshot id from <c>latest.snapshot</c>.</param>
/// <param name="Versions">All advertised versions, in manifest order.</param>
public sealed record MojangVersionManifest(
    string LatestRelease,
    string LatestSnapshot,
    IReadOnlyList<MojangVersionSummary> Versions);

/// <summary>One <c>versions[]</c> entry in the version manifest.</summary>
/// <param name="Id">Version id, for example <c>1.20.1</c>.</param>
/// <param name="Type"><c>release</c>, <c>snapshot</c>, <c>old_beta</c>, ...</param>
/// <param name="Url">Absolute HTTPS URL of the version JSON.</param>
/// <param name="Sha1">SHA-1 of the version JSON as published by Mojang.</param>
/// <param name="ReleaseTime">Publication time, when present.</param>
/// <param name="ComplianceLevel">Mojang compliance level (0/1).</param>
/// <param name="Size">
/// Expected version JSON size. The published manifest normally omits it; the
/// field exists so a proxy/mirror that adds it can be verified too.
/// </param>
public sealed record MojangVersionSummary(
    string Id,
    string Type,
    string Url,
    string Sha1,
    DateTimeOffset? ReleaseTime = null,
    int ComplianceLevel = 0,
    long? Size = null);
