namespace Archoera.Domain.Mojang;

/// <summary>
/// One downloadable artifact as described by Mojang metadata. <see cref="Sha1"/>
/// and <see cref="Size"/> stay nullable so the plan producer can reject missing
/// values with a stable code instead of silently emitting an unverifiable file.
/// </summary>
/// <param name="Url">Absolute download URL (may be empty for local Forge artifacts).</param>
/// <param name="Sha1">Lower-case SHA-1, when the metadata supplies one.</param>
/// <param name="Size">Expected bytes, when the metadata supplies it.</param>
/// <param name="Path">
/// Maven-style path relative to <c>libraries/</c>; usually present for vanilla
/// libraries. When absent the builder derives it from the library name.
/// </param>
public sealed record MojangDownload(
    string Url,
    string? Sha1,
    long? Size,
    string? Path = null);

/// <summary><c>assetIndex</c> block of a version JSON.</summary>
/// <param name="Id">Index id, for example <c>5</c> or <c>1.20</c>.</param>
/// <param name="Url">Absolute URL of <c>assets/indexes/&lt;id&gt;.json</c>.</param>
/// <param name="Sha1">SHA-1 of the index JSON.</param>
/// <param name="Size">Size of the index JSON in bytes.</param>
/// <param name="TotalSize">Sum of all asset object sizes (informational).</param>
public sealed record MojangAssetIndexRef(
    string Id,
    string Url,
    string? Sha1,
    long? Size,
    long? TotalSize = null);

/// <summary><c>downloads</c> object of a version JSON.</summary>
public sealed record MojangVersionDownloads(MojangDownload? Client);

/// <summary><c>javaVersion</c> block; the runtime itself is a next-step plan.</summary>
public sealed record MojangJavaVersion(string? Component, int? MajorVersion);

/// <summary>One file in <c>logging.client</c> (Log4Shell mitigation config).</summary>
public sealed record MojangLoggingFile(
    string? Id,
    string Url,
    string? Sha1,
    long? Size,
    string? Type = null);

/// <summary><c>logging</c> block, notably <c>logging.client.file</c>.</summary>
public sealed record MojangLogging(string? Argument, MojangLoggingFile? Client);

/// <summary>
/// A resolved <c>arguments.game[]</c>/<c>arguments.jvm[]</c> entry. Values are
/// launch-time data only; this slice never downloads or substitutes them.
/// </summary>
public sealed record MojangArgument(
    IReadOnlyList<MojangRule> Rules,
    IReadOnlyList<string> Values);

/// <summary>Structured arguments (1.13+). Legacy <c>minecraftArguments</c> is not modelled yet.</summary>
public sealed record MojangArguments(
    IReadOnlyList<MojangArgument> Game,
    IReadOnlyList<MojangArgument> Jvm);

/// <summary>
/// A fully resolved Mojang version JSON. <see cref="InheritsFrom"/> is parsed
/// but never merged in this slice: the plan builder rejects it with
/// <c>UNSUPPORTED_INHERITS</c> so a loader profile cannot silently produce an
/// incomplete plan (docs/08 01 §3).
/// </summary>
public sealed record MojangVersion
{
    /// <summary>Version id used for <c>versions/&lt;id&gt;/&lt;id&gt;.jar</c>.</summary>
    public string Id { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    /// <summary>Loader parent id; non-empty values are rejected by the builder.</summary>
    public string? InheritsFrom { get; init; }

    public string? MainClass { get; init; }

    /// <summary>Asset index id from the legacy <c>assets</c> field.</summary>
    public string? Assets { get; init; }

    public MojangAssetIndexRef? AssetIndex { get; init; }

    public MojangVersionDownloads Downloads { get; init; } = new((MojangDownload?)null);

    public MojangJavaVersion? JavaVersion { get; init; }

    public MojangLogging? Logging { get; init; }

    public IReadOnlyList<MojangLibrary> Libraries { get; init; } = [];

    public MojangArguments? Arguments { get; init; }

    public int? MinimumLauncherVersion { get; init; }

    public int? ComplianceLevel { get; init; }

    public DateTimeOffset? ReleaseTime { get; init; }
}
