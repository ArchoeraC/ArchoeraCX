namespace Archoera.Domain.Settings;

/// <summary>
/// User-visible storage and runtime locations. The data root owns
/// settings/cache/logs; instances can live on multiple drives or folders.
/// </summary>
/// <param name="InstancesRoot">Primary instances directory (legacy alias).</param>
/// <param name="DataRoot">Absolute application data directory (read-only here).</param>
/// <param name="SettingsPath">Where this settings document is stored (diagnostics only).</param>
/// <param name="InstancesRoots">All configured instance directories; first is the default.</param>
/// <param name="JavaPath">Manually selected Java executable, or null for automatic selection.</param>
/// <param name="JavaAuto">True when launch should choose Java automatically.</param>
/// <param name="DownloadSource">`auto` (region based), `official` or `mirror`.</param>
/// <param name="DownloadConcurrency">Global download streams (1..64).</param>
/// <param name="DownloadPerHost">Streams per host (0..64, 0 = unlimited).</param>
/// <param name="DownloadProxy">Manual proxy URL (http/https); null follows the system.</param>
/// <param name="WindowWidth">Game window width in pixels when not fullscreen.</param>
/// <param name="WindowHeight">Game window height in pixels when not fullscreen.</param>
/// <param name="Fullscreen">Starts the game with the window maximized to fullscreen.</param>
/// <param name="DownloadChunkSizeKb">Chunk size for ranged downloads, in KiB.</param>
/// <param name="DownloadRangeThresholdKb">Files above this size use ranged chunks (0 disables).</param>
/// <param name="LogRetentionFiles">Launch logs kept per instance.</param>
/// <param name="LogRetentionMegabytes">Total launch-log budget per instance, in MiB.</param>
public sealed record AppSettings(
    string InstancesRoot,
    string DataRoot,
    string SettingsPath,
    IReadOnlyList<string>? InstancesRoots = null,
    string? JavaPath = null,
    bool JavaAuto = true,
    int MemoryMinMb = 512,
    int MemoryMaxMb = 2048,
    string? JvmArgs = null,
    string GcMode = "auto",
    string? GameArgs = null,
    string DownloadSource = "auto",
    int DownloadConcurrency = 16,
    int DownloadPerHost = 16,
    string? DownloadProxy = null,
    int WindowWidth = 854,
    int WindowHeight = 480,
    bool Fullscreen = false,
    int DownloadChunkSizeKb = 512,
    int DownloadRangeThresholdKb = 1024,
    int LogRetentionFiles = 10,
    int LogRetentionMegabytes = 32)
{
    /// <summary>Current settings document schema.</summary>
    public const int CurrentSchemaVersion = 9;

    /// <summary>Configured roots, falling back to the legacy primary root.</summary>
    public IReadOnlyList<string> Roots =>
        InstancesRoots is { Count: > 0 } ? InstancesRoots : [InstancesRoot];
}
