namespace Archoera.Domain.Downloads;

/// <summary>
/// Hard limits and defaults mirrored from <c>app/modules/downloader/src/plan.rs</c>.
/// Keep these in sync: the Rust validator rejects anything stricter it does not
/// know about, and silently normalizes anything looser.
/// </summary>
public static class DownloadPlanLimits
{
    /// <summary>Only <c>schemaVersion</c> 1 is understood by the executor.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Maximum number of entries in <c>files[]</c>.</summary>
    public const int MaxFiles = 200_000;

    /// <summary>Maximum size of a single file (1 TiB).</summary>
    public const long MaxFileBytes = 1L << 40;

    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 64;
    /// <summary>
    /// Parallel streams: files, or range chunks inside one large file. Mirrors
    /// the Rust default; 16 keeps small files from being latency bound.
    /// </summary>
    public const int DefaultConcurrency = 16;

    /// <summary>Maximum length of <c>planId</c>.</summary>
    public const int MaxPlanIdLength = 256;

    /// <summary>Maximum byte length of a single <c>dest</c>.</summary>
    public const int MaxDestBytes = 4_096;

    /// <summary>Maximum byte length of a path segment.</summary>
    public const int MaxDestSegmentBytes = 255;

    /// <summary>Maximum length of a URL/ mirror.</summary>
    public const int MaxUrlLength = 8_192;

    public const int MaxHeaderNameLength = 128;
    public const int MaxHeaderValueLength = 8 * 1_024;

    /// <summary>Parallel streams per host; mirrors the Rust default.</summary>
    public const int DefaultPerHostConcurrency = 8;
    public const int MaxPerHostConcurrency = 64;

    /// <summary>
    /// Only files above this size use ranged chunks. Minecraft assets are mostly
    /// tiny and libraries mostly a few MiB, so chunking them adds request and
    /// handshake overhead without raising throughput.
    /// </summary>
    public const long DefaultRangeThreshold = 8L * 1024 * 1024;
    public const long DefaultChunkSize = 4L * 1024 * 1024;
    public const int DefaultProgressIntervalMs = 150;
}
