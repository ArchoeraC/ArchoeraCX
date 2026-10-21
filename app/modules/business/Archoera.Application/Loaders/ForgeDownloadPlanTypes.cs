using Archoera.Domain.Downloads;

namespace Archoera.Application.Loaders;

/// <summary>
/// Stable error codes for the Forge/NeoForge installer download producer and
/// the Maven sidecar client. They are safe to log and to map to UI text.
/// </summary>
public static class ForgeDownloadErrorCode
{
    /// <summary>The caller supplied an unusable root/version/URL.</summary>
    public const string InvalidRequest = "INVALID_REQUEST";

    /// <summary>Installer metadata is structurally valid but cannot produce a safe plan.</summary>
    public const string InvalidMetadata = "INVALID_METADATA";

    /// <summary>A file that must be downloaded has no usable hash.</summary>
    public const string MissingHash = "MISSING_HASH";

    /// <summary>A file that must be downloaded has no size (or no Content-Length).</summary>
    public const string MissingSize = "MISSING_SIZE";

    /// <summary>Two sources mapped different bytes onto the same destination.</summary>
    public const string DuplicateDest = "DUPLICATE_DEST";

    /// <summary>The local installer path does not exist.</summary>
    public const string InstallerMissing = "INSTALLER_MISSING";

    /// <summary>The installer is not a readable zip/jar or lacks a required entry.</summary>
    public const string InstallerInvalid = "INSTALLER_INVALID";

    /// <summary>The Maven sidecar/artifact returned 404 or 410.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>Unexpected HTTP status, malformed sidecar or malformed metadata.</summary>
    public const string InvalidResponse = "INVALID_RESPONSE";

    /// <summary>Transport failure (DNS/TLS/timeout), never a caller cancellation.</summary>
    public const string NetworkError = "NETWORK_ERROR";
}

/// <summary>
/// Forge/NeoForge download producer failure with a stable code. Messages contain
/// URLs and paths, never credentials.
/// </summary>
public sealed class ForgeDownloadException : Exception
{
    public ForgeDownloadException(
        string code,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>One of <see cref="ForgeDownloadErrorCode"/>.</summary>
    public string Code { get; }

    /// <summary>HTTP status when the failure came from a response.</summary>
    public int? StatusCode { get; }
}

/// <summary>
/// The installer jar as described by the download source: official Maven URL,
/// published SHA-1 and size, plus optional byte-identical mirrors.
/// </summary>
/// <param name="Url">Primary HTTPS URL (loopback HTTP is accepted for tests).</param>
/// <param name="Sha1">Published 40-character SHA-1 of the installer bytes.</param>
/// <param name="Size">Published size in bytes; required by the executor.</param>
/// <param name="Mirrors">Optional byte-identical mirrors.</param>
public sealed record ForgeInstallerDescriptor(
    string Url,
    string Sha1,
    long? Size,
    IReadOnlyList<string>? Mirrors = null);

/// <summary>Producer knobs; defaults match docs/08 §4.3 (no generated files).</summary>
public sealed record ForgeDownloadPlanOptions
{
    /// <summary>
    /// Caller-supplied instance-relative installer destination. When null the
    /// default is <c>downloads/loaders/&lt;kind&gt;/&lt;game&gt;-&lt;loader&gt;-installer.jar</c>.
    /// </summary>
    public string? InstallerRelativePath { get; init; }

    /// <summary>
    /// Include <c>version.json.downloads.client</c>. Default false because the
    /// vanilla client jar belongs to the Mojang plan (docs/10 §4.3).
    /// </summary>
    public bool IncludeClientJar { get; init; }

    /// <summary>Defaults: generated installer destination, no client jar.</summary>
    public static ForgeDownloadPlanOptions Default { get; } = new();
}

/// <summary>Stable warning codes emitted when a library is intentionally skipped.</summary>
public static class ForgeDownloadWarningCode
{
    /// <summary>An empty <c>downloads.artifact.url</c>: local/generated, never downloaded.</summary>
    public const string EmptyUrl = "EMPTY_URL";

    /// <summary>No <c>downloads.artifact</c>: embedded in the installer or maven/ extracted locally.</summary>
    public const string EmbeddedLibrary = "EMBEDDED_LIBRARY";

    /// <summary>Only the installer could be planned because no installer reader was supplied.</summary>
    public const string InstallerMetadataUnavailable = "INSTALLER_METADATA_UNAVAILABLE";
}

/// <summary>One non-fatal diagnostic produced while planning.</summary>
public sealed record ForgeDownloadPlanWarning(string Code, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>A validated download plan plus diagnostics for skipped entries.</summary>
/// <param name="Plan">Plan containing only verifiable remote files.</param>
/// <param name="Warnings">Skipped embedded/empty-url libraries and similar notes.</param>
public sealed record ForgeDownloadPlanResult(
    DownloadPlan Plan,
    IReadOnlyList<ForgeDownloadPlanWarning> Warnings);
