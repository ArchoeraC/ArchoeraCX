namespace Archoera.Domain.Downloads;

/// <summary>
/// Executor options carried by <c>options</c> in the plan JSON.
/// </summary>
public sealed record DownloadPlanOptions
{
    /// <summary>Deduplicate committed content in the content store.</summary>
    public bool Dedup { get; init; } = true;

    /// <summary>How an existing destination is compared before skipping.</summary>
    public DownloadVerifyExisting VerifyExisting { get; init; } = DownloadVerifyExisting.Size;

    /// <summary>Commit durability policy.</summary>
    public DownloadDurability Durability { get; init; } = DownloadDurability.Normal;

    /// <summary>Range request threshold in bytes (0 disables the hint).</summary>
    public long RangeThreshold { get; init; } = DownloadPlanLimits.DefaultRangeThreshold;

    /// <summary>Chunk size hint in bytes.</summary>
    public long ChunkSize { get; init; } = DownloadPlanLimits.DefaultChunkSize;

    /// <summary>Progress aggregation interval hint in milliseconds.</summary>
    public int ProgressIntervalMs { get; init; } = DownloadPlanLimits.DefaultProgressIntervalMs;

    /// <summary>Streams allowed per host name; 0 disables the per-host cap.</summary>
    public int PerHostConcurrency { get; init; } = DownloadPlanLimits.DefaultPerHostConcurrency;

    /// <summary>Explicit proxy resolved by the host, or null for direct connections.</summary>
    public DownloadProxyOptions? Proxy { get; init; }

    /// <summary>Defaults matching the Rust implementation.</summary>
    public static DownloadPlanOptions Default { get; } = new();
}
